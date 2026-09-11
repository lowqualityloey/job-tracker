using JobTracker.Api.Data;
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

/// <summary>
/// Exposes the entry point to <c>WebApplicationFactory&lt;Program&gt;</c>. Minimal APIs compile
/// <c>Program</c> as an internal class, so without this partial declaration the integration tests could not
/// host the app in-process and would have to shell out to a live server.
/// </summary>
public partial class Program;
