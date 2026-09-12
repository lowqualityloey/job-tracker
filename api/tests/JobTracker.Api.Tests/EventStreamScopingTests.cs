using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using JobTracker.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-068 — **the change stream is scoped to its owner.** `ApplicationEventBus` writes every event to every
/// connected channel, which was correct until M4. Seams Integration + DB, p0.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EventStreamScopingTests(PostgresFixture postgres) : IDisposable
{
    private const string EmailA = "stream-a@example.test";
    private const string EmailB = "stream-b@example.test";
    private const string PasswordA = "the-stream-a-password-4c1";
    private const string PasswordB = "the-stream-b-password-5d2";

    /// <summary>
    /// How long a "must NOT arrive" assertion watches the pipe. Two seconds is a judgement, not a measurement: the fan-out
    /// being removed here is in-process (a channel write on the request thread), so a frame that is going to arrive arrives in
    /// microseconds. Anything longer buys nothing real and costs the suite wall time it has no reason to spend.
    /// </summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(2);

    /// <summary>A live push can be slow on a contended runner, so the positive bound stays generous; the negative one does not.</summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(10);

    private readonly List<IDisposable> _hosts = [];

    private sealed class StreamFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", EmailA);
            builder.UseSetting("Auth:Bootstrap:Password", PasswordA);
        }
    }

    /// <summary>
    /// ONE host for the whole class, and that is load-bearing rather than tidy. The event bus is a per-process singleton, so
    /// a stream opened against one factory and a write sent through another can never see each other: the first draft made a
    /// fresh host at every use site and saw no frames at all, which looked exactly like the bug under test while being purely
    /// the harness. Proving anything about fan-out requires the connections to share a process.
    /// </summary>
    private HttpClient? _http;

    private HttpClient Http
    {
        get
        {
            if (_http is not null)
            {
                return _http;
            }

            var factory = new StreamFactory(postgres.ConnectionString);
            _hosts.Add(factory);
            return _http = factory.CreateClient();
        }
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    /// <summary>
    /// Account B is inserted with a real hash rather than a shortcut: B logs in through the endpoint, so the credential has
    /// to verify at the same cost as A's (<c>-056</c>'s reasoning, reused). The host must boot before the insert, because
    /// booting is what runs <c>Migrate()</c> and the table does not exist before it.
    /// </summary>
    private async Task EnsureSecondAccountAsync()
    {
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            insert into users (id, email, password_hash, created_at)
            values (gen_random_uuid(), $1::citext, $2, now())
            on conflict (email) do nothing
            """;
        cmd.Parameters.AddWithValue(EmailB);
        cmd.Parameters.AddWithValue(new PasswordService().Hash(PasswordB));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> CookieFor(string email, string password)
    {
        _ = Http; // boot first: migration, and the bootstrap account
        await EnsureSecondAccountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(
                $$"""{ "email": "{{email}}", "password": "{{password}}" }""",
                new MediaTypeHeaderValue("application/json")),
        };
        var response = await Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "no cookie issued");
        return Assert.Single(values.ToList()).Split(';', 2)[0];
    }

    private async Task<Listener> OpenStreamAsync(string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications/events");
        request.Headers.Add("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        // ResponseHeadersRead is the whole trick, as in EventStreamTests: with the default completion option the client
        // buffers until the body ends, and an SSE body never ends.
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType?.ToString());
        return new Listener(await response.Content.ReadAsStreamAsync());
    }

    private async Task<Guid> CreateAsync(string cookie, string company)
    {
        var id = Guid.NewGuid();
        var response = await SendAsync(Http, HttpMethod.Post, "/api/applications", cookie,
            new { id, companyName = company, jobTitle = "Engineer", status = "Applied" });
        Assert.True(response.IsSuccessStatusCode, $"create failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return id;
    }

    private static HttpRequestMessage AuthenticatedRaw(HttpMethod method, string path, string cookie)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private async Task<string> RevisionOfAsync(string cookie, Guid id)
    {
        var doc = System.Text.Json.JsonDocument.Parse(await (await SendAsync(Http, HttpMethod.Get, $"/api/applications/{id}", cookie)).Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("revision").ToString();
    }

    /// <summary>
    /// What the stream said after watching it quietly for a while.
    ///
    /// Implemented as a pause, not as a cancelled read, and the distinction is the reason this file took an extra round:
    /// passing a <see cref="CancellationToken"/> to <c>Stream.ReadAsync</c> on an HTTP response does not merely abandon the
    /// read, it ABORTS THE RESPONSE. The next read then throws <c>IOException</c>, and a test whose negative window destroys
    /// the pipe cannot prove anything with the positive half that follows it. So one background task drains the stream into
    /// a buffer for the listener's lifetime, and "silence" is just a delay before sampling it.
    /// </summary>
    private static async Task<string> ReadWindowAsync(Listener listener, TimeSpan window)
    {
        await Task.Delay(window);
        return listener.Snapshot();
    }

    /// <summary>Waits for a frame, sampling the buffer the pump fills. Polling rather than awaiting the stream keeps the
    /// connection intact; the interval is 50 ms against a 10 s bound, so a real push is never late for this.</summary>
    private static async Task<string> ReadUntilAsync(Listener listener, string needle)
    {
        var deadline = DateTime.UtcNow + FrameTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = listener.Snapshot();
            if (seen.Contains(needle, StringComparison.Ordinal))
            {
                return seen;
            }

            await Task.Delay(50);
        }

        var final = listener.Snapshot();
        Assert.Fail($"never saw {needle} within {FrameTimeout.TotalSeconds}s. Stream said: {Shorten(final)}");
        return final;
    }

    /// <summary>
    /// Drains one SSE response into a growing snapshot. The task is deliberately un-cancelled: it ends when the response
    /// ends, which is when the host is disposed at the end of the class.
    /// </summary>
    /// <summary>
    /// Drains one SSE response into a growing snapshot for the lifetime of the connection.
    ///
    /// The pump is started in the constructor, not in a field initializer: a field initializer cannot reference an
    /// instance method (CS0236), and more importantly a listener that could exist un-pumped is a test fixture that
    /// "proves" silence whenever it forgets to read. Here the two are inseparable -- you get a Listener, you get its pump.
    /// The task is never cancelled, and it must not be: cancelling a read on an HTTP response aborts the response.
    /// </summary>
    private sealed class Listener : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly StringBuilder _seen = new();
        private readonly Task _pump;

        public Listener(Stream stream)
        {
            _stream = stream;
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    var read = await _stream.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }

                    lock (_seen)
                    {
                        _seen.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    }
                }
            }
            catch (IOException)
            {
                // the host went away under us at disposal; whatever was already seen stays valid to assert on
            }
        }

        public string Snapshot()
        {
            lock (_seen)
            {
                return _seen.ToString();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
        }
    }

    private static string Shorten(string s) => s.Length <= 240 ? s : s[..240] + "…";

    [Fact]
    public async Task A_subscriber_receives_the_change_it_caused()
    {
        // Control case, and the one M3 already proved: it keeps this file honest about what must NOT break. The bus exists
        // because two tabs of one user must both update; scoping it to an owner is the fix, scoping it to a connection is a
        // regression dressed as security.
        var a = await CookieFor(EmailA, PasswordA);
        await using var stream = await OpenStreamAsync(a);
        var id = await CreateAsync(a, "Control Co");

        Assert.Contains(id.ToString("D"), await ReadUntilAsync(stream, id.ToString("D")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_users_write_never_reaches_another_users_stream()
    {
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        await using var aStream = await OpenStreamAsync(a);

        var byRecord = await CreateAsync(b, "Someone Else Ltd");

        // Absent within the window...
        var heard = await ReadWindowAsync(aStream, SilenceWindow);
        Assert.DoesNotContain(byRecord.ToString("D"), heard, StringComparison.Ordinal);

        // ...and the absence is not a dead pipe: the very next write by A does arrive on the SAME stream object. Without this
        // second half, the test passes just as well when nothing about the stream works at all.
        var ownRecord = await CreateAsync(a, "My Own Co");
        Assert.Contains(ownRecord.ToString("D"), await ReadUntilAsync(aStream, ownRecord.ToString("D")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_reaches_the_owner_and_not_the_other_user()
    {
        // The bus has three publish sites and the defect is in the fan-out, not in one call. A row that only checks create
        // would leave delete -- and update -- broadcasting whoever-else-is-connected an id for a record they cannot read.
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        var doomed = await CreateAsync(a, "Closing Co");
        var revision = await RevisionOfAsync(a, doomed);

        await using var bStream = await OpenStreamAsync(b);
        var deleted = await SendAsync(Http, HttpMethod.Delete, $"/api/applications/{doomed}", a, ifMatch: revision);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.DoesNotContain(doomed.ToString("D"), await ReadWindowAsync(bStream, SilenceWindow), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_of_one_users_streams_receive_the_change()
    {
        var a = await CookieFor(EmailA, PasswordA);
        await using var first = await OpenStreamAsync(a);
        await using var second = await OpenStreamAsync(a);

        var id = await CreateAsync(a, "Two Tabs Co");
        var needle = id.ToString("D");

        Assert.Contains(needle, await ReadUntilAsync(first, needle), StringComparison.Ordinal);
        Assert.Contains(needle, await ReadUntilAsync(second, needle), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string cookie, object? body = null, string? ifMatch = null)
    {
        var request = AuthenticatedRaw(method, path, cookie);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        request.Content = body is null ? null : new StringContent(System.Text.Json.JsonSerializer.Serialize(body), new MediaTypeHeaderValue("application/json"));
        return http.SendAsync(request);
    }
}
