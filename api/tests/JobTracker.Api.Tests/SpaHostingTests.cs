using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-071 / test-plan row `-071` — **the API serves the built front end as its own origin.**
///
/// ## Why this behaviour exists at all
///
/// `DECISION-m4-auth-007` was answered (a): the browser harness must load its page from the **API's** origin, because
/// Chromium's *site* is scheme + host and a page on `127.0.0.1:4173` talking to an API on `172.23.x.x:5080` is two sites,
/// so `SameSite=Lax` drops the session cookie and AC-12 fails for a reason that is not the product. The amendment at
/// `DECISION-007`'s foot adds the half that measurement forced: **same-origin is necessary and not sufficient** — over
/// plain `http` Chromium refuses to *store* a `__Host-` cookie at any address, so the same origin must also be TLS.
/// The TLS half needs no code here (Kestrel reads a PEM from configuration), which is why this file is only about serving.
///
/// ## The failure this row exists to catch
///
/// An SPA fallback is a catch-all, and a catch-all that is too wide **swallows the API**. Concretely: the natural
/// implementation, `MapFallback("{*path:nonfile}")`, matches `/api/anything-without-a-dot` — so a typo'd or removed
/// endpoint stops answering `404 application/problem+json` and starts answering **`200 text/html` with the app shell**.
/// That is the worst available shape for a JSON client: the status says success, the body is not the contract, and
/// `System.Text.Json` reports the mismatch. Case 5 pins it. Case 4 pins its neighbour — the **gate** must still be the
/// gate, so an unauthenticated `/api/applications` is `401` and not a friendly page.
///
/// Case 6 covers the other classic SPA bug: a **missing asset** asked for by name must stay a `404`. Returning the shell
/// for `/assets/typo.js` yields a `200` whose body is HTML served to a script tag, which browsers report as a syntax
/// error with no hint that the file was never there. The `nonfile` constraint is what gets this right, and it is asserted
/// rather than trusted.
///
/// ## Why Development-only is a behaviour and not a detail
///
/// `docs/aws-deployment.md` puts the front end and the API on **different origins** in the deployed shape (that is what
/// makes AC-16's credentialed CORS load-bearing). A bundle served by the API in production would be a second, silent
/// copy of the app on the API's origin — same-origin requests that bypass the CORS policy everyone believes is the
/// boundary. So the gate is asserted from Production's side (case 7), with the bundle **present on disk**, because a
/// gate that only passes when the directory is missing is not a gate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaHostingTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>Only ever appears in the shell. Asserting on it distinguishes "the fallback answered" from "a real file
    /// answered" — a status code alone cannot tell those apart, which is the whole subtlety of this row.</summary>
    private const string ShellMarker = "<title>SHELL-MARKER-071</title>";

    private const string AssetBody = "export const marker = 'ASSET-BODY-071';";

    private readonly string _spaRoot = MakeBundle();

    private static string MakeBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "jt-spa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"),
            $"<!doctype html><html><head>{ShellMarker}</head><body><div id=\"root\"></div></body></html>");
        File.WriteAllText(Path.Combine(root, "assets", "app.js"), AssetBody);
        return root;
    }

    public void Dispose()
    {
        try { Directory.Delete(_spaRoot, recursive: true); } catch (IOException) { /* temp scratch, already gone */ }
    }

    private SpaFactory Factory(string? environment, string? spaRoot) => new(environment, spaRoot, postgres.ConnectionString);

    /// <summary>
    /// The house shape (compare <c>BootGuardTests.TestFactory</c>, <c>BootstrapSeedTests.SeedFactory</c>): a sealed
    /// factory per file overriding <see cref="WebApplicationFactory{TEntryPoint}.ConfigureWebHost"/>, rather than the
    /// delegate-configured <c>CustomWebApplicationFactory</c> — one way to build a host across the whole suite.
    /// </summary>
    private sealed class SpaFactory(string? environment, string? spaRoot, string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development is the shape the gate is written against; the connection string must be real either way,
            // because Program.cs migrates at startup and a missing one fails the boot for an unrelated reason.
            if (environment is not null) builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            if (spaRoot is not null) builder.UseSetting("Web:SpaRoot", spaRoot);
            // Only consumed by BootGuard, which refuses a Production boot carrying default credentials. Supplying
            // them here is what lets the Production case assert the hosting gate in the environment where the guard is
            // awake, instead of quietly testing Development and calling it Production.
            builder.UseSetting("Auth:Bootstrap:Email", $"spa071.{Guid.NewGuid():N}@example.test");
            builder.UseSetting("Auth:Bootstrap:Password", "spa-hosting-row-071-long-enough");
        }
    }

    [Fact]
    public async Task Shell_is_served_at_the_root_in_development()
    {
        using var app = Factory("Development", _spaRoot);
        var res = await app.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("text/html", res.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ShellMarker, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Real_asset_is_served_as_itself_and_not_as_the_shell()
    {
        using var app = Factory("Development", _spaRoot);
        var res = await app.CreateClient().GetAsync("/assets/app.js");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        // The shell also answers 200, so status proves nothing here: the body is the only thing that separates
        // "the file server found it" from "the fallback caught it".
        Assert.Equal(AssetBody, body);
        Assert.DoesNotContain(ShellMarker, body);
    }

    [Fact]
    public async Task Deep_client_side_route_falls_back_to_the_shell()
    {
        using var app = Factory("Development", _spaRoot);
        // A route that exists only in the React router. The server has never heard of it, and must not 404.
        var res = await app.CreateClient().GetAsync("/records/8f31/edit");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(ShellMarker, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Known_api_route_is_not_disturbed_by_the_fallback()
    {
        using var app = Factory("Development", _spaRoot);
        // The gate must still gate with a catch-all registered behind it: this is a real API route, and the answer
        // must be the API's 401 problem document, not the shell and not a framework HTML page.
        var res = await app.CreateClient().GetAsync("/api/applications");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.DoesNotContain(ShellMarker, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_api_route_is_a_problem_document_and_never_the_html_shell()
    {
        using var app = Factory("Development", _spaRoot);
        // `/api/…` with no dot in it is precisely what a `nonfile` catch-all swallows. This is the row's reason to exist.
        var res = await app.CreateClient().GetAsync("/api/applications-changed-since-last-deploy");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("application/problem+json", res.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ShellMarker, body);
    }

    [Fact]
    public async Task Missing_named_asset_stays_a_404_rather_than_becoming_the_shell()
    {
        using var app = Factory("Development", _spaRoot);
        // A `200` whose body is HTML, requested as a script, surfaces as a syntax error far from its cause.
        var res = await app.CreateClient().GetAsync("/assets/typo.js");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain(ShellMarker, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_boot_with_the_bundle_present_serves_nothing()
    {
        using var app = Factory("Production", _spaRoot);
        var res = await app.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain(ShellMarker, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Absent_bundle_in_development_boots_and_serves_nothing()
    {
        // A path that cannot exist: the app must still start and still answer as a pure API. The harness runs against a
        // real checkout, but CI's api job has no built front end at all, and a startup throw there would turn a
        // convenience into a dependency.
        var missing = Path.Combine(Path.GetTempPath(), "jt-spa-absent-" + Guid.NewGuid().ToString("N"));
        using var app = Factory("Development", missing);
        var client = app.CreateClient();

        var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);

        // And the API half still works, which is what "degraded, not broken" has to mean.
        var api = await client.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }
}
