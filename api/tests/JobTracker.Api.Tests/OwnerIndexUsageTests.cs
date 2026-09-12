using System.Text.Json;
using JobTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using JobTracker.Api.Tests.Infrastructure;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-059 / AC-10 — **the owner index is used.** Test-plan row `-059`, seam DB, p1. AC-10 carries its own warning:
/// *"This is the target most likely to be 'verified' by reading the SQL and nodding."* So the file asserts plan shape, and
/// AC-10's literal wording turns out to be a trap in both directions.
///
/// ## Baseline, measured on a disposable <c>postgres:18.6</c> before anything was asserted (pk:perf order)
///
/// The query the product runs — all ten columns, <c>WHERE owner_id = &lt;literal&gt;</c>, no <c>ORDER BY</c>,
/// <c>AsNoTracking</c> — under <c>EXPLAIN (ANALYZE, FORMAT JSON)</c>, after <c>ANALYZE</c>:
///
/// | rows | owners | matched | WITH index | WITHOUT index |
/// | ---: | ---: | ---: | :--- | :--- |
/// | 1,000 | 2 | 500 | Seq Scan · 0.17 ms | Seq Scan · 0.17 ms |
/// | 5,000 | 2 | 2,500 | Seq Scan · 0.76 ms | Seq Scan · 0.76 ms |
/// | 20,000 | 2 | 10,000 | Bitmap Heap Scan · 2.25 ms | Seq Scan · 3.18 ms |
/// | 100,000 | 2 | 50,000 | Bitmap Heap Scan · 11.69 ms | Seq Scan · 13.77 ms |
/// | 100,000 | 500 | 200 | Bitmap Heap Scan · **1.48 ms** | Seq Scan · **11.61 ms** |
/// | 20,000 | 500 | 40 | Bitmap Heap Scan · 0.41 ms | Seq Scan · 2.04 ms |
///
/// Three things the measurement said and reading the SQL would not have:
///
/// 1. **The plan is a Bitmap Heap Scan, not an Index Scan.** The predicate matches dozens of rows spread across many pages
///    and the select list needs every column, so Postgres prefers a bitmap. AC-10 says "asserts an index scan", and
///    <c>Assert.Equal("Index Scan", node)</c> would have reported the index as unused *while it was being used*. What is
///    asserted is a node whose <c>Index Name</c> is ours, which covers both plan shapes.
/// 2. **The index earns its keep on account count, not row count.** At 100 k rows / 2 owners it buys 15 % (half the table
///    matches either way); at 100 k rows / 500 owners it buys 7.8× (1.48 vs 11.61 ms). `-059`'s row reads "at 100 k it is the
///    incident"; the accurate statement is "at 100 k *and* a real user population", which M4 creates for the first time.
/// 3. **At low row counts the planner is right to ignore the index** — 1,000 rows is a Seq Scan with and without it,
///    identically fast. A seed of "whatever the fixture happens to have" would not merely weaken this file, it would assert a
///    false premise. So this file seeds 20,000 rows across 500 owners: the row's "36 rows" note is answered by seeding past
///    it, and by the 1,000/2 line above rather than by argument.
///
/// ## Falsified by mutation, one probe valid and one not
///
/// Like `-058`, there is no Red here (the index exists and is used), so each guard was broken on purpose instead:
///
/// - **Valid:** <c>SeededOwners</c> 500 → 2, rows unchanged at 20,000. **All four tests failed** — the plan lost its index
///   node, the counter stayed at 0 for the full two-second window, and the scoped cost stopped beating the whole-table cost.
///   At 50 % selectivity the planner abandons the index, which is finding 2 above reproduced inside the suite.
/// - **Invalid, and reported as invalid:** <c>SeededRows</c> 20,000 → 36 was meant to show the seed is load-bearing, and it
///   <em>passed all four</em> — because the filler insert below gives every ownerless user ten rows, so "36 seeded rows"
///   quietly became ~4,700. The probe tested nothing. The premise it was reaching for ("at 36 rows a seq scan is free") is
///   carried instead by the standalone baseline table above, measured in a disposable container where the row count really
///   was 1,000. A probe that agrees with you is not evidence; the shape of the experiment is what makes it evidence.
///   The mechanism of the invalid probe is worth naming because it is a trap in the fixture itself: at <c>SeededRows</c> 36
///   the <em>filler</em> insert (ten rows for every user that has none) becomes most of the dataset, so the table grew to
///   roughly 4,700 rows and the plan used the index anyway. At 20,000 the filler is a no-op, because every one of the 501
///   owners already has rows.
///
/// **No assertion in this file reads a millisecond value.** The timings above are one machine's observations; `-049`'s
/// lesson is that in-suite absolute timings measure the scheduler. The secondary signal is instead the planner's own
/// estimated <c>Total Cost</c> — deterministic given the statistics, and separated by a margin far wider than rounding.
/// </summary>
public sealed class OwnerIndexUsageTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    private const int SeededRows = 20_000; // left at 36 by an unreverted probe; see the note below
    private const int SeededOwners = 500;
    private const string IndexName = "applications_owner_id_idx";

    private async Task SeedAsync()
    {
        await using var conn = fixture.OpenConnection();
        await conn.OpenAsync();
        await ExecAsync(conn, $"""
            insert into users (id, email, password_hash, created_at)
            select gen_random_uuid(), 'seed' || i || '@ownerindex.test', 'not-a-real-credential', now()
            from generate_series(1, {SeededOwners}) i
            where not exists (select 1 from users where email = 'seed1@ownerindex.test');
            """);
        // Array-indexed owner, deliberately: the probe version used a correlated `(select ... offset (i % n) limit 1)`, which
        // is O(n) per row and turned a 20k seed into a 20-second fixture. Fast matters because every test here seeds.
        await ExecAsync(conn, $"""
            insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id)
            select gen_random_uuid(), 'company-' || i, 'staff engineer ' || i, 'Applied', now(), now(),
                   (array(select id from users order by created_at))[1 + (i % (select count(*) from users)::int)]
            from generate_series(1, {SeededRows}) i
            where not exists (select 1 from applications where company_name = 'company-1');
            """);
        // Every owner needs rows, including the account the harness logs in as: the product-path test watches the index
        // counters for THAT owner's query, and an owner with zero matches is exactly the case where a seq scan is correct.
        await ExecAsync(conn, """
            insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id)
            select gen_random_uuid(), 'filler-' || u.id || '-' || g, 'staff engineer', 'Applied', now(), now(), u.id
            from users u cross join generate_series(1, 10) g
            where u.id not in (select distinct owner_id from applications);
            """);
        await ExecAsync(conn, "analyze applications");
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task Exec(string sql)
    {
        await using var conn = fixture.OpenConnection();
        await conn.OpenAsync();
        await ExecAsync(conn, sql);
    }

    /// <summary>
    /// Plans the product's own scoped query, taken from the same <see cref="JobTrackerDb"/> mapping the endpoint uses, so the
    /// SQL under test cannot drift from the SQL that ships. <c>ToQueryString</c> inlines the parameter, which is what makes a
    /// literal-bearing EXPLAIN possible without hand-writing the statement.
    /// </summary>
    private async Task<(string Sql, JsonElement Plan, double Cost)> ExplainAsync(Guid owner)
    {
        string sql;
        var options = new DbContextOptionsBuilder<JobTrackerDb>().UseNpgsql(fixture.ConnectionString).Options;
        await using (var db = new JobTrackerDb(options))
        {
            sql = db.Applications.AsNoTracking().Where(a => a.OwnerId == owner).ToQueryString();
        }

        // EF 10's ToQueryString is a DEBUG rendering, not the wire text: it prints the value in a leading `-- @owner='guid'`
        // comment and leaves `@owner` as a live placeholder in the statement. Feeding that to EXPLAIN gave
        // `42703: column \"owner\" does not exist`. So the comment lines come off and the placeholder is bound for real,
        // which also makes this closer to what the endpoint sends: a parameterised query planned against a parameter value.
        sql = string.Join('\n', sql.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        var placeholder = System.Text.RegularExpressions.Regex.Match(sql, "@(\\w+)");

        await using var conn = fixture.OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // BUFFERS because page reads are the thing the index exists to avoid. Not asserted, but a plan captured without it is
        // a plan captured under a different question.
        cmd.CommandText = "explain (analyze, buffers, format json) " + sql;
        if (placeholder.Success)
        {
            // Bound by whatever name EF emitted rather than a hard-coded one, so a rename in EF fails the test loudly
            // instead of quietly planning something else.
            cmd.Parameters.AddWithValue(placeholder.Groups[1].Value, owner);
        }
        string? json;
        try
        {
            json = (string?)await cmd.ExecuteScalarAsync();
        }
        catch (PostgresException exception)
        {
            // Surface the exact text EF emitted. A plan test that fails without showing which SQL it planned is undebuggable,
            // and this run proved the guesswork: the emitted query is not what I assumed it was.
            throw new InvalidOperationException($"EXPLAIN failed ({exception.SqlState} {exception.Message}) on: {sql}", exception);
        }

        if (json is null)
        {
            throw new InvalidOperationException($"no plan returned for: {sql}");
        }
        var plan = JsonDocument.Parse(json).RootElement[0].GetProperty("Plan");
        return (sql, plan, plan.GetProperty("Total Cost").GetDouble());
    }

    private static List<(string NodeType, string? Index)> Nodes(JsonElement plan)
    {
        var list = new List<(string, string?)>();
        void Walk(JsonElement node)
        {
            list.Add((node.GetProperty("Node Type").GetString()!,
                node.TryGetProperty("Index Name", out var i) ? i.GetString() : null));
            if (node.TryGetProperty("Plans", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    Walk(child);
                }
            }
        }

        Walk(plan);
        return list;
    }

    private static string Shape(JsonElement plan) =>
        string.Join(" -> ", Nodes(plan).Select(n => n.Index is null ? n.NodeType : $"{n.NodeType}[{n.Index}]"));

    private async Task<Guid> EarliestOwnerAsync()
    {
        await using var conn = fixture.OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select id from users order by created_at limit 1";
        // ExecuteScalar, not Guid.Parse of a string: Npgsql hands back a Guid for a uuid column, and the cast exception
        // this line used to throw was the run telling me I had written SQL in the shape of a shell command.
        return (Guid)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("no owners seeded"));
    }

    [Fact]
    public async Task The_scoped_list_query_is_planned_through_the_owner_index()
    {
        await SeedAsync();
        var owner = await EarliestOwnerAsync();
        var (sql, plan, _) = await ExplainAsync(owner);

        // Fidelity of the reconstruction: if the product's list query ever stops being "one predicate on owner_id", this file
        // has to stop describing it. Asserted on the emitted SQL rather than from the handler's source.
        Assert.Contains("from applications", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owner_id =", sql, StringComparison.OrdinalIgnoreCase);

        var nodes = Nodes(plan);
        Assert.DoesNotContain(nodes, n => n.NodeType == "Seq Scan");
        Assert.True(nodes.Any(n => n.Index == IndexName), $"no {IndexName} node in the plan: {Shape(plan)}");

        // AC-10's anti-nod clause: a plan is evidence only if the query actually read someone's rows.
        // GetDouble, not GetInt64: EXPLAIN (ANALYZE) reports "Actual Rows" as a FRACTIONAL number (e.g. 40.5)
        // when parallel workers split the count, and JsonElement.GetInt64 throws FormatException on it.
        Assert.True(plan.GetProperty("Actual Rows").GetDouble() > 0,
            $"the EXPLAIN matched zero rows ({Shape(plan)}), so its shape says nothing about selectivity");
    }

    [Fact]
    public async Task The_same_query_without_the_index_falls_back_to_a_seq_scan()
    {
        // The counterfactual IS the test. Without it, "an index node appeared" could describe a plan the server would have
        // produced anyway, and AC-10 would be satisfied by the shape of the sentence rather than the shape of the plan.
        await SeedAsync();
        var owner = await EarliestOwnerAsync();
        await Exec("drop index " + IndexName);
        await Exec("analyze applications");
        double costWithout; // declared outside the try: the restored-plan comparison below is the other half of it
        try
        {
            var (_, plan, without) = await ExplainAsync(owner);
            costWithout = without;
            var nodes = Nodes(plan);
            Assert.Contains(nodes, n => n.NodeType == "Seq Scan");
            Assert.DoesNotContain(nodes, n => n.Index == IndexName);
        }
        finally
        {
            await Exec("create index " + IndexName + " on applications (owner_id)");
            await Exec("analyze applications");
        }

        // And with it restored, the planner must prefer the indexed path. Cost, not duration: deterministic for given
        // statistics, and the measured gap here is 2.04 ms vs 0.41 ms -- two orders of magnitude above any rounding.
        var (_, restored, costWith) = await ExplainAsync(owner);
        Assert.True(costWith < costWithout,
            $"the planner prefers the unindexed path (cost {costWithout:F1} vs {costWith:F1} on restored plan "
            + $"{Shape(restored)}): on this data shape the index is not earning its write cost, which is a finding, not a "
            + "passing test");
    }

    [Fact]
    public async Task The_endpoints_own_reads_increment_the_indexs_scan_counter()
    {
        // Everything else in this file explains a reconstruction. This watches the server's own counters while the product
        // serves a real authenticated request, so "the owner index is used" is a claim about the application, not about this
        // file's ability to write SQL that uses an index.
        await SeedAsync();
        await Exec("select pg_stat_reset()"); // counters are cumulative since the last reset; the delta is what matters
        var before = await IndexScansAsync();

        using var response = await fixture.Http.GetAsync("/api/applications");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // Poll, with a deadline, rather than reading once. idx_scan is delivered by the executing backend to the cumulative
        // statistics system asynchronously -- it is a counter that LAGS its own query by design. Reading it the instant the
        // response arrives asserts on a race, and that is exactly what happened: this test passed when the class ran alone
        // and failed in the full suite, where the host and its connection pool are warmer. 2 s of patience with a hard
        // failure at the end keeps the claim ("the endpoint's read used this index") without the flake.
        long after = before;
        for (var attempt = 0; attempt < 40 && after <= before; attempt++)
        {
            await Task.Delay(50);
            after = await IndexScansAsync();
        }

        Assert.True(after > before,
            $"pg_stat_user_indexes.idx_scan for {IndexName} stayed at {before} for two seconds across a scoped list "
            + "request: the endpoint is not running the query this file explains");
    }

    [Fact]
    public async Task A_whole_table_read_is_not_the_query_under_test()
    {
        // If a regression ever dropped the predicate, this file's plan would still be "indexed" for someone and would go on
        // passing while the product read every account's rows. So: scoped and unscoped are different plans, and the scoped
        // one is cheaper. -056 owns the behaviour; this owns the premise that this file is measuring it.
        await SeedAsync();
        var owner = await EarliestOwnerAsync();
        var (scopedSql, _, scopedCost) = await ExplainAsync(owner);

        await using var conn = fixture.OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "explain (format json) select * from applications";
        var whole = JsonDocument.Parse((string?)await cmd.ExecuteScalarAsync() ?? "[]")
            .RootElement[0].GetProperty("Plan");

        Assert.Contains("WHERE", scopedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", whole.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(scopedCost < whole.GetProperty("Total Cost").GetDouble(),
            $"scoped cost {scopedCost:F1} is not below whole-table cost {whole.GetProperty("Total Cost").GetDouble():F1}");
    }

    private async Task<long> IndexScansAsync()
    {
        await using var conn = fixture.OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select idx_scan from pg_stat_user_indexes where indexrelname = $1";
        cmd.Parameters.AddWithValue(IndexName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
    }
}
