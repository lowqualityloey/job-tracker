using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobTracker.Api.Auth;
using JobTracker.Api.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using JobTracker.Api.Tests.Infrastructure;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-056 / AC-7 (+ AC-9's guard) — **another account's row does not exist, in every direction.** Test-plan row
/// `-056`, seam Integration, priority p0.
///
/// ## The 403 rule, and why it is the interesting part
///
/// A `403` says *"that row exists and you may not have it"*, which hands an attacker a membership test over the id space —
/// AC-7's own words: *"403 is an oracle"*. The fix is not an if-statement about permissions; it is that every query carries
/// `owner_id` in its predicate, so a foreign row and a nonexistent row produce the identical `404 not-found`.
///
/// Which is why the sharpest test here is not the `GET`. It is
/// <see cref="B_writing_A_s_row_gets_404_even_with_a_correct_if_match"/>: a `PUT` that supplies A's *real* revision number
/// and still must not learn anything. Without owner scoping, that request returns `412`/`409` — and **a concurrency token is
/// an existence oracle all by itself**, because only someone who could read the row could know its revision. 404-vs-403 is
/// the claim the row makes; 404-vs-412 is the claim nobody wrote down and the one that would survive a careful review of
/// the naive implementation.
///
/// ## What AC-9 contributes to this file
///
/// `-056` cannot exist without `owner_id`, and AC-9 requires the column to arrive expand→contract **with the assertion
/// before the constraint**, applied to a *non-empty* database. The non-empty rehearsal was run against the real dev
/// database and is recorded in `docs/tasks/TASK-m4-authentication.md` §6 (three rows, zero accounts → expand leaves them
/// unowned → contract raises naming the count → the documented remedy makes it succeed). What is asserted *here* is the
/// guard as a statement — the scratch table under <see cref="AddApplicationOwnerContract.AssertionSql"/> — because a
/// migration is otherwise a script you can only watch succeed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OwnershipTests(PostgresFixture postgres)
{
    private const string EmailA = "owner-a@example.test";
    private const string EmailB = "owner-b@example.test";
    private const string PasswordA = "the-owner-a-password-1af";
    private const string PasswordB = "the-owner-b-password-2b0";

    private sealed class OwnershipFactory(string cs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", cs);
            // A is the bootstrap account (the product's own seed path); B is inserted directly, because M4 has no
            // registration endpoint -- spec 2.5 puts signup out of scope, so a second account is arrange-by-SQL by
            // necessity, not by convenience.
            builder.UseSetting("Auth:Bootstrap:Email", EmailA);
            builder.UseSetting("Auth:Bootstrap:Password", PasswordA);
        }
    }

    /// <summary>
    /// ONE host for the whole class, which stopped being cosmetic in `-063`.
    ///
    /// This file used to build a fresh `WebApplicationFactory` per request. That was harmless while the only thing a
    /// request carried was a session id: the sessions table is shared, so any host accepts it. An antiforgery token is
    /// different — it is protected by the **data-protection key ring of the instance that minted it**, so a host that
    /// dies between the login and the write cannot read its own value. The symptom was six failures here saying
    /// `create failed: 403 …/probs/antiforgery`, raised by a `Send` that looked entirely correct.
    ///
    /// The test fix is one host. The deployment consequence is the same fact at a different scale, and it is recorded in
    /// `Antiforgery`'s comment rather than left for whoever first puts this behind a load balancer.
    /// </summary>
    private readonly WebApplicationFactory<Program> _host = new OwnershipFactory(postgres.ConnectionString);

    private WebApplicationFactory<Program> Host() => _host;

    private async Task EnsureSecondAccountAsync()
    {
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Real verifier material, not a placeholder: B logs in through the endpoint, so its hash must verify at the same
        // cost as A's. A fake string here would make "both paths do the work" untestable.
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
        // Order matters and is not obvious: the container is empty until a host boots, because boot is what runs
        // Migrate(). Inserting the second account first hits `relation "users" does not exist` (42P01) -- which is what the
        // first version of this helper did. Creating the client is the thing that creates the schema.
        var http = Host().CreateClient();
        await EnsureSecondAccountAsync();
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return TestCookies.SessionOf(response);
    }

    private async Task<HttpResponseMessage> Send(string cookie, HttpMethod method, string path,
        object? body = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, path);
        TestCookies.Attach(request, cookie);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        // The client is created per call and disposed with the request; the point of a fresh one each time is that no
        // cookie jar can quietly carry a session between the two accounts under test.
        return await Host().CreateClient().SendAsync(request);
    }

    private async Task<Guid> CreateAs(string cookie, string company)
    {
        var id = Guid.NewGuid();
        var response = await Send(cookie, HttpMethod.Post, "/api/applications",
            new { id, companyName = company, jobTitle = "Engineer", status = "Applied" });
        Assert.True(response.IsSuccessStatusCode, $"create failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return id;
    }

    private async Task<string> RevisionOf(string cookie, Guid id)
    {
        var read = await Send(cookie, HttpMethod.Get, $"/api/applications/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var doc = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("revision").ToString();
    }

    [Fact]
    public async Task B_reading_A_s_row_gets_404_not_403()
    {
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        var id = await CreateAs(a, "Stark Industries");

        var response = await Send(b, HttpMethod.Get, $"/api/applications/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The body is the same envelope the "never existed" case produces, `code` included: a distinct problem type for
        // "exists but not yours" would reintroduce the oracle through the payload while getting the status right. Compare
        // members rather than raw text, because the framework's per-request traceId makes byte equality impossible --
        // learned the hard way in -053 and recorded in the task record.
        Assert.Contains("not-found", body, StringComparison.Ordinal);
        var nobody = await Send(b, HttpMethod.Get, $"/api/applications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
        using var foreignDoc = JsonDocument.Parse(body);
        using var missingDoc = JsonDocument.Parse(await nobody.Content.ReadAsStringAsync());
        foreach (var member in new[] { "type", "title", "status", "code" })
        {
            Assert.Equal(missingDoc.RootElement.GetProperty(member).ToString(),
                         foreignDoc.RootElement.GetProperty(member).ToString());
        }
    }

    [Fact]
    public async Task B_writing_A_s_row_gets_404_even_with_a_correct_if_match()
    {
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        var id = await CreateAs(a, "Wayne Enterprises");
        var revision = await RevisionOf(a, id); // read through A, so the value is genuinely the row's current one

        var response = await Send(b, HttpMethod.Put, $"/api/applications/{id}",
            new { id, companyName = "Hijacked", jobTitle = "Engineer", status = "Applied" }, ifMatch: revision);

        // 409/412 here would mean the concurrency token leaked the row's existence to someone who cannot read it -- an
        // oracle through a code path the 403 rule never touched. Owner scoping in the same predicate as the id lookup is
        // what makes this 404 rather than "found, then refused".
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("conflict", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task B_deleting_A_s_row_gets_404_and_the_row_survives()
    {
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        var id = await CreateAs(a, "Cyberdyne");
        var revision = await RevisionOf(a, id);

        var response = await Send(b, HttpMethod.Delete, $"/api/applications/{id}", ifMatch: revision);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Status alone would also be returned by an implementation that deleted the row and then lied about it. The
        // database gets the vote, per the test plan's rule for persistence effects.
        Assert.Equal(HttpStatusCode.OK, (await Send(a, HttpMethod.Get, $"/api/applications/{id}")).StatusCode);
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from applications where id = $1";
        cmd.Parameters.AddWithValue(id);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task B_s_list_never_contains_A_s_rows_and_vice_versa()
    {
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        var aId = await CreateAs(a, "Soylent Corp");
        var bId = await CreateAs(b, "Massive Dynamic");

        var aIds = await IdsOf(a);
        var bIds = await IdsOf(b);

        Assert.Contains(bId, bIds);
        Assert.DoesNotContain(aId, bIds);
        Assert.Contains(aId, aIds);
        Assert.DoesNotContain(bId, aIds);

        // And the single-row read agrees with the list: a scoping rule applied to one of the two is the most common way
        // this ships broken, because the list is where the leak is visible and the by-id path is where it is exploitable.
        Assert.Equal(HttpStatusCode.OK, (await Send(b, HttpMethod.Get, $"/api/applications/{bId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(b, HttpMethod.Get, $"/api/applications/{aId}")).StatusCode);
    }

    [Fact]
    public async Task An_owner_can_still_do_everything_with_its_own_rows()
    {
        // Positive control, and the reason the three 404 assertions above are not just "nobody can do anything".
        var a = await CookieFor(EmailA, PasswordA);
        var id = await CreateAs(a, "Pied Piper");
        var revision = await RevisionOf(a, id);

        var updated = await Send(a, HttpMethod.Put, $"/api/applications/{id}",
            new { id, companyName = "Pied Piper", jobTitle = "Engineer", status = "Interview" }, ifMatch: revision);
        // 200 + body, per the M3 contract (ApplicationCatalog returns Results.Ok(entity) for a successful PUT). Asserted
        // against what the endpoint does, not what a REST convention says it should do -- my first draft expected 204 here
        // and the run corrected it.
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // The revision moved, so deleting with the value read before the update must be a conflict, not a success: this is
        // M3's optimistic concurrency still working *under* owner scoping, which no other test in this file covers.
        var staleDelete = await Send(a, HttpMethod.Delete, $"/api/applications/{id}", ifMatch: revision);
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode); // Problems.Conflict is 409, not 412

        var freshRevision = await RevisionOf(a, id);
        var deleted = await Send(a, HttpMethod.Delete, $"/api/applications/{id}", ifMatch: freshRevision);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(a, HttpMethod.Get, $"/api/applications/{id}")).StatusCode);
    }

    [Fact]
    public async Task Ownership_cannot_be_forged_from_the_request_body()
    {
        // The create handler takes its owner from the gate, not the payload. A client that sends `ownerId` must be ignored
        // -- not rejected with a 400 (unknown members are not this app's error surface) and certainly not honoured.
        var a = await CookieFor(EmailA, PasswordA);
        var b = await CookieFor(EmailB, PasswordB);
        var id = Guid.NewGuid();

        var response = await Send(a, HttpMethod.Post, "/api/applications", new
        {
            id,
            companyName = "Wonk Co",
            jobTitle = "Engineer",
            status = "Applied",
            ownerId = (await IdsOf(b)).Count == 0 ? Guid.NewGuid() : Guid.NewGuid(),
        });
        Assert.True(response.IsSuccessStatusCode);

        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select owner_id from applications where id = $1";
        cmd.Parameters.AddWithValue(id);
        var stored = (Guid)(await cmd.ExecuteScalarAsync())!;

        // Compare against the real owner rather than "not empty": Guid.Empty would pass a null-coalescing bug, which is
        // exactly the default EF tried to give this column (see -056's record).
        var expected = await OwnerOf(EmailA);
        Assert.Equal(expected, stored);
    }

    [Fact]
    public async Task The_contract_guard_raises_on_one_unowned_row_and_passes_with_none()
    {
        // AC-9's "assertion before the constraint" as a testable statement rather than a step in a migration nobody can
        // re-run. A scratch table keeps this independent of the real applications table, so it cannot be perturbed by the
        // other tests in this collection -- and the guard's SQL is the same text the migration executes.
        const string Table = "applications_guard_probe";
        await using (var conn = new NpgsqlConnection(postgres.ConnectionString))
        {
            await conn.OpenAsync();
            await using (var setup = conn.CreateCommand())
            {
                setup.CommandText = $"create temporary table if not exists {Table} (id uuid, owner_id uuid);";
                await setup.ExecuteNonQueryAsync();
            }

            await using (var clean = conn.CreateCommand())
            {
                clean.CommandText = $"delete from {Table}; insert into {Table} (id, owner_id) values (gen_random_uuid(), gen_random_uuid());";
                await clean.ExecuteNonQueryAsync();
            }

            await using (var passes = conn.CreateCommand())
            {
                passes.CommandText = AddApplicationOwnerContract.AssertionSql(Table);
                await passes.ExecuteNonQueryAsync(); // must not throw
            }

            await using (var dirty = conn.CreateCommand())
            {
                dirty.CommandText = $"insert into {Table} (id, owner_id) values (gen_random_uuid(), null);";
                await dirty.ExecuteNonQueryAsync();
            }

            var error = await Assert.ThrowsAsync<PostgresException>(() =>
            {
                using var raises = conn.CreateCommand();
                raises.CommandText = AddApplicationOwnerContract.AssertionSql(Table);
                return raises.ExecuteNonQueryAsync();
            });
            Assert.Equal("P0001", error.SqlState);
            // The count and the remedy are the point of the message: an operator reading it must know how many rows are
            // unowned and that a missing account -- not a missing column -- is the usual cause.
            Assert.Contains("AC-9 guard", error.MessageText, StringComparison.Ordinal);
            Assert.Contains("1 of rows", error.MessageText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_owner_column_has_no_database_default_and_refuses_null()
    {
        // Pinned because EF emitted `defaultValue: new Guid("0000...")` when it generated the contract migration, and
        // that default is the "owned by nobody" state -056 exists to make unreachable -- it also converts a clear 23502
        // (cannot be null) into an opaque 23503 (violates the FK to users). Removing the default is the fix; this is the
        // test that stops it coming back on the next regeneration.
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select coalesce(column_default, 'NONE') || '|' || is_nullable from information_schema.columns where table_name = 'applications' and column_name = 'owner_id'";
        // One scalar, not a reader: an open reader on this connection made the NEXT command on it throw
        // NpgsqlOperationInProgressException, which the run reported as a product failure and is purely my plumbing.
        Assert.Equal("NONE|NO", (string?)await cmd.ExecuteScalarAsync());

        await using var insert = conn.CreateCommand();
        insert.CommandText = """
            insert into applications (id, company_name, job_title, status, created_at, updated_at, owner_id)
            values (gen_random_uuid(), 'Orphan Works', 'Engineer', 'Applied', now(), now(), null)
            """;
        var error = await Assert.ThrowsAsync<PostgresException>(insert.ExecuteNonQueryAsync);
        Assert.Equal("23502", error.SqlState); // not_null_violation, from the server
    }

    private async Task<List<Guid>> IdsOf(string cookie)
    {
        var response = await Send(cookie, HttpMethod.Get, "/api/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. doc.RootElement.EnumerateArray().Select(e => Guid.Parse(e.GetProperty("id").GetString()!))];
    }

    private async Task<Guid> OwnerOf(string email)
    {
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select id from users where email = $1::citext";
        cmd.Parameters.AddWithValue(email);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }
}
