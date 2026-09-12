using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http.Json;
using JobTracker.Api.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-053 / AC-3 — **login cannot be used to enumerate accounts.** Spec §2.4; test-plan row `-053`; class name is
/// the filter spec §2.4 names (<c>dotnet test --filter ~EnumerationResistance</c>).
///
/// ## The tell this guards, and why the message is the easy half
///
/// Two ways a login endpoint leaks which emails exist: the body it returns (trivially fixed by returning one problem
/// document), and **the work it does**. The second is the one that survives a careless review: the natural implementation
/// is
///
/// <code>if (user is null || !passwords.Verify(user.PasswordHash, password)) → 401</code>
///
/// which is what <c>-051</c> shipped. It is byte-identical in both branches, returns the same status, and has no
/// <c>NullReferenceException</c> to complain about — and <b>the short-circuit means an absent email never pays for a hash
/// verification, so the response comes back roughly 300 ms early.</b> Spec §2.4 says so directly: *the classic tell isn't
/// the message, it's the missing 400 ms.* A "distinguishable" claim that only checks the body is a claim about the half
/// that was never hard.
///
/// ## Why the presence of the dummy verify is proven structurally, not by timing
///
/// Grill Q8's restatement: a bound in milliseconds over N samples on shared CI hardware is a claim about noise, and `-049`
/// has already demonstrated that an absolute timing assertion in a parallel suite flakes on the machine rather than the
/// code (median 1012.4 ms against an 800 ms band, 302 ms when run alone). So the committed guard here is **structural**: a
/// counting <see cref="IPasswordService"/> substituted at the DI seam records the exact envelope each path asked the
/// hasher to check. That proves (a) a verify happens at all on the absent path, and (b) the envelope it verifies declares
/// the same iteration count as a real one — which is the property that makes the work equal. Timings are reported in the
/// task record; **no assertion in this file reads a clock.**
///
/// ## The dummy hash is generated, not hard-coded — and that is load-bearing
///
/// A literal base64 envelope would freeze whatever iteration count was current when someone pasted it. After
/// <c>-048</c> moved that constant to 350,000, a pinned envelope would make the absent path verify at the *old* cost —
/// silently re-creating, in the fix, the very oracle the fix exists to close. Deriving it through the same
/// <see cref="IPasswordService"/> that hashes real passwords makes the equality structural rather than a number two files
/// have to agree on.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EnumerationResistanceTests(PostgresFixture postgres)
{
    private const string RealEmail = "enumeration-real@example.test";
    private const string AbsentEmail = "nobody-has-this@example.test";
    private const string Password = "the-enumeration-test-password-8b4";
    private const string WrongPassword = "the-enumeration-wrong-password-1c7";

    /// <summary>Records every envelope the endpoint asks to be verified, and delegates to the real implementation.</summary>
    private sealed class CountingPasswords : IPasswordService
    {
        private readonly IPasswordService _inner = new PasswordService();
        private readonly object _gate = new();
        private readonly List<string> _verified = [];

        public string Hash(string password) => _inner.Hash(password);

        public bool Verify(string storedHash, string password)
        {
            lock (_gate)
            {
                _verified.Add(storedHash);
            }
            return _inner.Verify(storedHash, password);
        }

        /// <summary>Returns what has been recorded <b>and clears the record</b>: several tests observe two attempts on
        /// one host, and a non-draining read would report the earlier attempt's call as if it belonged to the later one.</summary>
        public string[] Drain()
        {
            lock (_gate)
            {
                string[] snapshot = [.. _verified];
                _verified.Clear();
                return snapshot;
            }
        }
    }

    private static CountingPasswords? _probe;

    private WebApplicationFactory<Program> Host() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting("Auth:Bootstrap:Email", RealEmail);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
            builder.ConfigureTestServices(services =>
            {
                _probe = new CountingPasswords();
                services.RemoveAll<IPasswordService>();
                services.AddSingleton<IPasswordService>(_probe);
            });
        });

    private static async Task<(HttpStatusCode Status, string Body, string? Cookie)> Attempt(
        HttpClient http, string email, string password)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        return (response.StatusCode,
                await response.Content.ReadAsStringAsync(),
                response.Headers.TryGetValues("Set-Cookie", out var v) ? string.Join("|", v) : null);
    }

    /// <summary>PBKDF2-SHA512 envelope: marker byte 0, then the declared iteration count as a big-endian uint32 at [5..9].
    /// Measured in -048, not assumed.</summary>
    private static uint DeclaredIterations(string storedHash)
    {
        var bytes = Convert.FromBase64String(storedHash);
        Assert.Equal(1, bytes[0]);
        return ((uint)bytes[5] << 24) | ((uint)bytes[6] << 16) | ((uint)bytes[7] << 8) | bytes[8];
    }

    [Fact]
    public async Task A_wrong_email_and_a_wrong_password_are_byte_identical()
    {
        using var http = Host().CreateClient();
        var unknown = await Attempt(http, AbsentEmail, Password);
        var wrong = await Attempt(http, RealEmail, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal(wrong.Status, unknown.Status);
        // The whole body, byte for byte, minus the one member that is random by construction.
        //
        // This row was written as "byte-identical in body and status". Measured today: **no two ASP.NET problem documents
        // in this application are ever byte-identical**, because the framework's default writer injects a per-request
        // W3C `traceId` -- the first version of this assertion failed with
        //     Expected: ...,"code":"unauthorized"}   Actual: ...,"code":"unauthorized","traceId":"00-b97b516c5cfde..."}
        // A random correlation id carries no account information, so the claim worth making is precise rather than
        // unachievable: every member *except* the trace id must be equal, and the trace id must be *present on both paths*
        // (its absence on one side would itself be a distinguishing feature). Recorded as a restatement of row -053 in
        // the task record; the alternative -- suppressing traceId globally to make a literal assertion pass -- would trade
        // away the correlation id M3's SSE and problem-doc debugging depends on.
        var (a, b) = (Json(unknown.Body), Json(wrong.Body));
        Assert.True(a.RootElement.TryGetProperty("traceId", out var ta), "absent-email body lost its traceId");
        Assert.True(b.RootElement.TryGetProperty("traceId", out var tb), "wrong-password body lost its traceId");
        Assert.NotEqual(ta.GetString(), tb.GetString());

        foreach (var member in new[] { "type", "title", "status", "detail", "instance", "code" })
        {
            Assert.Equal(Text(b, member), Text(a, member));
        }

        Assert.Equal(Strip(unknown.Body), Strip(wrong.Body));
        Assert.DoesNotContain(unknown.Body, AbsentEmail, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong.Body, RealEmail, StringComparison.Ordinal);
        // Neither body may contain the email it was given, in either direction: that is the actual enumeration channel.
        Assert.DoesNotContain(unknown.Body, RealEmail, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong.Body, AbsentEmail, StringComparison.Ordinal);

        static JsonDocument Json(string body) => JsonDocument.Parse(body);
        static string Text(JsonDocument d, string member) =>
            d.RootElement.TryGetProperty(member, out var v) ? v.ToString() : "<missing>";
        static string Strip(string body) =>
            System.Text.RegularExpressions.Regex.Replace(body, @"""traceId"":""[^""]*"",?", "");
    }

    [Fact]
    public async Task An_absent_email_still_verifies_an_envelope_at_the_real_iteration_count()
    {
        using var http = Host().CreateClient();
        _probe!.Drain(); // anything the boot path recorded
        var absent = await Attempt(http, AbsentEmail, Password);

        Assert.Equal(HttpStatusCode.Unauthorized, absent.Status);
        var calls = _probe.Drain();
        // Presence: the short-circuit means this is empty today, and empty is the oracle.
        Assert.Single(calls);
        // Shape: the dummy declares the same work a real verify would, otherwise "a verify happens" is satisfied by
        // checking a one-iteration stub and the timing tell is only moved, not closed.
        Assert.Equal((uint)PasswordService.IterationCount, DeclaredIterations(calls[0]));
        // And it is not the real user's hash: the absent path must not touch a stored credential it has no business
        // comparing against.
        using var probe = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await probe.OpenAsync();
        await using var cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM users WHERE email = $1::citext";
        cmd.Parameters.AddWithValue(RealEmail);
        var realHash = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(realHash);
        Assert.NotEqual(realHash, calls[0]);
    }

    [Fact]
    public async Task Both_failure_paths_verify_envelopes_of_identical_declared_cost()
    {
        using var http = Host().CreateClient();

        _probe!.Drain();
        await Attempt(http, AbsentEmail, Password);
        var absentCalls = _probe.Drain();

        await Attempt(http, RealEmail, WrongPassword);
        var wrongCalls = _probe.Drain();

        Assert.Single(absentCalls);
        Assert.Single(wrongCalls);
        Assert.Equal(DeclaredIterations(absentCalls[0]), DeclaredIterations(wrongCalls[0]));
        // Same salt length and same derived-key length, structurally: two envelopes whose parameters agree in every
        // declared field do the same number of PBKDF2 rounds over the same amount of input.
        Assert.Equal(Convert.FromBase64String(absentCalls[0]).Length,
                     Convert.FromBase64String(wrongCalls[0]).Length);
    }

    [Fact]
    public async Task Neither_failure_path_sets_a_session_cookie()
    {
        using var http = Host().CreateClient();
        var absent = await Attempt(http, AbsentEmail, Password);
        var wrong = await Attempt(http, RealEmail, WrongPassword);

        Assert.Null(absent.Cookie);
        Assert.Null(wrong.Cookie);
    }

    [Fact]
    public async Task A_successful_login_is_distinguishable_only_by_succeeding()
    {
        // The control that keeps the two 401 tests from passing vacuously: if every attempt returned 401 regardless of
        // input, byte-identity and "no cookie on failure" would both be satisfied by a broken endpoint. This asserts the
        // same client, same route, correct credentials -> 204 + a cookie.
        using var http = Host().CreateClient();
        var ok = await http.PostAsJsonAsync("/api/auth/login", new { email = RealEmail, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.NotNull(ok.Headers.TryGetValues("Set-Cookie", out var v) ? string.Join("|", v) : null);
    }
}
