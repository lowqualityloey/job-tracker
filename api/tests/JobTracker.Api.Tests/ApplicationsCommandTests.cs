using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
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
    public async Task Delete_then_missing()
    {
        // BEHAVIOR-m3-backend-api-035 (p1): "DELETE → 204, then GET → 404".
        //
        // Both halves, because a 204 that deletes nothing and a 404 that arrives because the row never existed are
        // the two ways this can be wrong, and each is invisible if you assert only one end of the sequence. The id
        // is created through the API so the test does not depend on the seed helper for the thing being destroyed.
        await fixture.ExecuteAsync("delete from applications");
        var id = Guid.NewGuid();
        var created = await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id = id.ToString(),
            companyName = "Baxter Ltd",
            jobTitle = "Field Engineer",
            location = "Neo Tokyo",
            status = "Offer",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // The precondition is read back from the server rather than assumed, which is also what a client does after
        // a reload. F-3 (grill) made If-Match *required* on DELETE after this behaviour was registered, so 035's
        // "DELETE → 204" now means "DELETE carrying a current version → 204"; 044's Green is what proved the
        // reading, by breaking this test. Reconciled in §11 of the test register.
        using var stored = JsonDocument.Parse(await fixture.Http.GetStringAsync($"/api/applications/{id}"));
        var current = stored.RootElement.GetProperty("revision").GetUInt32();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/applications/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{current}\"");
        var deleted = await fixture.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        // 204 must not lie about having a body: an empty-body response with a Content-Length of 0 is correct, and
        // a 200-shaped payload smuggled into 204 would parse as corrupt-data on the client.
        Assert.True(deleted.Content.Headers.ContentLength is null or 0,
            $"204 carried a body: {await deleted.Content.ReadAsStringAsync()}");

        var after = await fixture.Http.GetAsync($"/api/applications/{id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Stale_if_match_conflicts_without_writing()
    {
        // BEHAVIOR-m3-backend-api-033 (p0): "Stale If-Match → 409 + code:\"conflict\" and a re-read proves the row
        // is unchanged; fails: last write wins". AC-6's clause in executable form.
        //
        // The arrangement *is* a successful PUT: a revision can only become stale if some write advanced it, so
        // this test is currently the only thing in the suite that would notice a broken update. §7's ladder has no
        // behaviour for PUT success — the third unasserted statement in §4.3, alongside the index and the ETag
        // header — so rather than invent a row, the happy path is asserted here as arrangement and the gap is
        // raised for review with two fixes: register -046, or let -042's browser run own it.
        await fixture.ExecuteAsync("delete from applications");
        var id = Guid.NewGuid();
        await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id = id.ToString(),
            companyName = "Hooli",
            jobTitle = "VP of Sales",
            location = "New York",
            status = "Saved",
        });

        using var first = JsonDocument.Parse(await fixture.Http.GetStringAsync($"/api/applications/{id}"));
        var original = first.RootElement.GetProperty("revision").GetUInt32();

        // The write that makes `original` stale. Asserted, not just performed: if this 200 never arrives, the 409
        // below would be proving an absence rather than a conflict.
        var fresh = await PutAsync(id, original, "Hooli", "VP of Partnerships", "Applied");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        using var advanced = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        Assert.Equal("VP of Partnerships", advanced.RootElement.GetProperty("jobTitle").GetString());
        var current = advanced.RootElement.GetProperty("revision").GetUInt32();
        Assert.NotEqual(original, current);

        // Now the request a second client would send if it had loaded the record before that first write landed:
        // same id, a version it can no longer claim, and a different edit.
        var stale = await PutAsync(id, original, "Hooli", "VP of Sales", "Rejected");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal("conflict", problem.RootElement.GetProperty("code").GetString());

        // The clause that makes AC-6 real: the loser's edit is absent and the winner's is intact, field by field.
        // "409 was returned" alone would be satisfied by a handler that wrote first and apologised afterwards.
        using var after = JsonDocument.Parse(await fixture.Http.GetStringAsync($"/api/applications/{id}"));
        Assert.Equal("VP of Partnerships", after.RootElement.GetProperty("jobTitle").GetString());
        Assert.Equal("Applied", after.RootElement.GetProperty("status").GetString());
        Assert.Equal(current, after.RootElement.GetProperty("revision").GetUInt32());
    }

    /// <summary>One place that spells the §4.3 update contract: full replacement, quoted <c>If-Match</c>.</summary>
    private Task<HttpResponseMessage> PutAsync(Guid id, uint revision, string companyName, string jobTitle, string status)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/applications/{id}")
        {
            Content = JsonContent.Create(new { id = id.ToString(), companyName, jobTitle, location = "New York", status }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{revision}\"");
        return fixture.Http.SendAsync(request);
    }

    [Theory]
    [InlineData("1")] // certainly not this row's version: any write since the dawn of the cluster is newer
    [InlineData(null)] // grill F-3 made If-Match *required*; a delete carrying nothing cannot prove it is current
    public async Task Delete_with_stale_revision_is_refused(string? revision)
    {
        // BEHAVIOR-m3-backend-api-044 (p0): "DELETE with a stale revision → 409 + code:\"conflict\", and the row
        // STILL EXISTS". The register carries only the stale case; the missing-header case is added here because
        // F-3's whole amendment is that the precondition is not optional, and a handler that refuses stale tokens
        // while accepting absent ones implements a rule nobody wrote. Disclosed as an additive change in §11 of
        // the test register rather than slipped in.
        await fixture.ExecuteAsync("delete from applications");
        var id = Guid.NewGuid();
        await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id = id.ToString(),
            companyName = "Weyland-Ueda",
            jobTitle = "Xeno Analyst",
            location = "LV-426",
            status = "Interview",
        });

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/applications/{id}");
        if (revision is not null)
        {
            // Quoted, exactly as §4.3 spells `ETag: "<xmin>"`. An unquoted comparison would pass this test and
            // fail the moment a real client echoes a header verbatim, where weak validators and `W/` prefixes
            // live.
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{revision}\"");
        }

        var response = await fixture.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("conflict", problem.RootElement.GetProperty("code").GetString());

        // The half that makes this a safety property rather than a status code: the refused delete must have
        // changed nothing. A handler that answers 409 *after* removing the row would pass every assertion above
        // and destroy a user's record in the process.
        using var stillThere = JsonDocument.Parse(await fixture.Http.GetStringAsync($"/api/applications/{id}"));
        Assert.Equal("Weyland-Ueda", stillThere.RootElement.GetProperty("companyName").GetString());
        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "select count(*) from applications";
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

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
