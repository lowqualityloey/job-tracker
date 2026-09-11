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
    public async Task Status_check_constraint_rejects_sixth_value()
    {
        await fixture.ExecuteAsync("delete from applications");

        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync(
            $"insert into applications (id, company_name, job_title, status, created_at, updated_at) " +
            $"values ('{Guid.NewGuid()}', 'Hooli', 'Engineer', 'Escalated', now(), now())"));

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
                $"insert into applications (id, company_name, job_title, status, created_at, updated_at) " +
                $"values ('{Guid.NewGuid()}', 'Hooli', 'Engineer', '{status}', now(), now())");
        }

        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "select count(*) from applications";
        Assert.Equal(5L, (long)(await count.ExecuteScalarAsync())!);
    }
}
