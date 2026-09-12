using System.Net.Http.Json;
using System.Net.Http;
using JobTracker.Api.Auth;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace JobTracker.Api.Tests.Infrastructure;

/// <summary>
/// The real host, in-process, pointed at a real container.
///
/// <see cref="WebApplicationFactory{TEntryPoint}"/> boots the application exactly as <c>dotnet run</c> would —
/// including <c>Program.cs</c>'s middleware order — and hands back an <see cref="HttpClient"/> that talks to it
/// over an in-memory pipeline. Nothing is mocked. The one thing replaced is the connection string, via ordinary
/// configuration, because "which database" is environment and not behaviour.
///
/// This is why <c>Program.cs</c> carries <c>public partial class Program;</c>: the factory needs the entry point
/// as a type, and the top-level-statements file otherwise keeps it internal.
/// </summary>
public sealed class ApplicationsApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>
    /// The one origin the test host is configured to trust. Public because <c>CorsContractTests</c> must assert on
    /// the *same* value the host was given — a test that invents its own origin string is testing the test's memory.
    /// </summary>
    public const string AllowedOrigin = "http://allowed.test";

    /// <summary>
    /// The fixture's own bootstrap account, configured through the same <c>Auth:Bootstrap:*</c> keys the product reads.
    /// Deliberately <b>not</b> inserted with SQL: seeding through configuration means these tests exercise the real
    /// credential path, so a login route that quietly stopped working would be noticed by every test in the file
    /// rather than only by the ones about login.
    /// </summary>
    public const string BootstrapEmail = "fixture-user@example.test";
    public const string BootstrapPassword = "the-fixture-session-password-9d1";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        // UseSetting, not environment variables: it lands in the same configuration pipeline the app reads at
        // startup, and it keeps the fixture honest about *what* it is overriding.
        //
        // The CORS origins are set here for the same reason the connection string is: without it the host would read
        // `appsettings.Development.json`'s localhost list, and the tests would be asserting that the deployment
        // config happens to allow a browser on this particular machine. Index-key form because `UseSetting` has no
        // array overload — and that also proves the app must read the section as an array, not a delimited string.
        builder.UseSetting("ConnectionStrings:Default", connectionString)
            .UseSetting("Cors:AllowedOrigins:0", AllowedOrigin)
            // The gate (BEHAVIOR-055) refuses anonymous reads, so a host with no configured account would be a host
            // no test can use.
            .UseSetting("Auth:Bootstrap:Email", BootstrapEmail)
            .UseSetting("Auth:Bootstrap:Password", BootstrapPassword);
}

/// <summary>
/// Container + host, started together. Also the tests' back door for arranging rows: <see cref="OpenConnection"/>
/// speaks raw SQL, so a test can put the database into a state **without** using the endpoints that test is about
/// to assert. A POST that proves GET would let a round-tripping bug in both directions pass — the same reason
/// 027 seeds with SQL rather than calling a create endpoint that does not exist yet.
/// </summary>
public sealed class ApplicationsApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresFixture.PinnedImage).Build();

    // private set, not init: the factory cannot exist until the container is running (see InitializeAsync), and
    // an init-only property can only be assigned in a constructor or object initializer.
    public ApplicationsApiFactory Factory { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;

    /// <summary>
    /// A **second, independent host** against the same container. Exists for BEHAVIOR-…-030's claim that a record
    /// created through one client is visible to another: a second HttpClient over the *same* host would only prove
    /// the response is not cached in the client, and an in-memory list would pass that happily. This spins a new
    /// <c>WebApplicationFactory</c> — its own <c>Program</c> instance, its own DI container, its own change
    /// trackers — so the only thing the two can share is PostgreSQL. That is the failure mode worth catching: a
    /// "backend" that keeps the catalog in process memory would satisfy every other test in this file.
    /// </summary>
    public async Task<HttpClient> CreateIndependentHost()
    {
        var http = new ApplicationsApiFactory(ConnectionString).CreateClient();
        await AttachSessionAsync(http);
        return http;
    }

    /// <summary>Valid only after <see cref="InitializeAsync"/>; mapped port, not 5432, by construction.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlConnection OpenConnection() => new(ConnectionString);

    /// <summary>
    /// Arrange-and-clean escape hatch. Public on purpose: tests need the table in a known state and the API has
    /// no create endpoint yet (and even once it does, see 027's comment on why GET is not proven with POST).
    /// </summary>
    /// <summary>
    /// Logs in through the real endpoint and pins the resulting cookie onto the client as a default header.
    /// The header is set by hand rather than via a CookieContainer because these tests assert on what a client
    /// holding a session receives; -051 covers the raw Set-Cookie string separately, where a container would hide it.
    /// </summary>
    private static async Task AttachSessionAsync(HttpClient http)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login",
            new { email = ApplicationsApiFactory.BootstrapEmail, password = ApplicationsApiFactory.BootstrapPassword });
        if (login.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"fixture login failed: {(int)login.StatusCode} {await login.Content.ReadAsStringAsync()}");
        }

        // Selected by name instead of `values.First()`, which was positionally correct while login set one cookie and
        // became silently load-bearing the moment it set two: `-063` adds the antiforgery token, and a fixture that
        // grabbed whichever header happened to come first would attach a token where a session belongs.
        string? Pair(string name) => login.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';', 2)[0]).FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

        var cookie = Pair(AuthCatalog.SessionCookieName)
            ?? throw new InvalidOperationException("fixture login issued no session cookie");
        // `Pair` returns the whole `name=value` pair, which is what a `Cookie:` header wants and what a *token value*
        // must not have. The first run of this row sent `X-CSRF-Token: __Host-JTCsrf=CfDJ8…` and every gated write came
        // back 403 — a fixture bug, diagnosed only because the probe printed the value instead of inferring it.
        // `AntiforgeryGateTests` had the strip right all along, which is why 7/7 passed while 36 cases in another file
        // did not: the same mistake, seen twice, is a helper's fault, and the helper is this method.
        var tokenPair = Pair(Antiforgery.CookieName);
        var token = tokenPair is null ? null : tokenPair[(Antiforgery.CookieName.Length + 1)..];

        http.DefaultRequestHeaders.Remove("Cookie");
        http.DefaultRequestHeaders.Add("Cookie", cookie);

        // BEHAVIOR-m4-auth-063: the antiforgery token, as a default header on the same client the session belongs to.
        //
        // What this scaffolding decision buys, and what it therefore *costs in evidence*, is worth being exact about.
        // The ~80 pre-existing tests that write through this fixture are about validation, ownership, concurrency and
        // event fan-out — none of them is about antiforgery, and making each carry the token by hand would have
        // rewritten 85 call sites to express one property that `AntiforgeryGateTests` already pins at the seam where it
        // belongs. So the fixture holds the token the way a browser holds the cookie: without each test deciding to.
        //
        // The cost is the sentence to remember when reading a green suite: **these tests no longer prove the header is
        // required, because something is always sending it.** That claim lives in `AntiforgeryGateTests`, which builds
        // its own clients and deliberately omits the header, and in `-063`'s two browser cases. A fixture that
        // supplied the token there too would turn the only real evidence into plumbing.
        http.DefaultRequestHeaders.Remove(Antiforgery.HeaderName);
        if (token is not null)
        {
            http.DefaultRequestHeaders.Add(Antiforgery.HeaderName, token);
        }
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Built after the start on purpose: a Testcontainers connection string needs the mapped public port,
        // which does not exist until the container is running.
        Factory = new ApplicationsApiFactory(ConnectionString);
        Http = Factory.CreateClient();
        await AttachSessionAsync(Http);
    }

    public async Task DisposeAsync()
    {
        Http.Dispose();
        Factory.Dispose();
        await _container.DisposeAsync();
    }
}
