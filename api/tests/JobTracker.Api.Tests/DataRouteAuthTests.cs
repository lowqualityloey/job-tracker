using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-055 — **every data route rejects anonymity, the stream included.** Spec §2.1; AC-1; test-plan row `-055`.
///
/// ## Why this behaviour runs out of ladder order, on the record
///
/// The plan sequenced this as `-055`, after `-052` ("the pre-login cookie is dead, **proven by replaying the old value**")
/// and `-054` ("the old cookie then gets `401`"). **Neither of those is observable without this gate** — before `-055`,
/// a replayed cookie and no cookie at all produce the same `200`, so both p0 behaviours would have had to assert
/// something weaker than their own rows say. Running the gate first is the correction; the ladder's IDs stay as written so
/// the reorder is visible rather than edited away.
///
/// ## Why a `[Theory]` over all six, and why the stream is one of them
///
/// `-046` is the argument, from M3: the SSE endpoint was added to the catalog long after the CRUD routes existed, so
/// anything "declared once near the routes" is exactly the thing someone forgets on the endpoint they add next. AC-1 says
/// the same thing: *an attribute typo on one endpoint is invisible to every other test.* So the theory enumerates methods
/// rather than trusting a shared convention, and `/api/applications/events` is asserted explicitly.
///
/// ## The exception clause is asserted too
///
/// §2.1 excludes `/api/auth/login`. A gate that also blocks the login route is untestable-by-construction — it can never
/// be authenticated — so it would look like a working system that nobody can enter. That is asserted below, not assumed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DataRouteAuthTests(PostgresFixture postgres)
{
    private const string Email = "gate-tests@example.test";
    private const string Password = "the-gate-test-password-2f6a";
    private static readonly Guid SomeId = Guid.Parse("7c9f1f2e-3b4a-4d5e-9f60-112233445566");

    private sealed class GateFactory(string cs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", cs);
            builder.UseSetting("Auth:Bootstrap:Email", Email);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
        }
    }

    /// <summary>An **anonymous** client: no cookie, no login. The whole point is what the server does without one.</summary>
    private HttpClient Anonymous() => new GateFactory(postgres.ConnectionString).CreateClient();

    [Theory]
    [InlineData("GET", "/api/applications")]
    [InlineData("GET", "/api/applications/7c9f1f2e-3b4a-4d5e-9f60-112233445566")]
    [InlineData("GET", "/api/applications/events")]
    [InlineData("POST", "/api/applications")]
    [InlineData("PUT", "/api/applications/7c9f1f2e-3b4a-4d5e-9f60-112233445566")]
    [InlineData("DELETE", "/api/applications/7c9f1f2e-3b4a-4d5e-9f60-112233445566")]
    public async Task Every_data_route_answers_401_anonymously(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        // Writes need a body to be a well-formed attempt; the 401 must arrive regardless, and a 415/400 from the binder
        // would let "rejected for the wrong reason" masquerade as the gate working.
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new
            {
                id = SomeId,
                companyName = "Gate",
                jobTitle = "Engineer",
                status = "Applied"
            });
        }

        var response = await Anonymous().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.Ordinal);
        // The code is the discriminator DECISION-m3-backend-api-004 requires: a 401 without it is a status the client's
        // adapter cannot map, and "fail closed on unknown code" would turn a working gate into a broken screen.
        Assert.Contains("\"unauthorized\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stream_is_refused_before_any_event_is_written()
    {
        // The 200-with-immediate-EOF case is the one that fools a status-code assertion: SSE responses commit headers
        // eagerly, so a gate applied after the first write yields 200 + `text/event-stream` and then nothing. Asserting
        // the content type here is what distinguishes "refused" from "opened and then abandoned".
        var response = await Anonymous().GetAsync("/api/applications/events", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("text/event-stream", response.Content.Headers.ContentType?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_stays_reachable_anonymously_and_grants_access()
    {
        var http = Anonymous();
        var denied = await http.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        // The exception clause in §2.1 is only meaningful if the gate on the other side actually works, so this is one
        // test and not two: login must be reachable without a session, and what it issues must satisfy the gate.
        // HttpClient in this factory does not manage a cookie jar, so the header is replayed by hand -- which also
        // proves the gate reads the cookie this endpoint writes, rather than some other notion of "authenticated".
        var cookie = login.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.First().Split(';', 2)[0]
            : null;
        Assert.NotNull(cookie);

        using var authorised = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        authorised.Headers.Add("Cookie", cookie!);
        var granted = await http.SendAsync(authorised);

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
    }

    [Fact]
    public async Task A_garbage_session_value_is_401_rather_than_500()
    {
        // Gap 12's class again, one layer up: the gate parses attacker-controlled input on every request. A session id
        // that is not a Guid must be rejected, not crash the pipeline -- and a 500 here would also be a way to probe the
        // gate's internals.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.Add("Cookie", "__Host-JTSession=not-a-guid-at-all");

        var response = await Anonymous().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
