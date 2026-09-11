using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// One PostgreSQL container shared by a whole test collection, on a <b>dynamic</b> host port so parallel
/// runs cannot collide (test plan §6). Per-test isolation is by <c>TRUNCATE</c>, not by container-per-test:
/// starting a container per <c>[Fact]</c> would cost ~2 s each and hide nothing that a truncate does not.
/// </summary>
/// <remarks>
/// The image tag is the exact <c>major.minor</c> the spec pins (<c>DECISION-m3-backend-api-001</c>). No
/// <c>latest</c>, no floating <c>18</c> — a test harness that drifts from the pinned version is measuring a
/// different database than the one being shipped.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string PinnedImage = "postgres:18.6";

    // The parameterless builder is obsolete in Testcontainers 4.15.0; the image is now a constructor
    // argument, which also makes the pin impossible to forget.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PinnedImage).Build();

    /// <summary>Wall-clock instant before <c>StartAsync</c>, so a test can prove the server it is talking to
    /// was started by <i>this run</i> rather than reused from some earlier one. xUnit's reported
    /// <c>Duration</c> excludes fixture startup, so the elapsed time printed by <c>dotnet test</c> is not
    /// evidence of anything — this is.</summary>
    public DateTime FixtureConstructedUtc { get; } = DateTime.UtcNow;

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>
/// Slice 0's exception-path evidence, <b>not</b> one of the 20 M3 behaviours. It asserts the one thing every
/// later integration test silently depends on: that a <c>dotnet</c> process can start the pinned image and
/// speak to it. That is <c>UNCERTAINTY-m3-backend-api-008-001</c> (Docker reachability) resolved by
/// execution rather than by assumption — and if it fails, it fails here, before any behaviour is at stake.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresHarnessTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Dotnet_process_reaches_the_pinned_container_and_reports_the_pinned_server_version()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("show server_version", connection);
        var reported = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(reported);
        Assert.StartsWith("18.6", reported);
    }

    [Fact]
    public void Harness_is_independent_of_whatever_database_the_developer_is_running()
    {
        // Without this, a fixture that quietly resolved to a host-ported database (the classic
        // `127.0.0.1:5432` accident) would still print 18.6 and still pass — a green that proves nothing.
        // Testcontainers maps a dynamic port, so the well-known port must never appear in the string.
        Assert.DoesNotContain("Port=5432", fixture.ConnectionString);
        Assert.Contains("Port=", fixture.ConnectionString);
    }

    [Fact]
    public async Task Database_under_test_was_started_by_this_run_not_reused_from_an_earlier_one()
    {
        // The assertion that makes a false green impossible: if the container were cached, reused, or simply
        // the developer's own, the postmaster's start time would predate this fixture by hours and the
        // integration evidence would be worthless while still printing "Passed".
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("select pg_postmaster_start_time()", connection);
        var started = (DateTime)await command.ExecuteScalarAsync();

        Assert.InRange(started, fixture.FixtureConstructedUtc.AddSeconds(-10), DateTime.UtcNow);
    }

    [Fact]
    public async Task A_check_constraint_is_enforced_by_this_container_and_not_silently_accepted()
    {
        // The whole reason SQLite and EF-InMemory are excluded from this milestone: this must fail for the
        // right reason, in the engine that will run in production. If a harness ever reports 0 rows here,
        // the CHECK has become advisory and BEHAVIOR-m3-backend-api-032 is meaningless.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var setup = new NpgsqlCommand(
            "create temporary table probe_status (status text not null check (status in ('Saved','Applied','Interview','Rejected','Offer')))",
            connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var insert = new NpgsqlCommand(
            "insert into probe_status (status) values ('Escalated')", connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal("23514", error.SqlState);
    }
}
