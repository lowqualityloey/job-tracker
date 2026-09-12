using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using Npgsql;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-052 / AC-5 — **the session id is regenerated at login, and the pre-login cookie is dead afterwards.**
/// Test-plan row `-052`, seam Integration, priority p0. Executable as written only after `-055` landed (see the task
/// record's §5 amendment): before the gate, replaying an old cookie and sending none both produced `200`, so this row's
/// stated proof had nothing to be proved against.
///
/// ## The attack this is a guard against, in one sentence
///
/// **Session fixation:** an attacker obtains a session id (a planted cookie via subdomain XSS, a captured value, a link
/// with `;jsessionid=` in it), tricks a victim into authenticating *with that id still in place*, and then walks into the
/// now-authenticated session. The defence is exactly one line of policy — **the id that existed before login is never the
/// id that exists after it** — and it is the reason a "session" here is a row and not a signed blob of the credentials:
/// rotation has to be able to invalidate something the client is still holding.
///
/// ## Why two of the five tests pass before Green, and what that pair is worth
///
/// Measured, not assumed: `A_second_login_issues_a_different_session_id` and `A_first_login_with_no_previous_cookie_still_
/// succeeds` are already true, because a fresh `Guid` per login is how `-051` was written. **That is exactly the property
/// fixation attacks survive** — a new id is issued, and the old one stays valid, so the attacker's planted cookie still
/// works after the victim authenticates. Two tests pass to establish that "the id changed" was never the point; **the
/// three that fail are the ones that say the old id had to stop working.** If all five had failed, the likely reading
/// would be a broken endpoint rather than a missing rotation, which is why the shape of a Red is evidence too.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SessionRotationTests(PostgresFixture postgres)
{
    private const string Email = "rotation-tests@example.test";
    private const string Password = "the-rotation-test-password-5e8";

    private sealed class RotationFactory(string cs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", cs);
            builder.UseSetting("Auth:Bootstrap:Email", Email);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
        }
    }

    /// <summary>Logs in with a client that may already hold a cookie, and returns just the issued cookie value.
    /// Deliberately not a CookieContainer: rotation is about the *header the server sends* and the request the client
    /// chooses to make afterwards, and a jar would decide both of those policy questions for us.</summary>
    private static async Task<string> LoginAsync(HttpClient http, string? existingCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = Email, password = Password })
        };
        if (existingCookie is not null)
        {
            request.Headers.Add("Cookie", existingCookie);
        }

        var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "login issued no cookie");
        return values!.First().Split(';', 2)[0];
    }

    private HttpClient Client() => new RotationFactory(postgres.ConnectionString).CreateClient();

    private async Task<HttpStatusCode> StatusFor(string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }
        return (await Client().SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task A_second_login_issues_a_different_session_id()
    {
        var http = Client();
        var first = await LoginAsync(http, null);
        var second = await LoginAsync(http, first);

        Assert.NotEqual(first, second);
        // Same cookie *name*, different value: a change of name would leave the old one in the jar untouched and is not
        // rotation at all, it is two cookies.
        Assert.StartsWith("__Host-JTSession=", first, StringComparison.Ordinal);
        Assert.StartsWith("__Host-JTSession=", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pre_login_cookie_is_refused_after_logging_in_again()
    {
        // THE fixation test. Everything else here is arrangement; this is the row's stated proof.
        var http = Client();
        var before = await LoginAsync(http, null);
        var after = await LoginAsync(http, before);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusFor(before));
        Assert.NotNull(after);
    }

    [Fact]
    public async Task The_rotated_session_is_still_accepted_where_the_old_one_is_refused()
    {
        // Positive control, and the reason the test above is allowed to mean anything: without it, "the old cookie gets
        // 401" is also satisfied by a server that has stopped accepting any cookie at all. Same request, one header
        // different, opposite verdicts.
        var http = Client();
        var before = await LoginAsync(http, null);
        var after = await LoginAsync(http, before);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusFor(before));
        Assert.Equal(HttpStatusCode.OK, await StatusFor(after));
    }

    [Fact]
    public async Task The_superseded_session_is_revoked_in_the_database_not_left_valid_or_deleted()
    {
        // Read from PostgreSQL rather than inferred, per the test plan's rule for DB effects. Two properties in one query,
        // because the cheap wrong implementations differ and both are wrong: leaving the row valid keeps the stolen cookie
        // alive (which is -052's failure), and *deleting* it makes rotation unauditable — you can no longer answer "when
        // did this session stop working, and was that logout or a second login".
        var http = Client();
        var before = await LoginAsync(http, null);
        var after = await LoginAsync(http, before);
        var oldId = before.Split('=', 2)[1];
        var newId = after.Split('=', 2)[1];

        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT (count(*) FILTER (WHERE revoked_at IS NULL))::int     AS live,
                   (count(*) FILTER (WHERE revoked_at IS NOT NULL))::int AS revoked,
                   count(*)::int                                         AS total
            FROM sessions
            WHERE id IN ($1::uuid, $2::uuid)
            """;
        cmd.Parameters.AddWithValue(oldId);
        cmd.Parameters.AddWithValue(newId);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "neither session row exists — rotation silently dropped a row");
        var live = reader.GetInt32(0);
        var revoked = reader.GetInt32(1);
        var total = reader.GetInt32(2);

        Assert.Equal(2, total);          // both rows still present: nothing deleted
        Assert.Equal(1, revoked);        // exactly the superseded one
        Assert.Equal(1, live);           // exactly the new one
    }

    [Fact]
    public async Task A_first_login_with_no_previous_cookie_still_succeeds()
    {
        // The absence case for the revoke step: a handler that reads "revoke whatever cookie came in" before checking that
        // one arrived would NRE or throw on the very first login, which is the most common login there is.
        var http = Client();
        var cookie = await LoginAsync(http, null);
        Assert.Equal(HttpStatusCode.OK, await StatusFor(cookie));
    }
}
