using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using JobTracker.Api.Tests.Infrastructure;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-066 / AC-17 — **expiry is real**: an idle window and a hard cap both end a session, and dead rows are pruned at
/// login so <c>sessions</c> cannot grow without bound. Seams Integration + DB, p0.
///
/// ## The clock is the increment
///
/// <c>SessionGate</c> read, until this row: "UtcNow rather than TimeProvider: -066 is the behaviour that needs it." That
/// promise is what these tests spend. Without injectable time the honest alternatives are a test that sleeps thirty-one
/// minutes (slow, and flaky while the whole suite shares one container) or one that sets the system clock (which leaks into
/// every other test in the shared-collection fixture). Spec 3.2 said so at design time and named the cost of refusing:
/// "expiry is a column nobody reads". Every case below moves ONE clock — the app's — and asserts what the product does with it.
///
/// ## Two lifetimes, because they answer different attacks
///
/// The **idle window** (30 minutes, pre-existing) answers "this laptop went to sleep on a train": activity slides it, silence
/// ends it. The **hard cap** answers "the cookie was copied": no amount of activity extends it. That is exactly why the cap
/// needs its own test — a suite with only the idle cases passes perfectly well when no cap exists, because sliding looks
/// precisely like living forever.
///
/// ## Pruning, and the -052 property it must not eat
///
/// AC-17 asks for expired AND revoked rows to be pruned. `-052` revoked superseded sessions rather than deleting them for a
/// reason recorded in its own comment: "the audit question stays answerable — when did this session end, and was that logout,
/// expiry, or a second login?" Unbounded growth and an audit trail are in real tension, so the resolution is a retention
/// window rather than a choice: revoked rows survive <c>RevokedRetention</c> and then go, and the boundary is asserted on
/// BOTH sides so neither half can be satisfied by a no-op. Pruning runs at login (AC-17's "opportunistically"): no hosted
/// service, no timer, no new background failure mode — the sweep rides a path that already writes.
/// </summary>
/// <remarks>Shared-collection fixture: these tests move a clock, never a database, and every prune here is
/// bounded by a retention window so a neighbouring class's live or recently-revoked rows are untouched.</remarks>
[Collection(PostgresCollection.Name)]
public sealed class SessionExpiryTests(PostgresFixture postgres)
{
    private const string Email = "expiry-tests@example.test";
    private const string Password = "the-expiry-seed-password-2c8f";

    /// <summary>
    /// The only clock the product is allowed to read.
    ///
    /// It starts at the REAL current instant, not at a tidy date. A column like <c>created_at</c> may be filled by a
    /// PostgreSQL <c>DEFAULT now()</c> rather than by the app, and a fake clock set to an arbitrary epoch then disagrees with
    /// the row it is supposed to age: the hard-cap test would fail because its session was born months in the future.
    /// Anchoring to real <c>UtcNow</c> makes that class of mismatch impossible to hide behind.
    /// </summary>
    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset current = start;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan delta) => current = current.Add(delta);
    }

    private sealed class ClockFactory(string email, string password, string connectionString, MutableTimeProvider clock)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", email);
            builder.UseSetting("Auth:Bootstrap:Password", password);

            // Replace, do not append-and-hope: the host may register TimeProvider.System itself, and the LAST registration is
            // what a constructor injection resolves. ConfigureTestServices runs after the app's own ConfigureServices, which
            // is the only reason this line can win.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        }
    }

    private static MutableTimeProvider Clock() => new(DateTimeOffset.UtcNow);

    private static async Task<string> LoginAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(
                $$"""{ "email": "{{Email}}", "password": "{{Password}}" }""",
                new MediaTypeHeaderValue("application/json")),
        };
        var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The name=value trimming rule — and the reason for it — now lives in `TestCookies.Pair`. `SessionOf` also
        // records the token the same login issued, which is what lets this file's replayed cookies pass `-063`'s gate
        // without every case threading a second value through a signature.
        return TestCookies.SessionOf(response);
    }

    private static async Task<HttpStatusCode> ReadAsync(HttpClient http, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        TestCookies.Attach(request, cookie);
        return (await http.SendAsync(request)).StatusCode;
    }

    private async Task<List<T>> QueryAsync<T>(string sql, NpgsqlParameter? parameter = null)
    {
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (parameter is not null)
        {
            cmd.Parameters.Add(parameter);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add((T)reader.GetValue(0));
        }
        return rows;
    }

    /// <summary>The session id a login cookie names, taken from the pre-trimmed <c>name=value</c> form above.</summary>
    private static Guid SessionIdFrom(string cookie) => Guid.Parse(cookie.Split('=', 2)[1]);

    /// <summary>Row presence, read from PostgreSQL directly rather than inferred from an HTTP status — the test plan's rule
    /// for DB effects, and the only way to tell a prune that deletes nothing apart from one that deletes.</summary>
    private async Task<bool> RowExistsAsync(Guid id)
    {
        // ::int returns Int32, so the generic is int: asking for a long here threw InvalidCastException, which is what four
        // of the six "Reds" in the first measurement actually were -- a harness bug wearing a feature's clothes.
        var rows = await QueryAsync<int>("select count(*)::int from sessions where id = @id",
            new NpgsqlParameter<Guid>("@id", id));
        return Assert.Single(rows) == 1;
    }

    private async Task<DateTime> ExpiryOfAsync(Guid id)
    {
        var rows = await QueryAsync<DateTime>("select expires_at from sessions where id = @id",
            new NpgsqlParameter<Guid>("@id", id));
        return Assert.Single(rows);
    }

    [Fact]
    public async Task A_session_that_is_used_survives_far_beyond_the_idle_window_and_its_expiry_moves()
    {
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var cookie = await LoginAsync(http);
        var id = SessionIdFrom(cookie);
        Assert.True(await RowExistsAsync(id), "a successful login wrote no session row");

        var firstExpiry = await ExpiryOfAsync(id);

        // Four uses, twenty minutes apart: each lands inside the 30-minute window measured from the PREVIOUS use, so a
        // sliding session is alive at 80 minutes elapsed. If nothing slides, lap 2 or 3 is a 401.
        for (var lap = 1; lap <= 4; lap++)
        {
            clock.Advance(TimeSpan.FromMinutes(20));
            Assert.True(await ReadAsync(http, cookie) == HttpStatusCode.OK,
                $"lap {lap} was refused: the idle window is not sliding");
        }

        // The HTTP half proves the gate agrees with itself. This half proves the gate reads a column that ACTIVITY MOVED,
        // which is the difference between "sliding session" and "the window is longer than I assumed".
        var lastExpiry = await ExpiryOfAsync(id);
        Assert.True(lastExpiry > firstExpiry,
            $"expires_at never moved ({firstExpiry:O} -> {lastExpiry:O}): the slide is not being written");
    }

    [Fact]
    public async Task A_session_expires_when_the_idle_window_passes_without_use()
    {
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var cookie = await LoginAsync(http);

        // The first draft of this case read the API at 29 minutes, asserted 200, then advanced two more minutes and expected
        // 401. The product answered 200 and was RIGHT: at 29 minutes only one minute of a 30-minute window remains, which is
        // inside SessionPolicy.SlideThreshold, so the read slid the window and there was no expiry left to find. A test that
        // performs the thing it is trying to exclude measures something else, so silence — no reads at all — is the stimulus
        // here, and the slide/no-slide boundary is asserted separately below where it can be seen without touching expiry.
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, await ReadAsync(http, cookie));
        Assert.True(await RowExistsAsync(SessionIdFrom(cookie)),
            "an expired session vanished with no login in between: something other than the prune is deleting rows");
    }

    [Fact]
    public async Task A_use_early_in_the_window_does_not_move_the_expiry_and_a_use_late_does()
    {
        // The threshold is the price of sliding: without it every authenticated request writes to sessions, and AC-10's
        // whole argument for the owner index is that reads here should be cheap. So the boundary is asserted from both sides
        // on a session that is nowhere near expiring -- which is also the only way to see it without a 401 in the way.
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var cookie = await LoginAsync(http);
        var id = SessionIdFrom(cookie);
        var minted = await ExpiryOfAsync(id);

        clock.Advance(TimeSpan.FromMinutes(10)); // 20 left: no slide expected
        Assert.Equal(HttpStatusCode.OK, await ReadAsync(http, cookie));
        Assert.Equal(minted, await ExpiryOfAsync(id));

        clock.Advance(TimeSpan.FromMinutes(15)); // now 5 left: inside the threshold
        Assert.Equal(HttpStatusCode.OK, await ReadAsync(http, cookie));
        Assert.True(await ExpiryOfAsync(id) > minted,
            "the window never slid, so an active session dies 30 minutes after login no matter what the user is doing");
    }

    [Fact]
    public async Task The_hard_cap_ends_a_session_that_keeps_being_used()
    {
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var cookie = await LoginAsync(http);

        // Activity every ten minutes, so the idle window never expires it. If the idle window is the only lifetime in the
        // product, this loop runs to its end with every request answering 200 -- which is also what a copied cookie gets.
        var sawAlive = false;
        var endedAfter = TimeSpan.Zero;
        for (var lap = 1; lap <= 200; lap++)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            if (await ReadAsync(http, cookie) == HttpStatusCode.OK)
            {
                sawAlive = true;
                continue;
            }

            endedAfter = TimeSpan.FromMinutes(10L * lap);
            break;
        }

        Assert.True(sawAlive, "no request ever succeeded, so this proves nothing about the cap");
        Assert.True(endedAfter > TimeSpan.Zero,
            "200 advances of ten minutes (33 hours of use) never ended the session: there is no hard cap");
        Assert.InRange(endedAfter, TimeSpan.FromHours(8), TimeSpan.FromDays(2));
    }

    [Fact]
    public async Task Login_prunes_a_session_that_expired()
    {
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var stale = await LoginAsync(http);
        var staleId = SessionIdFrom(stale);
        Assert.True(await RowExistsAsync(staleId));

        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True(await RowExistsAsync(staleId), "the row vanished before anything was supposed to prune it");
        Assert.Equal(HttpStatusCode.Unauthorized, await ReadAsync(http, stale));

        var fresh = await LoginAsync(http); // the prune rides the path that already writes

        Assert.False(await RowExistsAsync(staleId),
            "an expired session survived a login that AC-17 says must prune it");
        Assert.True(await RowExistsAsync(SessionIdFrom(fresh)));
    }

    [Fact]
    public async Task A_revoked_session_survives_the_prune_until_retention_ends_and_then_goes()
    {
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var cookie = await LoginAsync(http);
        var id = SessionIdFrom(cookie);
        Assert.True(await RowExistsAsync(id));

        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
        {
            TestCookies.Attach(request, cookie);
            Assert.Equal(HttpStatusCode.NoContent, (await http.SendAsync(request)).StatusCode);
        }

        // Logout revokes rather than deletes, and -052 said why. So the first login AFTER the logout must leave the dead row
        // alone -- pruning it inside retention turns "when did this session end, and why" back into an unanswerable query.
        clock.Advance(TimeSpan.FromDays(1));
        await LoginAsync(http);
        Assert.True(await RowExistsAsync(id),
            "a revoked session was pruned inside its retention window: -052's audit question just became unanswerable");

        // A month later it must be gone, or the table has no bound -- and AC-17 names that growth as the point.
        clock.Advance(TimeSpan.FromDays(31));
        await LoginAsync(http);
        Assert.False(await RowExistsAsync(id),
            "the retention window is not enforced: sessions grows without bound");
    }

    [Fact]
    public async Task Pruning_never_touches_a_live_session()
    {
        var clock = Clock();
        using var factory = new ClockFactory(Email, Password, postgres.ConnectionString, clock);
        var http = factory.CreateClient();
        var live = await LoginAsync(http);
        var liveId = SessionIdFrom(live);

        clock.Advance(TimeSpan.FromMinutes(5));
        await LoginAsync(http); // the prune runs with a live row in the table

        Assert.True(await RowExistsAsync(liveId), "the prune deleted a session that is still alive");
        Assert.Equal(HttpStatusCode.OK, await ReadAsync(http, live));
    }
}
