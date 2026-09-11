using JobTracker.Api;
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

// BEHAVIOR-m3-backend-api-046: the fan-out behind `GET /api/applications/events`. A singleton because its whole
// purpose is to connect a request that *writes* to requests that are *already open* — scoped or transient would give
// every connection a bus of one. It is also the visible form of ASSUMPTION-m3-backend-api-002 (single instance
// through M5): state lives in this process, so a second instance would notify only its own subscribers. Lifting that
// assumption means replacing this class with a Redis backplane or PostgreSQL `NOTIFY`, not rewriting the endpoints.
builder.Services.AddSingleton<ApplicationEventBus>();

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

// Routes and handlers live in ApplicationCatalog (spec §4.1's deep module); Program is composition.
app.MapApplicationCatalog();

app.Run();



/// <summary>
/// Exposes the entry point to <c>WebApplicationFactory&lt;Program&gt;</c>. Minimal APIs compile
/// <c>Program</c> as an internal class, so without this partial declaration the integration tests could not
/// host the app in-process and would have to shell out to a live server.
/// </summary>
public partial class Program;
