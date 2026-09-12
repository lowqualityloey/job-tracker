using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobTracker.Api.Tests.Infrastructure;
using Npgsql;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// M3 Slice 1 — the read paths (BEHAVIOR-m3-backend-api-026 … 029).
///
/// One container and one host for the whole class: every test here asks the same question of the same table, and
/// sharing a fixture across a class is what xUnit's <c>IClassFixture</c> exists for. <see cref="PostgresFixture"/>
/// already proves separately that a fixture really is a cold start, so sharing here is a performance decision and
/// not a way to make a test cheaper by making it less honest.
///
/// <para>
/// The tests deliberately speak to the API through <see cref="HttpClient"/> with **no reference to any
/// server-side type**. That is the seam the test plan chose: this is the *contract*, and a test that imported
/// <c>Application</c> to build its expectations could never notice the server returning the wrong *names*, which
/// is the exact bug class <c>AC-1</c> exists to prevent ("the frontend changes not at all").
/// </para>
///
/// <para>
/// Sharing one database across a class has a price, and 027 is where it came due: a test that seeds a row makes a
/// sibling that asserts emptiness order-dependent, and xUnit gives no ordering guarantee. So every test here
/// arranges the table it needs instead of inheriting whatever a sibling left behind — the same discipline as
/// resetting module globals at file scope in the Vitest suite, in a different language, for the same reason.
/// </para>
/// </summary>
public sealed class ApplicationsQueryTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    [Fact]
    public async Task Empty_catalog_returns_200_with_an_empty_array()
    {
        // BEHAVIOR-m3-backend-api-026 (p0). `[]` and not `null`: the client's adapter calls .map() on whatever
        // comes back, so a JSON null is a TypeError in the browser that the server could have caught. The body is
        // asserted as text rather than deserialised into a typed list for the same reason — deserialising into a
        // record type would silently forgive a shape the client cannot read.
        await fixture.ExecuteAsync("delete from applications");

        var response = await fixture.Http.GetAsync("/api/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_persisted_row_is_returned_with_every_field_the_client_already_models()
    {
        // BEHAVIOR-m3-backend-api-027 (p0), the one that decides whether AC-1 is real.
        //
        // Arranged with raw SQL, not through a create endpoint: POST does not exist until Slice 2, and even once
        // it does, a test that proves GET by way of POST can never notice the two of them agreeing on the same
        // wrong thing. A round-tripping bug is invisible to a round-trip test.
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync("delete from applications");
        await using (var connection = fixture.OpenConnection())
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            // -056: rows now belong to an account, so arranging one with SQL must name the owner too. The
            // subselect resolves to the fixture's bootstrap account -- the same one its HttpClient logs in as.
            insert.CommandText = """
                insert into applications
                  (id, company_name, job_title, location, status, applied_at, notes, created_at, updated_at, owner_id)
                values
                  (@id, 'Globex GmbH', 'Staff Engineer', 'Berlin', 'Interview',
                   date '2026-03-07', 'Second round, with the founder',
                   timestamptz '2026-03-07T09:15:00Z', timestamptz '2026-03-07T09:15:00Z', (select id from users order by created_at limit 1))
                """;
            insert.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        using var document = JsonDocument.Parse(await fixture.Http.GetStringAsync("/api/applications"));
        var row = Assert.Single(document.RootElement.EnumerateArray());
        var fields = row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        // Values, field by field, against the names src/types/application.ts already uses.
        Assert.Equal(id.ToString(), Required(fields, "id").GetString());
        Assert.Equal("Globex GmbH", Required(fields, "companyName").GetString());
        Assert.Equal("Staff Engineer", Required(fields, "jobTitle").GetString());
        Assert.Equal("Berlin", Required(fields, "location").GetString());
        Assert.Equal("Interview", Required(fields, "status").GetString());
        Assert.Equal("Second round, with the founder", Required(fields, "notes").GetString());

        // The two assertions this behaviour exists for.
        //
        // applied_at is a `date` in §4.1 precisely so that "on which day did I apply" cannot be answered by the
        // server's timezone. A DateTime-backed property would serialise as 2026-03-07T00:00:00Z — or, worse, as
        // 2026-03-06T23:00:00 in a negative-offset zone, which is the off-by-one-day bug the spec named and
        // refused to import. Exact text, no tolerance: the frontend compares these strings.
        Assert.Equal("2026-03-07", Required(fields, "appliedAt").GetString());
        Assert.Equal("2026-03-07T09:15:00Z", Required(fields, "createdAt").GetString());

        // Naming, as a property of the whole response rather than of each field I happened to list. EF's default
        // is the CLR name verbatim ("CompanyName") and PostgreSQL's habit is snake_case (company_name); either
        // leak passes a per-field assertion the day someone adds a ninth column, because nobody asserts on the
        // column they forgot. This catches the forgetting.
        Assert.All(fields.Keys, name =>
        {
            Assert.False(char.IsUpper(name[0]), $"PascalCase field name reached the wire: {name}");
            Assert.DoesNotContain("_", name);
        });
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")] // well-formed, absent
    [InlineData("not-a-uuid")] // well-formedness is not the client's job to guarantee
    public async Task An_unknown_id_answers_404_with_a_problem_document(string id)
    {
        // BEHAVIOR-m3-backend-api-028 (p0). The status is the headline, but the *content type* is the assertion
        // that keeps a promise DECISION-m3-backend-api-004 made: an error response is never a bare status code,
        // because the client's adapter must produce `code: 'not-found'` with an id rather than guess from a
        // reason phrase some proxy rewrote.
        //
        // Both cases are one behaviour — "no such record" — so they are one theory. The tempting alternative is a
        // route constraint ({id:guid}) that answers `not-a-uuid` with the framework's own empty 404: it would
        // satisfy this test's sibling and break the envelope rule. Guid.TryParse in the handler is the version
        // that keeps every 404 a problem document.
        var response = await fixture.Http.GetAsync($"/api/applications/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("not-found", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_known_id_answers_200_with_that_record_and_nothing_else()
    {
        // BEHAVIOR-m3-backend-api-028's positive half, and the reason the 404 above is a fact about the row
        // rather than about routing: with only the unknown-id case in the register, "answer 404 always" would be
        // green.
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync("delete from applications");
        // Timestamps spelled out, not defaulted: §4.1's `DEFAULT now()` belongs to the column, but nothing has
        // made the server write a row yet — that is Slice 2's POST, whose Red will want it. Until then the seed
        // states the instant itself, which keeps 028 about "can a known row be found by id" and not about who
        // fills in audit columns.
        await fixture.ExecuteAsync(
            $"insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id) values ('{id}', 'Initech', 'Engineer', 'Saved', timestamptz '2026-01-05T10:00:00Z', timestamptz '2026-01-05T10:00:00Z', (select id from users order by created_at limit 1))");

        var response = await fixture.Http.GetAsync($"/api/applications/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var row = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id.ToString(), row.RootElement.GetProperty("id").GetString());
        Assert.Equal("Initech", row.RootElement.GetProperty("companyName").GetString());
        // The null half of §4.1: location and notes are absent from the seed and applied_at is null, while the
        // client models all three as optional. A 500 from a null-ref, or a location coerced to "", is what this
        // pins; Slice 3's adapter decides how to read them, and must not have to guess.
        Assert.Equal(JsonValueKind.Null, row.RootElement.GetProperty("location").ValueKind);
        Assert.Equal("Saved", row.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Revision_changes_after_update()
    {
        // BEHAVIOR-m3-backend-api-029 (p0). The register says exactly how this must fail first: "field absent".
        //
        // `revision` is the number every optimistic-concurrency behaviour in Slice 2 is built on (033's stale
        // write, 044's missing If-Match), and it comes from PostgreSQL's `xmin` rather than from a counter the
        // application maintains. Two properties matter and both are asserted here, because neither is what a
        // reviewer would think to check: it must be **stable across reads** (a GET that bumps the version would
        // make every If-Match stale before it was used, and the failure would look like a network race), and it
        // must **change on any write** (a version that only moved when a field value changed would miss the
        // `updated_at`-only updates the client cannot see).
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync("delete from applications");
        await fixture.ExecuteAsync(
            $"insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id) values ('{id}', 'Umbrella Co', 'Engineer', 'Saved', timestamptz '2026-02-01T00:00:00Z', timestamptz '2026-02-01T00:00:00Z', (select id from users order by created_at limit 1))");

        var first = await RevisionAsync();
        var repeated = await RevisionAsync();
        Assert.Equal(first, repeated);

        await fixture.ExecuteAsync($"update applications set status = 'Applied' where id = '{id}'");

        Assert.NotEqual(first, await RevisionAsync());
    }

    /// <summary>The one row's `revision` as the JSON number the client's `revision?: number` expects.</summary>
    private async Task<long> RevisionAsync()
    {
        using var document = JsonDocument.Parse(await fixture.Http.GetStringAsync("/api/applications"));
        var row = Assert.Single(document.RootElement.EnumerateArray());

        Assert.True(row.TryGetProperty("revision", out var revision),
            $"the list response carries no `revision` field; fields present: {string.Join(", ", row.EnumerateObject().Select(p => p.Name).Order())}");
        // Number and not string: DECISION-m3-backend-api-006 added `revision?: number` to the domain type, and a
        // quoted xmin would compare unequal on the client's first If-Match while still *looking* correct in a
        // debugger.
        Assert.Equal(JsonValueKind.Number, revision.ValueKind);
        return revision.GetInt64();
    }

    /// <summary>
    /// A missing field is a named failure listing what did arrive, not a <see cref="KeyNotFoundException"/> from
    /// an indexer — the difference between "the server sent no companyName" and "my dictionary threw".
    /// </summary>
    private static JsonElement Required(IReadOnlyDictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value)
            ? value
            : throw new Xunit.Sdk.XunitException(
                $"The response carries no '{name}' field. Fields present: {string.Join(", ", fields.Keys.Order())}");
}
