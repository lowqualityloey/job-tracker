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

// CORS: the origin list comes from configuration, and an absent list means no cross-origin access at all.
//
// The failure this fixes was invisible to every test that existed: `WebApplicationFactory` answers a request without
// asking permission first, and jsdom's `fetch` is a stub that enforces nothing, so 257 green tests coexisted with an
// API no browser could use (preflight 405, and no `Access-Control-Allow-Origin` on any response). `BEHAVIOR-…-042` is
// the behaviour that found it.
//
// **Strict on origins, open on headers and methods** — the direction is the decision. `If-Match` must survive
// preflight or optimistic writes fail only in browsers, AC-7's retry key will want another custom header, and a
// hand-maintained header allow-list fails on the *next* feature rather than the current one. Headers are a
// compatibility surface here, not a defence; the origin is the boundary a browser actually enforces.
//
// `AllowCredentials` is deliberately absent: M3 sends no cookies, and a wildcard origin combined with credentials is
// rejected outright by browsers, so adding it later would break this quietly rather than loudly.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var allowed = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowed.Length > 0)
    {
        policy.WithOrigins(allowed).AllowAnyHeader().AllowAnyMethod();
    }
}));

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

// The **global** policy, not per-endpoint `.RequireCors(...)`. `-046` is the argument: the SSE endpoint was added to
// the catalog long after the CRUD routes existed, and a per-endpoint policy is exactly the thing someone forgets on
// the endpoint they add next — which would strand the stream cross-origin while every write kept working.
app.UseCors();

// Routes and handlers live in ApplicationCatalog (spec §4.1's deep module); Program is composition.
app.MapApplicationCatalog();

app.Run();



/// <summary>
/// Exposes the entry point to <c>WebApplicationFactory&lt;Program&gt;</c>. Minimal APIs compile
/// <c>Program</c> as an internal class, so without this partial declaration the integration tests could not
/// host the app in-process and would have to shell out to a live server.
/// </summary>
public partial class Program;
