using JobTracker.Api.Data;
using Npgsql;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure that no behaviour test can drive. RFC 9457 was wired in Slice 0 because it is a
// cross-cutting configuration of the pipeline rather than a behaviour; endpoints are not, so they arrive only
// with the failing test that requires them. The template's `MapGet("/", ...)` sample stays deleted: an endpoint
// with no test is untested behaviour in the tree.
builder.Services.AddProblemDetails();

// BEHAVIOR-m3-backend-api-026: a catalog to be empty of. Registered here because the Red test above it fails
// with 404 without it, and not earlier because nothing needed a database until a test asked the API one.
builder.Services.AddDbContext<JobTrackerDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

// Migrations apply at startup. Declared as a decision, not a default, because it has a real failure mode: two
// instances racing the same migration, or a slow migration holding the process open past a health check. Both
// are acceptable while there is one instance, no production data and nothing to be down — §4.2 keeps every M3
// change expand-only, so a migration can never take a lock that breaks a reader. Who applies migrations at
// *deploy* time is M5's subject (pk:ship), and the integration fixture depends on this line: it is how a
// Testcontainers database gets its schema without the test reaching into EF internals.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<JobTrackerDb>().Database.Migrate();
}

// RFC 9457 (spec DECISION-m3-backend-api-004): unhandled exceptions become application/problem+json, and
// empty 4xx/5xx responses get a problem body too, so the client adapter never parses a bare status code.
app.UseExceptionHandler();
app.UseStatusCodePages();

// BEHAVIOR-m3-backend-api-026. AsNoTracking because this list is never edited in place, and tracking it would
// mean the read path keeps a change tracker alive for no reason. CancellationToken is bound by the framework to
// the request aborting: a client that navigates away should stop the query rather than finish it into a socket
// nobody reads.
app.MapGet("/api/applications", async (JobTrackerDb db, CancellationToken ct) =>
    await db.Applications.AsNoTracking().ToListAsync(ct));

// BEHAVIOR-m3-backend-api-028. The lambda's inferred return type is Task<IResult>: both arms are results rather
// than values, and letting the compiler arrive at that is clearer than annotating a union type by hand.
app.MapGet("/api/applications/{id}", async (string id, JobTrackerDb db, CancellationToken ct) =>
{
    // Guid.TryParse rather than a {id:guid} route constraint: a malformed id is the same fact to the client
    // ("there is no such record") and must not leave the endpoint answering with a framework envelope that has
    // no `code` in it. See 028's Red commit for the measured body of that default.
    if (!Guid.TryParse(id, out var guid))
    {
        return NotFound(id);
    }

    var application = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == guid, ct);
    return application is null ? NotFound(id) : Results.Ok(application);
});

// BEHAVIOR-m3-backend-api-030. Deliberately permissive about *values*: the status CHECK is enforced by the
// database (032), and the field-by-field validator is 031's registered subject, which brings the shared fixture
// with it. What this handler must do is refuse nothing the schema would accept, and return the row the client
// needs — including `revision`, which EF populates on save because xmin is a store-generated concurrency token.
app.MapPost("/api/applications", async (NewApplicationRequest request, JobTrackerDb db, CancellationToken ct) =>
{
    var entity = new Application
    {
        Id = request.Id,
        CompanyName = request.CompanyName,
        JobTitle = request.JobTitle,
        Location = request.Location,
        Status = request.Status,
        AppliedAt = request.AppliedAt,
        Notes = request.Notes,
        // Left at default on purpose: created_at/updated_at are the database's now(), and a client-supplied
        // creation time would let a retry claim the record is older than it is.
    };

    db.Applications.Add(entity);
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        // BEHAVIOR-m3-backend-api-034. The id is client-minted (DECISION-m3-backend-api-007), so the only thing
        // that distinguishes "create this" from "I already sent you this" is the primary key. Letting it reach the
        // exception handler would surface a duplicate as `storage-error` — the client's mapping has no other word
        // for a 500 — and the user would be told storage failed about a record that saved fine on the first try.
        return Conflict(request.Id);
    }

    // Results.Created rather than Ok: the 201 is what the client's adapter distinguishes a fresh create from, and
    // the Location header is the canonical path 033/044 will address with If-Match.
    return Results.Created($"/api/applications/{entity.Id}", entity);
});

app.Run();



/// <summary>
/// The 404 the client's adapter can read. RFC 9457 with `code` as an extension member
/// (DECISION-m3-backend-api-004): ASP.NET's own problem document already carries type/title/status, so what this
/// adds is the machine-readable discriminator and a `type` URI that names *this* problem rather than the HTTP
/// status section. `instance` records the request path the client actually used.
/// </summary>
static IResult NotFound(string id) => Results.Problem(
    title: "No application record exists with that id.",
    statusCode: StatusCodes.Status404NotFound,
    type: "https://job-tracker.local/probs/not-found",
    instance: $"/api/applications/{id}",
    extensions: new Dictionary<string, object?> { ["code"] = "not-found" });

/// <summary>The 409 every write path can hit: same id (034) or stale `If-Match` (033/044), one shape for both.</summary>
static IResult Conflict(Guid id) => Results.Problem(
    title: "Another request already wrote that record, or this one is based on a stale version.",
    statusCode: StatusCodes.Status409Conflict,
    type: "https://job-tracker.local/probs/conflict",
    instance: $"/api/applications/{id}",
    extensions: new Dictionary<string, object?> { ["code"] = "conflict" });

/// <summary>
/// Exposes the entry point to <c>WebApplicationFactory&lt;Program&gt;</c>. Minimal APIs compile
/// <c>Program</c> as an internal class, so without this partial declaration the integration tests could not
/// host the app in-process and would have to shell out to a live server.
/// </summary>
public partial class Program;

/// <summary>
/// The write contract of §4.3 — the fields a user supplies, which is exactly <c>ApplicationInput</c> plus the
/// client-minted <c>id</c> (DECISION-m3-backend-api-007). Record and not class: it is a boundary shape with no
/// behaviour, and `required` members mean a payload missing company_name fails during binding rather than as a
/// null-ref somewhere downstream.
/// </summary>
public sealed record NewApplicationRequest(
    Guid Id,
    string CompanyName,
    string JobTitle,
    string? Location,
    string Status,
    DateOnly? AppliedAt,
    string? Notes);
