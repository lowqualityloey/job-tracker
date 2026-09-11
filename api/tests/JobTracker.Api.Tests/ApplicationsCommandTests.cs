using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobTracker.Api.Tests.Infrastructure;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// M3 Slice 2 — the write paths (BEHAVIOR-m3-backend-api-030, -033 … -035, -044, -045).
///
/// Method names are taken from `docs/tests/2026-09-11-test-m3-backend-api.md` so each registered TDD-INTENT row's
/// `dotnet test --filter` command runs against the code that implements it. (Slice 1 drifted on three of four;
/// §11 of that file reconciles it. A register whose commands do not run is a document, not a gate.)
/// </summary>
public sealed class ApplicationsCommandTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    [Fact]
    public async Task Retry_of_create_is_idempotent()
    {
        // BEHAVIOR-m3-backend-api-034 (p1). Registered: "Retried POST with the same client id → 409; adapter
        // treats it as success; count(*) == 1".
        //
        // The count is the assertion that makes the status code mean something. A 409 emitted *after* the row was
        // already duplicated, or a retry that half-wrote a second row, would both look fine from the HTTP side and
        // would show up as a phantom duplicate in the list — the exact class of thing DECISION-m3-backend-api-007
        // (client-minted ids) buys with its one real cost: a retry is indistinguishable from a conflict unless the
        // primary key says so.
        await fixture.ExecuteAsync("delete from applications");
        var id = Guid.NewGuid();
        var payload = new
        {
            id = id.ToString(),
            companyName = "Pied Piper",
            jobTitle = "Engineer",
            location = "San Francisco",
            status = "Saved",
        };

        Assert.Equal(HttpStatusCode.Created, (await fixture.Http.PostAsJsonAsync("/api/applications", payload)).StatusCode);

        var retry = await fixture.Http.PostAsJsonAsync("/api/applications", payload);

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("application/problem+json", retry.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(await retry.Content.ReadAsStringAsync()))
        {
            Assert.Equal("conflict", problem.RootElement.GetProperty("code").GetString());
        }

        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "select count(*) from applications";
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);

        // The record the first request wrote is still the one on the server, byte for byte: a 409 that also
        // silently overwrote the row would satisfy every assertion above and break AC-6 in a way nobody could
        // reproduce from the response.
        using var stored = JsonDocument.Parse(await fixture.Http.GetStringAsync($"/api/applications/{id}"));
        Assert.Equal("Pied Piper", stored.RootElement.GetProperty("companyName").GetString());
    }
    [Fact]
    public async Task Create_visible_to_second_client()
    {
        // BEHAVIOR-m3-backend-api-030 (p0): "POST persists; a second client's GET sees it."
        await fixture.ExecuteAsync("delete from applications");
        var id = Guid.NewGuid();

        var created = await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id = id.ToString(),
            companyName = "Massive Dynamic",
            jobTitle = "Systems Engineer",
            location = "London",
            status = "Applied",
            appliedAt = "2026-04-02",
            notes = "Referred by Oreos",
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith($"/api/applications/{id}", created.Headers.Location?.ToString());

        // The body is the created record, not an echo of the request: the client's adapter returns the row it just
        // wrote and reads `revision` off it, so a 201 with an empty body would leave the adapter holding a record
        // it cannot later update.
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(id.ToString(), body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Massive Dynamic", body.RootElement.GetProperty("companyName").GetString());

        // The assertion the behaviour is actually named for — a *different process-equivalent* host must see it.
        using var secondHost = fixture.CreateIndependentHost();
        using var seen = JsonDocument.Parse(await secondHost.GetStringAsync($"/api/applications/{id}"));
        Assert.Equal("Massive Dynamic", seen.RootElement.GetProperty("companyName").GetString());
        Assert.Equal("Applied", seen.RootElement.GetProperty("status").GetString());
    }
}
