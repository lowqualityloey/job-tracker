using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-051 — **a valid login returns `204` and a `Set-Cookie` header carrying the whole `__Host-` attribute set.**
/// Spec §4.3's first row; test-plan row `-051`, seam **Integration**, priority **p0**.
///
/// ## Why the cookie is asserted on the header and nowhere else
///
/// A `204` has no body, and `HttpClient`'s `CookieContainer` would happily normalise, drop, or reorder attributes before
/// anything could see them — `Set-Cookie` is a **raw response header**, and `__Host-` semantics are defined by the exact
/// attribute string a browser receives. So every assertion here reads `Headers.TryGetValues("Set-Cookie", …)`.
/// The spec says the same thing in one line: "attributes are header-only, assert them there."
///
/// ## `Domain=` must be absent, and that is an assertion rather than an omission
///
/// `__Host-` is the prefix that buys "this cookie can never be sent to a subdomain". It only works if the cookie has **no
/// `Domain` attribute at all**, so a future edit that adds one for convenience would silently defeat the prefix — and the
/// browser, not the test suite, would be the thing that notices. Hence an explicit `DoesNotContain("Domain=")`.
///
/// ## The malformed-body case belongs here, not with `-053`
///
/// <c>-053</c> owns "wrong email and wrong password are byte-identical". A <c>{}</c> request is neither: it is the binder
/// question that produced gap 12 — <b>an input that fails before any validator exists</b>. A login endpoint that answers
/// <c>500</c> to an empty body would be the most-exposed instance of that defect in the whole app, so it is asserted with
/// the endpoint that introduces it rather than waiting for the behaviour about 401 parity.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LoginEndpointTests(PostgresFixture postgres)
{
    private const string Email = "login-tests@example.test";
    private const string Password = "the-configured-seed-password-7b4d";

    /// <summary>Boots the real app with the bootstrap account configured, so the credential under test is the one the
    /// product actually creates rather than a row this test inserted by hand.</summary>
    private sealed class AuthFactory(string email, string password, string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", email);
            builder.UseSetting("Auth:Bootstrap:Password", password);
        }
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient http, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(body, new MediaTypeHeaderValue("application/json"))
        };
        return await http.SendAsync(request);
    }

    private static async Task<string> SingleSetCookieAsync(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values),
            "no Set-Cookie header on the response — a 204 without a cookie is a login that authenticates and then forgets you");
        var cookie = Assert.Single(values.ToList());
        return cookie;
    }

    [Fact]
    public async Task A_valid_login_returns_204_with_no_body()
    {
        using var factory = new AuthFactory(Email, Password, postgres.ConnectionString);
        var http = factory.CreateClient();

        var response = await LoginAsync(http, $$"""{ "email": "{{Email}}", "password": "{{Password}}" }""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // 204 means "no content", and an authoring framework that wrote a body would be lying in the status line.
        // Null or zero, both honest for a 204; a positive length is the framework lying in the status line.
        Assert.True(response.Content.Headers.ContentLength is null or 0,
            $"204 carried {response.Content.Headers.ContentLength} bytes of body");
    }

    [Fact]
    public async Task The_session_cookie_carries_every_attribute_the__Host_prefix_requires_and_no_domain()
    {
        using var factory = new AuthFactory(Email, Password, postgres.ConnectionString);
        var cookie = await SingleSetCookieAsync(await LoginAsync(factory.CreateClient(),
            $$"""{ "email": "{{Email}}", "password": "{{Password}}" }"""));

        Assert.StartsWith("__Host-JTSession=", cookie, StringComparison.Ordinal);
        Assert.Contains("Secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.Ordinal);
        Assert.Contains("Path=/", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("Domain=", cookie, StringComparison.OrdinalIgnoreCase);

        // No Expires / Max-Age: this is a session cookie by design, so closing the browser drops the client's copy and the
        // server's own expiry is what actually ends the session (that window is BEHAVIOR-055/056, not this one).
        Assert.DoesNotContain("Expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Max-Age=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_successful_login_writes_a_server_side_session_row_for_the_issued_cookie()
    {
        // The claim that makes this "server-side sessions" rather than a bearer token with a cookie wrapper: the cookie
        // value must correspond to a row. -054 later revokes through that row, and if this link is fake, revocation is
        // theatre. Read from PostgreSQL directly, per the test plan's rule that DB effects are not inferred from responses.
        using var factory = new AuthFactory(Email, Password, postgres.ConnectionString);
        var cookie = await SingleSetCookieAsync(await LoginAsync(factory.CreateClient(),
            $$"""{ "email": "{{Email}}", "password": "{{Password}}" }"""));
        var token = cookie.Split('=', 2)[1].Split(';', 2)[0];

        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT count(*)::int FROM sessions s
                JOIN users u ON u.id = s.user_id
                WHERE s.id = $1::uuid AND u.email = $2::citext AND s.revoked_at IS NULL
                """;
            cmd.Parameters.AddWithValue(token);
            cmd.Parameters.AddWithValue(Email);
            Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
        }

        // And the expiry is stored rather than implied: a column nobody reads is how -055's window becomes decoration.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT expires_at IS NOT NULL FROM sessions WHERE id = $1::uuid";
            cmd.Parameters.AddWithValue(token);
            Assert.True((bool)(await cmd.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task An_empty_body_is_a_400_problem_with_a_code_and_never_a_500()
    {
        using var factory = new AuthFactory(Email, Password, postgres.ConnectionString);
        var response = await LoginAsync(factory.CreateClient(), "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("problem", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"code\"", body, StringComparison.Ordinal);
        Assert.Contains("validation", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal Server Error", body, StringComparison.OrdinalIgnoreCase);
    }
}
