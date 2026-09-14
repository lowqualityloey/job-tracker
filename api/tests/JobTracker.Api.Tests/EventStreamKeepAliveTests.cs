using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using JobTracker.Api;
using JobTracker.Api.Auth;
using JobTracker.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// T-06 / R-4.2 — the SSE keep-alive heartbeat, and the three things it must not break.
///
/// ## Why there is a knob at all
///
/// Spec §4.2 requires a comment frame every 20 s and §6's exit criterion is measured over 300 s. A test that waited for
/// the real interval would run five minutes and still prove nothing about 20 s specifically, so the interval is read from
/// configuration (`ApplicationCatalog.KeepAliveConfigKey`, defaulting to `ApplicationCatalog.DefaultKeepAliveInterval`)
/// and every test here drives it at 50 ms. That is the same measurement, compressed: §6's "at least 12 frames in a
/// 300-second-equivalent window" is 15 intervals at 20 s, and the window below spans 18 intervals at 50 ms. The number
/// that ships is pinned by a test, not by a comment — see
/// <see cref="The_default_interval_is_twenty_seconds_and_stays_under_every_proxy_ceiling"/>.
///
/// ## The trap this file is mostly about
///
/// A heartbeat is three lines of code and one very good way to break R-4.1: a test that watches a *silent* stream for
/// `event:` lines proves the heartbeat works, and a test that watches a *write-fed* stream proves scoping works, and
/// neither catches "the restart of the drain loop swallowed a real event" or "the frame reads `event: keep-alive` instead
/// of `: keep-alive`". So frames are counted rather than searched for, and the right-owner write must produce **exactly
/// one** `event: change` *while heartbeats keep arriving around it*.
///
/// ## Why `Sse:KeepAliveSeconds` never appears in a shipped appsettings
///
/// The default derives from the proxy ceilings (§4.3); it is not a per-environment taste. A key in `appsettings.json`
/// would make the code's default dead and untested, which is exactly the failure `ApplicationsApiFixture` documents for
/// `AllowedOrigins:0`. Only test hosts set it, and they set it through the product's own constant.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EventStreamKeepAliveTests(PostgresFixture postgres) : IDisposable
{
    private const string EmailA = "keepalive-a@example.test";
    private const string EmailB = "keepalive-b@example.test";
    private const string PasswordA = "the-keepalive-a-password-2c7";
    private const string PasswordB = "the-keepalive-b-password-9d4";

    /// <summary>The compressed interval — 50 ms, so a sub-second window still spans many of them.</summary>
    private static readonly TimeSpan TestInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 18 intervals of wall clock, and the bar is §6's 12 frames. The margin is deliberate in both directions: late
    /// <c>Task.Delay</c> firings on a loaded runner must not turn correct code red, and an implementation that emitted a
    /// frame per *event* rather than per interval could never clear 12 in a window with no events at all.
    /// </summary>
    private const int RequiredFrames = 12;

    private static readonly TimeSpan IdleWindow = TimeSpan.FromMilliseconds(900);

    private readonly List<IDisposable> _hosts = [];

    private FastHost? _fast;

    /// <summary>The 50 ms host, probe installed. Built on first use so the boot happens inside a test's own timeout.</summary>
    private FastHost Fast() => _fast ??= FastHost.Build(postgres.ConnectionString, this);

    private sealed record FastHost(KeepAliveFactory Factory, HttpClient Http, DisconnectProbe Probe)
    {
        public static FastHost Build(string connectionString, EventStreamKeepAliveTests owner)
        {
            var probe = new DisconnectProbe();
            var factory = new KeepAliveFactory(
                connectionString, TestInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture), probe);
            owner._hosts.Add(factory);
            return new FastHost(factory, factory.CreateClient(), probe);
        }
    }

    private sealed class KeepAliveFactory(string connectionString, string? keepAliveSeconds, IStartupFilter? probe)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", EmailA);
            builder.UseSetting("Auth:Bootstrap:Password", PasswordA);

            // Null means "absent", which is the shape production runs: no Sse key in any shipped appsettings, so the code's
            // default is the live value. The key comes from the product's own constant rather than a literal restated
            // here — the rule ApplicationsApiFixture states for AllowedOrigin: a test that spells a contract string from
            // memory is testing the test's memory.
            if (keepAliveSeconds is not null)
            {
                builder.UseSetting(ApplicationCatalog.KeepAliveConfigKey, keepAliveSeconds);
            }

            if (probe is not null)
            {
                // The one seam this repository already uses to put a service into a running host —
                // SessionExpiryTests.CreateHost swaps the key ring's TimeProvider the same way, for the same reason: the
                // composition root is the entry point, so anything the container must hold is appended after the fact, and
                // a TryAddSingleton would lose to the real registration. Append, never replace; IStartupFilter composes.
                builder.ConfigureTestServices(services => services.AddSingleton(probe));
            }
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // The observer.
    //
    // Deliberately NOT a copy of EventStreamScopingTests.Listener, even though it starts from the same three fields.
    // That one answers "has the substring you asked for arrived, and in what order", and it must never cancel its own read
    // or it stops collecting the bytes it is about to assert on. This one answers "how many frames of each kind arrived in
    // this window", and it must be able to hang up on purpose, because §4.2(d) is about what happens after a disconnect.
    // Two different questions; sharing the class would have meant a flag for the second one's whole reason to exist.
    //
    // A correction recorded here rather than left in a comment someone would repeat: hanging up the *read* is not a
    // disconnect. This file's first draft assumed that it was, and the probe measured otherwise — the handler stayed open
    // for the whole five-second window. See DisconnectProbe for what a hangup takes instead.
    // -------------------------------------------------------------------------------------------------------------

    private sealed class Tap : IAsyncDisposable
    {
        private readonly Stream _stream;

        /// <summary>
        /// The token the *request* was sent with, not merely the token the read loop uses, so that hanging up is the
        /// client's full leaving and not just a stopped read. Measured, so recorded: under TestServer this still does not
        /// reach the server — neither form of hangup fired HttpContext.RequestAborted in a five-second window. The real
        /// disconnect is played by <see cref="DisconnectProbe"/>; this token is what keeps the client honest about it.
        /// </summary>
        private readonly CancellationTokenSource _hangUp;

        private readonly StringBuilder _seen = new();
        private readonly Task _pump;

        public Tap(Stream stream, CancellationTokenSource hangUp)
        {
            _stream = stream;
            _hangUp = hangUp;
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var read = await _stream.ReadAsync(buffer, _hangUp.Token);
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
                // The host went away under us; whatever was already seen stays valid to assert on.
            }
            catch (OperationCanceledException)
            {
                // HangUp() — the browser closing the tab. In one test this is the event under study, not an accident.
            }
        }

        /// <summary>Leave. Cancelling the token the request went out with is the closest available model of a closed tab:
        /// it stops the client reading *and* aborts the request, so the server's `HttpContext.RequestAborted` fires — the
        /// only signal here that the handler can actually see.</summary>
        public void HangUp() => _hangUp.Cancel();

        public string Snapshot()
        {
            lock (_seen)
            {
                return _seen.ToString();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _hangUp.Cancel();
            await _stream.DisposeAsync();
            try
            {
                await _pump;
            }
            catch (Exception)
            {
                // Whatever the pump died on is not interesting once the assertions have run.
            }

            _hangUp.Dispose();
        }
    }

    /// <summary>
    /// The only seam that can answer §4.2(d) from inside a test: once the client has hung up, nothing the client does can
    /// observe the server, so the observation has to happen server-side. This filter, registered by the test host, watches
    /// for a request to the events route carrying <c>?keepalive-probe=&lt;token&gt;</c> and records when the handler's
    /// task returns and with what. While the connection is open the record is *not* complete — and that control is what
    /// makes the post-hangup assertion mean something instead of "the test happened to finish before the server noticed".
    ///
    /// It lives in the test project on purpose: the product has no hook for this and must not grow one to satisfy a test.
    /// </summary>
    private sealed class DisconnectProbe : IStartupFilter
    {
        public const string QueryKey = "keepalive-probe";

        private readonly ConcurrentDictionary<string, Observation> _observations = new();

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, inner) =>
            {
                var token = context.Request.Query[QueryKey].ToString();
                if (token.Length == 0 || !context.Request.Path.StartsWithSegments("/api/applications/events"))
                {
                    await inner(context);
                    return;
                }

                var observation = new Observation();
                _observations[token] = observation;

                // Registered out-of-band rather than read after `inner` returns, because §4.2(d) is really two claims and
                // the test has to tell them apart: "the server learned the client left" and "the handler stopped writing".
                // If the first fires and the second does not, the product is broken. If neither fires, the harness cannot
                // model a hangup at all — and a test that reports that as a timeout is accusing the wrong component.
                using var abortSeen = context.RequestAborted.Register(() => observation.AbortedUtc = DateTime.UtcNow);

                // The disconnect is driven through the framework's own abort seam rather than inferred from the client
                // hanging up, because measurement says the client's half does not reach here: cancelling the token passed
                // to SendAsync left HttpContext.RequestAborted unfired for the whole five-second window. TestServer has no
                // socket to break. IHttpRequestLifetimeFeature.Abort() is the call Kestrel makes when one does, so the
                // product's cancellation path is exercised for real — what stays unproven by this file is only that a
                // closed tab reaches that call, which is ASP.NET Core's contract and not this repository's.
                observation.Lifetime = context.Features.Get<IHttpRequestLifetimeFeature>();

                try
                {
                    await inner(context);
                }
                catch (Exception ex)
                {
                    observation.Failure = ex;
                    throw;
                }
                finally
                {
                    observation.CompletedUtc = DateTime.UtcNow;
                }
            });

            next(app);
        };

        public Observation Require(string token) =>
            _observations.TryGetValue(token, out var found)
                ? found
                : throw new InvalidOperationException(
                    "the probe never saw this request — either the route is wrong or IStartupFilter is not applied under "
                    + "minimal hosting, which is a harness problem and not a product failure");

        public sealed class Observation
        {
            public DateTime? AbortedUtc { get; set; }
            public DateTime? CompletedUtc { get; set; }
            public Exception? Failure { get; set; }

            internal IHttpRequestLifetimeFeature? Lifetime { get; set; }

            /// <summary>Play the disconnect. Returns false when the host never offered the lifetime feature, which the
            /// caller has to distinguish from "aborted and the handler ignored it".</summary>
            public bool Abort()
            {
                if (Lifetime is null)
                {
                    return false;
                }

                Lifetime.Abort();
                return true;
            }
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // Arrangement.
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A fresh login for one identity, every time — the sibling <c>EventStreamScopingTests.CookieFor</c> does the same and
    /// for the same reason: booting the host must happen here, before anything reaches for the database, or a test can
    /// observe an empty users table and report it as "login is broken" instead of "the migration has not run".
    /// </summary>
    private Task<string> CookieFor(string email, string password) => LoginAsync(email, password);

    private async Task<string> LoginAsync(string email, string password)
    {
        _ = Fast(); // boot first: migration, and the bootstrap account — EnsureSecondAccountAsync below opens its own
                    // connection, and a connection made before the host has run the migration sees no users table at all.
        await EnsureSecondAccountAsync();
        return await LoginWithAsync(Fast().Http, email, password);
    }

    private static async Task<string> LoginWithAsync(HttpClient http, string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(
                $$"""{ "email": "{{email}}", "password": "{{password}}"}""",
                new MediaTypeHeaderValue("application/json")),
        };
        var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return TestCookies.SessionOf(response);
    }

    private async Task EnsureSecondAccountAsync()
    {
        const string sql = """
            insert into users (id, email, password_hash)
            values (gen_random_uuid(), @email, @hash)
            on conflict (email) do nothing
            """;

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("email", EmailB);
        command.Parameters.AddWithValue("hash", new PasswordService().Hash(PasswordB));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Open an SSE stream and start draining it in the background. A probe token asks the test host to watch
    /// this particular connection's lifetime; passing none leaves it unwatched.</summary>
    private async Task<Tap> OpenStreamAsync(HttpClient http, string sessionPair, string? probeToken = null)
    {
        var url = probeToken is null
            ? "/api/applications/events"
            : $"/api/applications/events?{DisconnectProbe.QueryKey}={probeToken}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        TestCookies.Attach(request, sessionPair);

        // Created before the send and handed to the Tap, because the token that can abort the *request* has to exist
        // before the request does.
        var hangUp = new CancellationTokenSource();
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, hangUp.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        return new Tap(await response.Content.ReadAsStreamAsync(), hangUp);
    }

    private async Task<string> CreateAsync(string sessionPair, string companyName)
    {
        var id = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/applications")
        {
            Content = new StringContent(
                $$"""{ "id": "{{id}}", "companyName": "{{companyName}}", "jobTitle": "Platform", "status": "Saved" }""",
                new MediaTypeHeaderValue("application/json")),
        };
        TestCookies.Attach(request, sessionPair);

        var response = await Fast().Http.SendAsync(request);
        Assert.True(
            response.IsSuccessStatusCode,
            $"create failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return id.ToString("D");
    }

    /// <summary>Read until the predicate holds or the bound expires, and put the whole wire in the failure message. The
    /// timeouts here are generous relative to the interval, so a miss is a real behaviour gap rather than jitter, and the
    /// text is the only evidence of what the server actually sent.</summary>
    private static async Task<string> WaitForAsync(Tap tap, Func<string, bool> predicate, int timeoutMs, string what)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string seen;
        do
        {
            seen = tap.Snapshot();
            if (predicate(seen))
            {
                return seen;
            }

            await Task.Delay(20);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"timed out after {timeoutMs} ms waiting for {what}. Wire so far: {Shorten(seen)}");
        return null!;
    }

    private static int CountFrames(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Shorten(string s) => s.Length <= 320 ? s : s[..320] + "…";

    // -------------------------------------------------------------------------------------------------------------
    // The wire contract, spelled literally rather than pulled from a product constant, for the reason EventStreamTests
    // already states when it asserts ": open\n\n": this is what the browser's EventSource parses, so a change on either
    // side has to turn this red. R-4.2's own version of that hazard is a heartbeat written as `event: keep-alive`, which
    // every browser would then hand to application code as a message.
    // -------------------------------------------------------------------------------------------------------------

    private const string OpenFrame = ": open\n\n";
    private const string KeepAliveFrame = ": keep-alive\n\n";
    private const string ChangeFramePrefix = "event: change\ndata: {\"id\":\"";

    [Fact]
    public async Task An_idle_stream_emits_comment_frames_on_a_timer_and_not_one_event_line()
    {
        // (a), plus both halves of §6's exit criterion: the count, and zero `event:` lines.
        var a = await CookieFor(EmailA, PasswordA);
        await using var tap = await OpenStreamAsync(Fast().Http, a);

        // Sample a window, deliberately. "Wait until the twelfth frame arrives" would also be satisfied by a stream that
        // emitted twelve frames in the first ten milliseconds and then went quiet forever — which is not a heartbeat.
        await Task.Delay(IdleWindow);
        var seen = tap.Snapshot();

        var frames = CountFrames(seen, KeepAliveFrame);
        Assert.True(
            frames >= RequiredFrames,
            $"only {frames} keep-alive frames in {IdleWindow.TotalMilliseconds} ms at a "
            + $"{TestInterval.TotalMilliseconds} ms interval, and the bar is {RequiredFrames}. Wire: {Shorten(seen)}");

        Assert.DoesNotContain("event:", seen, StringComparison.Ordinal);

        // And nothing else either: the whole idle stream is the handshake followed by that many identical comment frames,
        // allowing for the one frame that may have been half-written at the moment of the snapshot. Without this, a
        // heartbeat that wrote ": keep-alive\n\n\n" or smuggled in a `retry:` would still satisfy the count above.
        var exact = OpenFrame + string.Concat(Enumerable.Repeat(KeepAliveFrame, frames));
        Assert.True(
            seen.StartsWith(exact, StringComparison.Ordinal) && seen.Length - exact.Length < KeepAliveFrame.Length,
            $"the stream carried bytes that are neither handshake nor heartbeat: {Shorten(seen)}");
    }

    [Fact]
    public async Task A_write_by_the_right_owner_lands_as_exactly_one_change_frame_among_the_heartbeats()
    {
        // (c), and the case T-06 exists to protect: §4.1 names restarting the drain loop as the way an add-on like this
        // breaks the feature underneath it. A badly restarted loop shows up here as a duplicate or a truncated frame —
        // never in a count of keep-alives, which is why this file counts frames instead of searching for substrings.
        var a = await CookieFor(EmailA, PasswordA);
        await using var tap = await OpenStreamAsync(Fast().Http, a);

        // Get the loop parked mid-Delay first, so the write interrupts a heartbeat wait rather than arriving before the
        // first timer is armed.
        await WaitForAsync(tap, s => CountFrames(s, KeepAliveFrame) >= 3, 5_000, "three heartbeats before the write");
        var id = await CreateAsync(a, "Interleave Co");
        await WaitForAsync(tap, s => s.Contains(id, StringComparison.Ordinal), 5_000, "the change frame");
        await Task.Delay(TestInterval * 4);

        var seen = tap.Snapshot();

        Assert.Equal(1, CountFrames(seen, $"{ChangeFramePrefix}{id}\"}}\n\n"));

        // Interleaved, not merely both present: the heartbeats ran before the change, and they resume after it.
        Assert.True(
            seen.StartsWith(OpenFrame + KeepAliveFrame, StringComparison.Ordinal),
            $"no heartbeat preceded the change frame: {Shorten(seen)}");
        var changeAt = seen.IndexOf("event: change", StringComparison.Ordinal);
        Assert.True(
            CountFrames(seen[(changeAt + 1)..], KeepAliveFrame) >= 2,
            $"the heartbeat did not resume after a real event: {Shorten(seen)}");
    }

    [Fact]
    public async Task A_write_by_another_owner_stays_invisible_while_the_heartbeat_proves_the_pipe_is_alive()
    {
        // (b). The interesting part is the third assertion: R-4.1's file had to prove the pipe was live by having a second
        // tab write something, because "no bytes" and "no events for me" were indistinguishable. A comment frame is
        // liveness proof that needs nobody to write anything, so this is the first negative SSE assertion in the codebase
        // that carries its own control.
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        await using var tap = await OpenStreamAsync(Fast().Http, a);

        var id = await CreateAsync(b, "Someone Else Co");
        await Task.Delay(IdleWindow);
        var seen = tap.Snapshot();

        Assert.DoesNotContain(id, seen, StringComparison.Ordinal);
        Assert.Equal(0, CountFrames(seen, "event: change"));
        Assert.True(
            CountFrames(seen, KeepAliveFrame) >= RequiredFrames,
            "the heartbeat stopped, so the two assertions above prove nothing about scoping. Wire: " + Shorten(seen));
    }


    [Fact]
    public async Task When_the_client_hangs_up_the_handler_stops_writing()
    {
        // (d). Once the client has gone there is nothing left for the client to observe, so the observation is made
        // server-side by DisconnectProbe: while the tab is open the handler's task has not returned, and after the hangup
        // it must have — which is the only way "it stopped writing" can be asserted at all from here.
        var a = await CookieFor(EmailA, PasswordA);
        var token = Guid.NewGuid().ToString("N");
        await using var tap = await OpenStreamAsync(Fast().Http, a, probeToken: token);

        await WaitForAsync(tap, s => CountFrames(s, KeepAliveFrame) >= 3, 5_000, "heartbeats before the hangup");
        var observation = Fast().Probe.Require(token);

        // The control. Without it, "the handler finished" is indistinguishable from "the handler never started", which is
        // the shape most of this file's failure modes take.
        Assert.Null(observation.CompletedUtc);

        tap.HangUp();

        // The client's half is only half a hangup under this harness (see DisconnectProbe), so the server-side event is
        // played through the framework's own seam. If that seam is missing, say so — "the handler did not stop" and "there
        // is no way to tell it to" are different conclusions and only one of them is actionable.
        Assert.True(
            observation.Abort(),
            "the host exposed no IHttpRequestLifetimeFeature for this request, so no disconnect can be played from here.");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && observation.CompletedUtc is null)
        {
            await Task.Delay(25);
        }

        // Name the half that failed: the server learning the client left, and the handler stopping.
        Assert.True(
            observation.AbortedUtc is not null,
            "Abort() did not reach HttpContext.RequestAborted, so this seam cannot model a hangup at all. That is a "
            + "harness limitation, not a heartbeat defect.");
        Assert.True(
            observation.CompletedUtc is not null,
            $"the request aborted at {observation.AbortedUtc:HH:mm:ss.fff} and the handler was still inside it five "
            + "seconds later — it did not stop writing.");

        // §4.2's named failure mode is a frame written to a completed response — ObjectDisposedException, from a timer
        // that outlived the request which armed it. Both exits accepted here mean the writes stopped, and neither is that
        // exception: a clean return, or the cancellation the handler is written to absorb.
        Assert.True(
            observation.Failure is null or OperationCanceledException,
            $"the handler exited by throwing {observation.Failure?.GetType().Name}: {observation.Failure?.Message}");
    }

    [Fact]
    public void The_default_interval_is_twenty_seconds_and_stays_under_every_proxy_ceiling()
    {
        // §4.2 is explicit that 20 s is derived from the proxies rather than chosen, and §4.3 hands the *measured* half of
        // that to T-09. This is the arithmetic half, pinned where a change to the default fails loudly instead of showing
        // up as a 504 in a browser two milestones from now.
        var heartbeat = ApplicationCatalog.DefaultKeepAliveInterval;
        Assert.Equal(TimeSpan.FromSeconds(20), heartbeat);

        // The ceilings as §4.3 records them. T-09's IaC owns the real values (aws/alb.tf sets the ALB idle timeout, and
        // CloudFront's origin-response timeout is the binding one); until that exists, this is the only place the
        // inequality is written down in code.
        var albIdleTimeout = TimeSpan.FromSeconds(120);
        var cloudFrontOriginResponseTimeout = TimeSpan.FromSeconds(60);

        Assert.True(
            heartbeat < cloudFrontOriginResponseTimeout,
            $"{heartbeat} is not under CloudFront's largest documented origin-response timeout, so the stream would still "
            + "depend on a ceiling the deploy has not confirmed.");
        Assert.True(
            albIdleTimeout / heartbeat >= 6,
            $"at {heartbeat}, an ALB idle timeout of {albIdleTimeout} leaves fewer than 6 beats between the ceiling and "
            + "the re-register margin in §4.3 — that is the derivation rule, not a preference.");
    }

    /// <summary>
    /// The two shapes where the configured value cannot be used: absent (which is what every shipped environment looks
    /// like, since no appsettings carries the key) and non-positive (an operator typo that, guardless, would have the
    /// loop write a frame as fast as the pipe drains — a self-inflicted DoS on the one process holding every open
    /// stream). Both fall back to the production interval, and at 20 s a window this long cannot contain one frame.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task An_unusable_interval_falls_back_to_the_default_instead_of_hot_looping(string? keepAliveSeconds)
    {
        await using var host = await PlainHostAsync(keepAliveSeconds);
        await using var tap = await OpenStreamAsync(host.Http, host.Cookie);

        await Task.Delay(IdleWindow);
        var seen = tap.Snapshot();

        Assert.StartsWith(OpenFrame, seen, StringComparison.Ordinal);
        Assert.True(
            CountFrames(seen, KeepAliveFrame) == 0,
            $"`{ApplicationCatalog.KeepAliveConfigKey}` was {(keepAliveSeconds is null ? "absent" : keepAliveSeconds)} and "
            + $"the stream still beat faster than the {ApplicationCatalog.DefaultKeepAliveInterval} default: {Shorten(seen)}");
    }

    /// <summary>
    /// A host whose only difference from <see cref="Fast"/> is its heartbeat configuration, with its own client and its
    /// own login. A session *is* replayable across hosts — both read the same Postgres sessions table and the same key
    /// ring, which is §5.4's multi-instance property — but logging in per host keeps each case honest about which server
    /// answered.
    /// </summary>
    private async Task<PlainHost> PlainHostAsync(string? keepAliveSeconds)
    {
        var factory = new KeepAliveFactory(postgres.ConnectionString, keepAliveSeconds, probe: null);
        var http = factory.CreateClient();
        return new PlainHost(factory, http, await LoginWithAsync(http, EmailA, PasswordA));
    }

    private sealed record PlainHost(KeepAliveFactory Factory, HttpClient Http, string Cookie) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }
}

