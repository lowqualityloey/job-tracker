using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-067 / AC-16 — **credentialed CORS is explicit, not incidental**: the exact origin plus
/// <c>Access-Control-Allow-Credentials: true</c>, and never <c>*</c>. Seam Integration, p0.
///
/// ## Why "silently" is the whole point
///
/// <c>M3RegressionNetTests</c>' predecessor (<c>CorsContractTests</c>) already proves the shape of a preflight answer:
/// the origin is echoed, the requested method and headers are granted. What it cannot prove is the line M4 made
/// load-bearing. <c>-050</c> put a session cookie on the wire, and a cross-origin request carrying it is governed by a
/// pair, not by one header:
///
///   * <c>Access-Control-Allow-Origin: http://frontend.test</c> alone means "anyone may read this, no identity attached".
///   * <c>Access-Control-Allow-Credentials: true</c> is what tells the browser it may attach the cookie.
///   * and the two are **incompatible with a wildcard**: a response saying "every origin" plus "bring your identity" is
///     refused by the browser with a network error that carries no HTTP status and no readable body.
///
/// That last bullet is the reason AC-16 exists as a behaviour and not as a comment. ASP.NET Core will happily let you
/// write the wildcard-with-credentials configuration, and the failure appears only in a browser, only for cross-origin
/// users, as <c>TypeError: Failed to fetch</c> — indistinguishable from the server being down. Which is exactly the class
/// of bug the M3 CORS work kept hitting: the local client used a dev proxy, so same-origin code never exercised the
/// headers at all.
///
/// ## The half this file cannot cover
///
/// The ladder row names two seams, Integration + Browser. Everything here is provable server-side — the headers a
/// response carries. What no <c>WebApplicationFactory</c> test can prove is that a real browser *accepts* the pair and
/// attaches the <c>__Host-</c> cookie cross-origin, because the factory answers the request without a same-origin
/// policy to violate. That half belongs to <c>-064</c>'s Chromium harness and is gated on the same Q4 origin answer; the
/// deviation is recorded rather than glossed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CorsCredentialsTests(PostgresFixture postgres) : IDisposable
{
    private const string AllowedOrigin = "http://cors-credentials.test";
    private const string DisallowedOrigin = "http://not-allowed.test";

    /// <summary>
    /// Its own host rather than the shared <c>ApplicationsApiFactory</c>, for one reason: that factory's
    /// <see cref="ApplicationsApiFactory.AllowedOrigin"/> is a <c>const</c> on a <c>sealed</c> class, and two of the
    /// cases here need a host configured differently — one with no entry that could ever match, one deliberately
    /// misconfigured with a wildcard. Reconfiguring the shared fixture would change what the neighbouring file asserts.
    /// </summary>
    private sealed class OriginFactory(string connectionString, params string[] origins) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", "cors-credentials@example.test");
            builder.UseSetting("Auth:Bootstrap:Password", "the-cors-seed-password-6a3");
            for (var i = 0; i < origins.Length; i++)
            {
                builder.UseSetting($"Cors:AllowedOrigins:{i}", origins[i]);
            }
        }
    }

    /// <summary>
    /// Hosts are tracked and disposed with the class, because a <c>WebApplicationFactory</c> owns a live host and this app
    /// applies migrations at startup -- an undisposed factory is a process still holding the database that the whole
    /// <c>postgres</c> collection shares.
    ///
    /// What this is NOT justified by: a timing win. Five leaking hosts here were blamed for the full suite going 38 s to
    /// 74 s, and that claim was written into this file before it was checked. Disposing them made the suite *slower*
    /// (1 m 36 s), and the class on its own runs in 7 s focused. The durations are real and the cause is not established;
    /// what is established is that "I saw two numbers move and assumed which change moved them" is how this repository has
    /// repeatedly manufactured a false record.
    /// </summary>
    private readonly List<IDisposable> _hosts = [];

    private HttpClient AllowedClient() => _allowed ??= Track(new OriginFactory(postgres.ConnectionString, AllowedOrigin)).CreateClient();

    private OriginFactory Track(OriginFactory factory)
    {
        _hosts.Add(factory);
        return factory;
    }

    private HttpClient? _allowed;

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient http, string method, string headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/applications");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", headers);
        return await http.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return await http.SendAsync(request);
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        // Both bags, same reasoning as CorsContractTests: CORS headers normally land on the response, and an assertion
        // that reads the wrong one reports absence for a header that is plainly there.
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(", ", values);
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues)
            ? string.Join(", ", contentValues)
            : null;
    }

    [Fact]
    public async Task The_preflight_for_a_credentialed_write_offers_the_credentials_line()
    {
        var http = AllowedClient();
        var response = await PreflightAsync(http, "PUT", "content-type, if-match");

        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));

        // The one header AC-16 is about. Absent, and every cross-origin deployment of this API reads as "no user is
        // signed in" for the single reason that the browser quietly withheld the cookie.
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task The_response_itself_carries_the_pair_not_only_the_preflight()
    {
        // A preflight grants permission to send; the actual response still has to be readable. This is the half that
        // gets forgotten, because the preflight is the one everyone remembers to check.
        //
        // Note the status: -055 gates this route, so the request is unauthenticated and answers 401. The CORS pair must
        // be on THAT response. A 401 without it is a browser-level network error, and the client cannot tell "you are
        // signed out" (redirect to login, -061's contract) from "the server is down" (retry, keep the rows on screen).
        // -062's stream probe is built on exactly that distinction.
        var http = AllowedClient();
        var response = await GetAsync(http, AllowedOrigin);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task An_origin_that_is_not_listed_receives_no_permission_and_no_credentials_offer()
    {
        // Control case: passes today and must keep passing. Nothing about adding credentials may widen the boundary —
        // the failure mode AC-16 warns about is an author "fixing" CORS by relaxing the origin list.
        var http = AllowedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.TryAddWithoutValidation("Origin", DisallowedOrigin);
        var response = await http.SendAsync(request);

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));

        // The credentials line on its own would be worse than nothing: it tells a browser to send identity to a
        // context the server never agreed to trust.
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"an unlisted origin was answered with {(int)response.StatusCode}: refused by CORS is fine, welcomed is not");
    }

    [Fact]
    public async Task A_wildcard_in_configuration_refuses_to_start_the_app()
    {
        // The case AC-16 was written against was "the server emits `*` together with credentials", and the first version
        // of this test asserted that pair never appears. It passed -- for a reason worth knowing: a literal "*" in
        // Cors:AllowedOrigins goes to WithOrigins, which treats it as an ORIGIN STRING to match, and no browser ever
        // sends `Origin: *`. Measured directly (probe, deleted): the preflight answers **204 with no Access-Control
        // headers at all**. So the wildcard never becomes the forbidden pair; it becomes a CORS policy that matches
        // nothing, every cross-origin client gets refused, and nothing says why.
        //
        // That is a worse operational failure than the one AC-16 named, because it looks like the browser being
        // mysterious. The Green therefore adds a startup guard rather than only a header assertion: an inert policy is
        // refused at boot with a message that names the key and the fix.
        var factory = Track(new OriginFactory(postgres.ConnectionString, "*")); // disposed with the class
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var http = factory.CreateClient();
            await GetAsync(http, "http://anywhere.test");
            // unreachable when the guard works: CreateClient() throws during host build
        });

        Assert.NotNull(failure);
        Assert.Contains("Cors:AllowedOrigins", failure!.ToString(), StringComparison.Ordinal);
        Assert.Contains("AC-16", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_credentialed_response_varies_on_origin_so_a_shared_cache_cannot_reuse_it()
    {
        // The header nobody thinks about until a CDN is in front of the app: the response above is *conditional* on the
        // request's Origin, so a cache that ignores Origin can serve the copy carrying
        // `Access-Control-Allow-Origin: http://cors-credentials.test` to a different site -- which that site's browser
        // then accepts, because it was told this origin is allowed. `Vary: Origin` is the instruction that makes the
        // response un-reusable. M5 is the AWS deploy milestone, so "later" here has a date attached.
        var http = AllowedClient();
        var response = await GetAsync(http, AllowedOrigin);

        Assert.Contains("Origin", response.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }
}
