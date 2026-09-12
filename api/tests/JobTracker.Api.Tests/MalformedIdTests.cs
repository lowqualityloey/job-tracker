using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobTracker.Api.Tests.Infrastructure;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-057 / AC-8 — **gap 12 closed: a malformed `id` is a `400` validation problem, not a `500`.** Test-plan row
/// `-057`, seam Integration, p0. AC-8's own check is a curl:
/// `curl -s -X POST … -d '{"id":"not-a-guid",…}'` → `400` + `errors[].pointer: "/id"`.
///
/// ## The measured starting state, from a live Kestrel — not from the row's description of it
///
/// Probed with an authenticated session against `127.0.0.1:5080`, port ownership checked before and after:
///
/// | request | today |
/// | :--- | :--- |
/// | `POST` body `{"id":"not-a-guid"}` | **`500`**, no `code`, RFC 9110 generic type |
/// | `POST` body `{"id":""}` | **`500`**, no `code` |
/// | `PUT` body `{"id":"not-a-guid"}` (valid route id) | **`500`**, no `code` |
/// | `GET` route `/not-a-guid` | `404` + `code: not-found` ✓ already handled |
/// | `DELETE` route `/not-a-guid` | `404` + `code: not-found` ✓ already handled |
/// | `POST` `{"status":"Escalated"}` | `400` + `code: validation` + `pointer: "#/status"` ✓ the house shape |
///
/// `-057`'s row names the POST case. The `PUT` case is the same defect on a different verb, and the row didn't know about
/// it — **which is what a "reproduce it first" instruction is for**: the measurement widened the scope by one shape before
/// any code was written.
///
/// ## Root cause, in one line
///
/// `NewApplicationRequest.Id` is a `Guid`, so a malformed value dies in the **JSON binder**, before any handler or
/// validator runs. The binder has no idea this application has a problem-document contract, so the exception falls through
/// to the generic handler — and the response is a `500` whose type is an IETF URL with **no `code` member at all**.
/// DECISION-m3-backend-api-004's whole argument is that the frontend adapter fails closed on an unmapped code, so the
/// client's screen for "you typed a bad id" is currently a red banner reading "storage error".
///
/// It is the same class as `-051`'s `{}` body and `-055`'s garbage cookie: **the framework's own error surface is not this
/// app's error surface**, and every place a raw framework type parses user input is a place that can answer 500. The fix is
/// therefore not a try/catch around the parse — it is that the wire type must be *wid enough to hold the mistake*
/// (`string?`), so the app's validator is the one that judges it.
/// </summary>
// No [Collection]: ApplicationsApiFixture is an IClassFixture -- M3's own pattern -- so this class gets its
// own container. Sharing PostgresCollection instead would put these deletes/inserts next to the ownership tests.
public sealed class MalformedIdTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    /// <summary>Embeds the raw JSON for the id member, so wrong *types* are testable and not only wrong
    /// strings. <b>The first version had a special case returning <c>idJson</c> unchanged when it started with
    /// <c>{</c></b> — so the object-shaped row sent a body with no <c>companyName</c> at all and passed as a
    /// validation 400 for a reason unrelated to the id. The tell was arithmetic: six inputs, five failures.
    /// </summary>
    private static string BodyWithId(string idJson) =>
        $$"""{"id":{{idJson}},"companyName":"Globex","jobTitle":"Staff Engineer","status":"Applied"}""";

    private async Task<(HttpStatusCode Status, string Body)> Post(string idJson)
    {
        var response = await fixture.Http.PostAsync("/api/applications", Json(BodyWithId(idJson)));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_malformed_id_in_a_post_body_is_a_400_validation_problem()
    {
        var (status, body) = await Post("\"not-a-guid\"");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("\"validation\"", body, StringComparison.Ordinal);
        // The house pointer form is "#/id" -- Problems.Validation renders `$"#/{field}"`, and the measured bad-status case
        // answers `pointer: "#/status"`. -057's row writes it as "/id"; asserting the convention the other 400s already
        // use rather than the row's abbreviation, so the client's adapter has one shape to parse. The discrepancy is
        // recorded in the task record rather than silently resolved.
        Assert.Contains("\"#/id\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tools.ietf.org", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"\"")]                    // empty string: the classic "cleared the field" case
    [InlineData("\"7c9f1f2e\"")]            // truncated guid: a real paste failure
    [InlineData("null")]                    // explicit null
    [InlineData("123")]                     // wrong JSON type entirely
    [InlineData("{\"kind\":\"uuid\"}")]     // an object where a string belongs
    [InlineData("\"not-a-guid\"")]
    public async Task No_shape_of_malformed_id_reaches_a_500(string idJson)
    {
        // The sweep is the point, not any one row: `Guid` in a wire record means *every* non-parsing input is a binder
        // exception, and a fix that handles the two shapes someone thought of leaves the rest as 500s. Wrong JSON types
        // (numbers, objects) are the shapes nobody writes a ticket for.
        var (status, body) = await Post(idJson);

        Assert.True((int)status < 500,
            $"{idJson} produced {(int)status}: a malformed id must never reach the exception handler");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("code", out var code),
            $"no `code` member on the {(int)status} for {idJson} -- the adapter fails closed on unknown codes");
        Assert.Equal("validation", code.GetString());
    }

    [Fact]
    public async Task A_malformed_body_id_on_a_put_is_a_400_not_a_500()
    {
        // Measured: this shape 500s today and is NOT named in -057's row. Same defect, second verb.
        var id = Guid.NewGuid();
        var response = await fixture.Http.PutAsync($"/api/applications/{id}",
            Json(BodyWithId("\"not-a-guid\"")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"#/id\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_id_never_writes_a_row()
    {
        // A 400 that also inserted something would be the worst version of this fix, because "validate later, catch the
        // database error" produces exactly that. Counted in the database rather than inferred from the status code.
        var (status, _) = await Post("\"not-a-guid\"");
        Assert.Equal(HttpStatusCode.BadRequest, status);

        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "select count(*) from applications where company_name = 'Globex'";
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_valid_id_is_still_accepted_by_the_same_route()
    {
        // Positive control, and the reason the DTO change is not a free win: `Id` becomes a string on the wire, so this
        // test is what proves the parsed value still lands in the row unchanged -- a silent `Guid.Empty` from a bad
        // conversion would pass every 400 assertion above and break the only path that matters.
        var id = Guid.NewGuid();
        var response = await fixture.Http.PostAsync("/api/applications",
            Json($$"""{"id":"{{id}}","companyName":"Initech","jobTitle":"Engineer","status":"Saved"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var connection = fixture.OpenConnection();
        await connection.OpenAsync();
        await using var check = connection.CreateCommand();
        check.CommandText = "select id from applications where id = $1";
        check.Parameters.AddWithValue(id);
        Assert.Equal(id, (Guid)(await check.ExecuteScalarAsync())!);

        // And the route still agrees with the row it just created.
        Assert.Equal(HttpStatusCode.OK, (await fixture.Http.GetAsync($"/api/applications/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_malformed_route_id_stays_404_which_is_a_deliberate_asymmetry()
    {
        // Measured today and left alone on purpose: GET /api/applications/not-a-guid answers 404 not-found, because a
        // route segment that cannot name a record identifies nothing, and 404 is what an unknown id gets anyway --
        // responding 400 instead would tell a prober "that id is well-formed but not yours", which is -056's oracle
        // reopened through a status code. The body case is different: there the client is asserting a value it wants
        // stored, so the honest answer is "that value is not a GUID". Pinned so the next person does not "harmonise" them.
        foreach (var path in new[] { "/api/applications/not-a-guid" })
        {
            var get = await fixture.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Contains("not-found", await get.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }
}
