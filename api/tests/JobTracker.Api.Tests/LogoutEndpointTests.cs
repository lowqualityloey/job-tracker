using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using JobTracker.Api.Tests.Infrastructure;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-054 / AC-6 — **logout revokes server-side.** Spec §4.3 (`204` + `Set-Cookie` expiry + `revoked_at`, auth column
/// **authorized**); test-plan row `-054`, seam *Integration + DB*, priority p0.
///
/// ## The row's wording is the design constraint
///
/// *"verified in `psql`, not inferred from the response."* Every cheap wrong implementation of logout looks correct from the
/// client's side — clear the cookie and the user is "logged out" — and AC-6 says so directly: **clearing the browser cookie
/// is not the control.** So the assertions below read `sessions.revoked_at` through Npgsql (the same mechanism the plan's
/// other DB rows use), and the response-level assertions are deliberately secondary.
///
/// ## The hole this increment also closes, in `-055`'s gate
///
/// `-055` protected `/api/applications` by prefix. Spec §4.3 lists `POST /api/auth/logout` and `GET /api/auth/session` as
/// **authorized** too — so the gate as shipped under-covers the ratified contract, and the first test below is the proof.
/// Found by reading §4.3 while writing this behaviour, not by a failing product test: `-055`'s own row said "every **data**
/// route", which it satisfies exactly. **A behaviour can be delivered as written and still be short of the spec it came
/// from**, which is the difference between satisfying a checklist and shipping the thing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LogoutEndpointTests(PostgresFixture postgres)
{
    private const string Email = "logout-tests@example.test";
    private const string Password = "the-logout-test-password-6a3";

    private sealed class LogoutFactory(string cs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", cs);
            builder.UseSetting("Auth:Bootstrap:Email", Email);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
        }
    }

    private HttpClient Client() => new LogoutFactory(postgres.ConnectionString).CreateClient();

    /// <summary>Logs in from a client carrying **no** existing cookie, so nothing is rotated, and returns the cookie header
    /// value plus the session id parsed out of it.</summary>
    private static async Task<(string Header, Guid Id)> FreshSessionAsync(HttpClient http)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "login issued no cookie");
        var header = values!.First().Split(';', 2)[0];
        return (header, Guid.Parse(header.Split('=', 2)[1]));
    }

    private static async Task<HttpStatusCode> DataAs(HttpClient http, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.Add("Cookie", cookie);
        return (await http.SendAsync(request)).StatusCode;
    }

    /// <summary>Reads one session row's server-side state. `epoch` rather than `DateTime` so the comparison is exact to the
    /// microsecond PostgreSQL stores, without a .NET rounding step in the middle of it.</summary>
    private static async Task<(bool Revoked, double? RevokedAtEpoch)> ReadSession(string connectionString, Guid id)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT revoked_at IS NOT NULL, extract(epoch from revoked_at) FROM sessions WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"session {id} is not in the database at all");
        if (reader.IsDBNull(0))
        {
            throw new InvalidOperationException("revoked_at IS NOT NULL returned NULL — a boolean cannot be null here");
        }
        return (reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetDouble(1));
    }

    [Fact]
    public async Task Logout_anonymously_is_401_like_every_other_non_login_route()
    {
        // Spec 4.3's Auth column for this row says "authorized", and -055's gate only covered /api/applications. Today the
        // anonymous request reaches a handler that does not exist, so this fails as 404; after Green it must fail as 401,
        // which is the point -- a route that answers 404 to an anonymous caller and 401 to a caller with a dead cookie is
        // itself a discriminator, and "does this endpoint exist" is not information the network should get for free.
        var response = await Client().PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_row_and_the_database_proves_it()
    {
        using var http = Client();
        var (cookie, id) = await FreshSessionAsync(http);
        Assert.Equal(HttpStatusCode.OK, await DataAs(http, cookie)); // the session really was live before

        var response = await PostLogout(http, cookie);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var (revoked, _) = await ReadSession(postgres.ConnectionString, id);
        // THE assertion the row is named for, read from PostgreSQL rather than inferred: a 204 with an expired Set-Cookie
        // would otherwise be accepted as logout even though the stolen id still authenticates on the next request.
        Assert.True(revoked, "logout returned 204 but sessions.revoked_at is still NULL — the cookie was cleared, the session was not");
    }

    [Fact]
    public async Task The_revoked_cookie_is_refused_afterwards()
    {
        using var http = Client();
        var (cookie, _) = await FreshSessionAsync(http);
        Assert.Equal(HttpStatusCode.OK, await DataAs(http, cookie));

        await PostLogout(http, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, await DataAs(http, cookie));
    }

    [Fact]
    public async Task Revocation_is_scoped_to_the_session_that_was_presented()
    {
        // The discriminating test of this file. `UPDATE sessions SET revoked_at = now() WHERE user_id = @me` passes every
        // other test in this class — the row is revoked, the cookie is dead, the response is 204 — and it is wrong: it turns
        // logout into a per-account denial of service that any stolen cookie can aim at, and it silently breaks the
        // logout-everywhere feature's own semantics by making "here" indistinguishable from "everywhere".
        // So: two independent sessions, one logout, and both verdicts asserted on both ends.
        using var a = Client();
        using var b = Client();
        var (cookieA, idA) = await FreshSessionAsync(a);
        var (cookieB, idB) = await FreshSessionAsync(b);
        Assert.NotEqual(idA, idB);

        await PostLogout(a, cookieA);

        Assert.Equal(HttpStatusCode.Unauthorized, await DataAs(a, cookieA));
        Assert.Equal(HttpStatusCode.OK, await DataAs(b, cookieB));

        var (aRevoked, _) = await ReadSession(postgres.ConnectionString, idA);
        var (bRevoked, _) = await ReadSession(postgres.ConnectionString, idB);
        Assert.True(aRevoked);
        Assert.False(bRevoked, "an unrelated session for the same user was revoked too");
    }

    [Fact]
    public async Task Logout_sends_a_cookie_expiry_alongside_the_server_side_control()
    {
        // Secondary on purpose -- AC-6: "clearing the browser cookie is not the control." But it is still the user-visible
        // half: without an expiring Set-Cookie the client keeps replaying a dead id on every request, and a `__Host-`
        // cookie cannot be deleted by any trick other than a matching attribute set (no Domain, Path=/), so the shape of
        // this header is asserted rather than assumed.
        using var http = Client();
        var (cookie, _) = await FreshSessionAsync(http);
        var response = await PostLogout(http, cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var setCookie = TestCookies.Required(response, AuthCatalog.SessionCookieName);
        Assert.StartsWith("__Host-JTSession=", setCookie, StringComparison.Ordinal);
        Assert.Contains("Max-Age=0", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("Domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.Ordinal);
        Assert.Contains("HttpOnly", setCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_logout_does_not_move_the_moment_the_session_died()
    {
        // Same policy -052 encoded for rotation: `revoked_at` records when a session stopped being valid. Letting a repeat
        // logout overwrite it means the audit question "when did this session actually die" silently becomes "when did
        // someone last hit logout", and a stolen-id-revoked-by-its-owner becomes indistinguishable from a session that died
        // hours earlier. Cheap to assert, invisible to every other test.
        using var http = Client();
        var (cookie, id) = await FreshSessionAsync(http);
        await PostLogout(http, cookie);
        var (firstRevoked, firstEpoch) = await ReadSession(postgres.ConnectionString, id);
        Assert.True(firstRevoked);
        Assert.NotNull(firstEpoch);

        await PostLogout(http, cookie);
        var (_, secondEpoch) = await ReadSession(postgres.ConnectionString, id);

        Assert.Equal(firstEpoch, secondEpoch);
    }

    private static async Task<HttpResponseMessage> PostLogout(HttpClient http, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", cookie);
        return await http.SendAsync(request);
    }
}
