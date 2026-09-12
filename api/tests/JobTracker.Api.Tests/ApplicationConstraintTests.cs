using JobTracker.Api.Tests.Infrastructure;
using Npgsql;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-m3-backend-api-032 (registered in Slice 2, executed early in Slice 1 — see the commit that adds this
/// file for why). AC-4's claim is that a status outside the five is rejected **by the database**, even when the
/// API's validator is bypassed, and the only way to know that is to bypass the API and insert one.
///
/// Every assertion here goes through raw SQL against the <c>applications</c> table the migrations actually
/// created. No HTTP, no EF: an endpoint that validates first would prove the wrong thing, and an EF model is not
/// the schema.
/// </summary>
public sealed class ApplicationConstraintTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    [Fact]
    public async Task No_text_column_carries_an_unchosen_default()
    {
        // Additive to BEHAVIOR-m3-backend-api-032's file, and named as such in the register: §4.1 declares
        // `company_name text NOT NULL`, `job_title text NOT NULL` and `status text NOT NULL` with no DEFAULT
        // anything. What the shipped schema actually had was `DEFAULT ''` on all three — a placeholder EF wrote
        // when adding NOT NULL columns to a populated table, which stayed behind afterwards.
        //
        // Why it is worth a test rather than a comment: an unchosen default turns "the client forgot to send a
        // status" into a stored empty string instead of a violated NOT NULL, so the row that appears in the list
        // has a blank status and nothing anywhere reported the mistake. Since 031 the API validator refuses an
        // empty status, which makes the placeholder unreachable through the front door — and the point of a
        // constraint test is the back door. `xmin` is excluded: a system column, with no default of its own.
        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select column_name
            from information_schema.columns
            where table_name = 'applications'
              and column_name in ('company_name', 'job_title', 'status', 'applied_at', 'location', 'notes')
              and column_default is not null
            order by column_name
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var withDefaults = new List<string>();
        while (await reader.ReadAsync())
        {
            withDefaults.Add(reader.GetString(0));
        }

        Assert.Empty(withDefaults);
    }

    [Fact]
    public async Task Status_check_constraint_rejects_sixth_value()
    {
        // -056: arranging rows by SQL now requires naming their owner; the subselect is the fixture account.
        await fixture.ExecuteAsync("delete from applications");

        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync(
            $"insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id) " +
            $"values ('{Guid.NewGuid()}', 'Hooli', 'Engineer', 'Escalated', now(), now(), (select id from users order by created_at limit 1))"));

        // 23514 = check_violation, from the server, not from a library that read the spec. The SQLSTATE is what
        // the client's adapter would see as a 500/storage-error path, so this number is the contract; matching on
        // the message text would pass for any constraint and fail for the one that matters.
        Assert.Equal("23514", error.SqlState);
        Assert.Contains("status", error.MessageText);
    }

    [Fact]
    public async Task All_five_statuses_the_client_models_are_accepted_by_the_database()
    {
        // The other half of a CHECK, and the half that gets forgotten: a constraint that rejects everything also
        // rejects nothing-allowed, and a list typed one letter wrong in the migration silently loses a status the
        // UI can already set. This is also the cheapest possible guard on G-6 (the DDL list and
        // src/types/application.ts's union must agree) until F-1's shared fixture makes the agreement structural.
        await fixture.ExecuteAsync("delete from applications");

        foreach (var status in new[] { "Saved", "Applied", "Interview", "Rejected", "Offer" })
        {
            await fixture.ExecuteAsync(
                $"insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id) " +
                $"values ('{Guid.NewGuid()}', 'Hooli', 'Engineer', '{status}', now(), now(), (select id from users order by created_at limit 1))");
        }

        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "select count(*) from applications";
        Assert.Equal(5L, (long)(await count.ExecuteScalarAsync())!);
    }
}
