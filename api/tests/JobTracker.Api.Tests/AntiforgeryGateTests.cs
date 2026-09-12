using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.Api.Tests;

/// <summary>
/// <see cref="TDD-EXEC-m4-authentication-063"/> — AC-11's antiforgery gate at its API seam.
///
/// The ladder row is written for the **Browser** seam, and that is where the two ratified cases run. This file is not a
/// substitute for them; it is the half that can only exist as a test, because the property it pins — *a request that
/// arrives with a valid session and no token is refused, and the same request with the token is served* — needs a
/// server-side check to fail against before any browser can be pointed at it.
///
/// Why a session cookie is not enough, and why DECISION-m4-auth-006 is therefore load-bearing: <c>SameSite=Lax</c> stops
/// a cross-site *POST* from a form, but it does not stop a cross-site request that the browser is willing to send with
/// cookies attached, and the deployment plan puts the front end on a different origin, where Lax stops helping entirely.
/// The token closes that gap: it is a value the server can verify and a foreign page cannot read or forge, and it must
/// arrive in a **header**, because a header is the one thing a cross-site form or <c>fetch</c> without CORS cooperation
/// cannot set.
///
/// The two guards this row exists to avoid:
///   · **A check that never runs.** Rejecting everything looks secure in a test that only asserts rejection, so the
///     positive control is mandatory: *the same call, with the token, succeeds*. AC-11's wording says so in bold, and a
///     broken endpoint otherwise passes as a secured one.
///   · **A check aimed at the wrong verb.** The datum from the spike: `POST /api/auth/login` carries no token and answers
///     `204`, so a case that probed login would "prove" antiforgery by exercising a route exempt from it. Every
///     rejection case below therefore targets a state-changing write that the gate is actually meant to cover.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AntiforgeryGateTests(PostgresFixture postgres)
{
    private const string Email = "antiforgery-tests@example.test";
    private const string Password = "the-antiforgery-test-password-9c4b";
    private const string Header = "X-CSRF-Token";
    private const string TokenCookieName = "__Host-JTCsrf";

    private sealed class AntiforgeryFactory(string cs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", cs);
            builder.UseSetting("Auth:Bootstrap:Email", Email);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
        }
    }

    private HttpClient Client() => new AntiforgeryFactory(postgres.ConnectionString).CreateClient();

    /// <summary>
    /// Logs in and returns the two things a real client holds: the session cookie and the antiforgery token, both read
    /// out of the login response's own <c>Set-Cookie</c> headers. Nothing here is invented — a token the test computed
    /// itself would prove the test's arithmetic, not the server's.
    /// </summary>
    private static async Task<(string SessionCookie, string? Token)> LoginAsync(HttpClient http)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var setCookies = login.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : [];

        string? Pair(string name) => setCookies
            .Select(c => c.Split(';', 2)[0])
            .FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal));

        var session = Pair("__Host-JTSession") ?? throw new InvalidOperationException("login issued no session cookie");
        var token = Pair(TokenCookieName)?.Substring(TokenCookieName.Length + 1);
        return (session, token);
    }

    // Raw JSON rather than a typed object: `JsonContent.Create` uses `JsonSerializerOptions.Default`, which is
    // PascalCase, while the API's contract is camelCase — a mismatch that would fail validation on every case and look
    // like a gate that works. `-069` is the row that made hand-written bodies in this file worth their bytes.
    //
    // `id` is present because the M3 contract has the CLIENT generate it (the same payload
    // `ApplicationsCommandTests.Non_json_body_rejected` sends), and its absence is a `400 validation` that arrived on
    // this row's first run masquerading as a gate decision. A write case that cannot succeed cannot discriminate a
    // refusal, so the id is fresh per request: a repeated one would answer 409 for the right reason about the wrong
    // subject.
    private static string BodyJson =>
        $$"""{"id":"{{Guid.NewGuid()}}","companyName":"Vandelay Industries","jobTitle":"Import/Export Manager","location":"New York","status":"Saved"}""";

    private static async Task<HttpResponseMessage> WriteAsync(
        HttpClient http, string sessionCookie, string? token, bool sendHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/applications")
        {
            Content = new StringContent(BodyJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", sessionCookie);
        // The distinction the whole row turns on: a request can carry the *cookie* and still be a forgery, so the gate
        // must look at the header. `sendHeader: false` reproduces exactly what a cross-site page can produce.
        if (sendHeader && token is not null)
        {
            request.Headers.Add(Header, token);
        }

        return await http.SendAsync(request);
    }

    [Fact]
    public async Task Valid_session_without_the_header_is_refused()
    {
        var http = Client();
        var (session, token) = await LoginAsync(http);
        Assert.NotNull(token); // the next case's precondition; asserted so a missing token never reads as "refused"

        var refused = await WriteAsync(http, session, token, sendHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("antiforgery", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_same_write_with_the_issued_token_succeeds()
    {
        // AC-11's positive control, and the reason the refusal above is not vacuous. Without this case, "the gate
        // rejects everything" and "the gate exists" are the same observation.
        var http = Client();
        var (session, token) = await LoginAsync(http);

        var served = await WriteAsync(http, session, token);

        Assert.Equal(HttpStatusCode.Created, served.StatusCode);
    }

    [Fact]
    public async Task Login_needs_no_token_and_still_answers_204()
    {
        // The datum that shaped the restatement: login is exempt, because a page that is not yet authenticated has
        // nothing to bind a token to. Pinned so the new gate cannot quietly widen itself over the route that issues it
        // — the mistake `SessionGate.IsProtected` was written to avoid for the session gate, one layer up.
        var http = Client();
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.False(login.Headers.Contains(Header));
    }

    [Fact]
    public async Task A_safe_verb_needs_no_token()
    {
        // GET is the method a cross-site page *can* issue with cookies attached, and it is harmless precisely because
        // M4 has no state-changing GETs — the rule DECISION-006 states so the next endpoint cannot break it silently.
        // If a future GET ever mutates, this case is where the omission shows up.
        var http = Client();
        var (session, _) = await LoginAsync(http);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.Add("Cookie", session);
        var res = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task A_token_belonging_to_another_session_is_refused()
    {
        // Binding, not presence. A token minted for session B must not satisfy session A, or the value is a global
        // password: stealable once and reusable forever, which is the property antiforgery is supposed to remove.
        var http = Client();
        var (sessionA, _) = await LoginAsync(http);
        var (_, tokenB) = await LoginAsync(Client());

        var refused = await WriteAsync(http, sessionA, tokenB);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_header_present_but_wrong_is_refused()
    {
        // Proves the gate compares a value rather than testing for the header's existence — an `if (Headers.Contains…)`
        // passes five cases above and is worthless.
        var http = Client();
        var (session, _) = await LoginAsync(http);

        var refused = await WriteAsync(http, session, "not-a-token-at-all");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_token_cookie_as_well_as_the_session()
    {
        // Not tidiness. A stale token cookie outliving its session is a value an attacker can keep replaying against a
        // future session of the same user if the token were ever reused; the clearing variant is what makes logout
        // mean "nothing you held is valid".
        var http = Client();
        var (session, _) = await LoginAsync(http);

        // Cookie attached: `/api/auth/logout` is *inside* the gate (`SessionGate.IsProtected` exempts login and
        // nothing else), so sending it anonymously produced `401` on the first run — which is the gate working, and
        // would have been read as the token not being cleared.
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", session);
        var logout = await http.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = logout.Headers.TryGetValues("Set-Cookie", out var values)
            && values.Any(v => v.StartsWith(TokenCookieName + "=", StringComparison.Ordinal));
        Assert.True(cleared, "logout cleared the session cookie but left the antiforgery cookie set");
    }
}
