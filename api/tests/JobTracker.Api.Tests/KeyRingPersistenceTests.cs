using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.Api.Tests;

/// <summary>
/// <c>-074</c> — the antiforgery token outlives the process that issued it, when the processes share a database.
///
/// <para>
/// <c>-063</c> protected the session id with ASP.NET Core's data-protection stack and this file's premise is that the
/// stack's key ring is <strong>ephemeral per process by default</strong>. <c>-063</c> found that out sideways: it built a
/// fresh <see cref="WebApplicationFactory{TEntryPoint}"/> per request in <c>OwnershipTests</c> and six of its own correct
/// writes came back <c>403 antiforgery</c> — a test-topology accident that happened to be a truthful picture of a
/// deployment.
/// </para>
///
/// <para>
/// So this is that accident, written as the behaviour it actually predicts, and it needed no assumption about how many
/// instances anyone plans to run. Measured on <c>127.0.0.1</c> against one scratch database with a single instance: log
/// in, write (201), <em>restart the process</em>, then send the very same token again — <c>403 antiforgery</c>, while the
/// session behind it still answers <c>200</c>, because the session lives in a table and the key lives in a ring that
/// died with the process. Every deploy silently invalidates every outstanding token while leaving its user looking
/// logged in. Two hosts sharing a database is the same event with a different name, which is why the test below is
/// written with two hosts rather than with a restart: it is the only form that can run in CI.
/// </para>
///
/// <para>
/// Cookie names are literals here rather than references to <c>Antiforgery.CookieName</c>, for the reason
/// <c>AntiforgeryGateTests</c> states: a test that reads its own constants from the code under test cannot catch that
/// code changing them.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class KeyRingPersistenceTests(PostgresFixture postgres)
{
    private const string Email = "keyring-tests@example.test";
    private const string Password = "the-keyring-test-password-7f2a";
    private const string SessionCookieName = "__Host-JTSession";
    private const string TokenCookieName = "__Host-JTCsrf";
    private const string Header = "X-CSRF-Token";

    /// <summary>
    /// Deliberately two instances of this class, constructed separately: a shared client would silently collapse the
    /// whole row into the one-host case that already passes. Each gets its own host, its own ring, and — before the fix
    /// — its own idea of what a valid token is.
    /// </summary>
    private sealed class RingFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", Email);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
        }
    }

    private HttpClient Host() => new RingFactory(postgres.ConnectionString).CreateClient();

    private static async Task<(string SessionCookie, string Token)> LoginAsync(HttpClient http)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var setCookies = login.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : [];

        string? Pair(string name) => setCookies
            .Select(c => c.Split(';', 2)[0])
            .FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal));

        var session = Pair(SessionCookieName) ?? throw new InvalidOperationException("login issued no session cookie");
        var token = Pair(TokenCookieName)?.Substring(TokenCookieName.Length + 1)
            ?? throw new InvalidOperationException("login issued no antiforgery cookie");
        return (session, token);
    }

    private static string BodyJson =>
        $$"""{"id":"{{Guid.NewGuid()}}","companyName":"Vandelay Industries","jobTitle":"Import/Export Manager","location":"New York","status":"Saved"}""";

    private static async Task<HttpStatusCode> WriteAsync(HttpClient http, string sessionCookie, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/applications")
        {
            Content = new StringContent(BodyJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", $"{sessionCookie}; {TokenCookieName}={token}");
        request.Headers.Add(Header, token);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task A_token_issued_by_one_host_is_accepted_by_a_second_host_on_the_same_database()
    {
        using var issuer = Host();
        using var receiver = Host();

        var (session, token) = await LoginAsync(issuer);
        Assert.Equal(HttpStatusCode.Created, await WriteAsync(issuer, session, token));

        var status = await WriteAsync(receiver, session, token);

        Assert.True(
            status == HttpStatusCode.Created,
            $"host B refused a token host A issued for the same database: {(int)status} {status}. "
            + "The data-protection ring is per-process, so the token cannot leave the instance that minted it. "
            + "A user's next write after a restart — or behind a second task — is a 403 they cannot explain.");
    }

    [Fact]
    public async Task The_second_host_accepts_a_token_it_issued_itself()
    {
        // Without this, the test above could only say "host B said no" — and a host B that is broken in every way says
        // no to everything. This is the control that makes the refusal mean "foreign token", not "unreachable host".
        using var receiver = Host();

        var (session, token) = await LoginAsync(receiver);

        Assert.Equal(HttpStatusCode.Created, await WriteAsync(receiver, session, token));
    }

    [Fact]
    public async Task The_second_host_reads_the_session_so_only_the_token_can_be_the_failure()
    {
        // Isolates the fault to the key ring rather than the credential: a shared read proves the session row, the user
        // row and the connection string are all common to both hosts. Before the fix this passes and the write above
        // fails — the exact shape of "you are still logged in, and nothing you do is accepted."
        using var issuer = Host();
        using var receiver = Host();

        var (session, _) = await LoginAsync(issuer);

        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        read.Headers.Add("Cookie", session);
        using var response = await receiver.SendAsync(read);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
