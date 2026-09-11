var builder = WebApplication.CreateBuilder(args);

// Slice 0 wires only infrastructure that no behaviour test could drive yet. Endpoints, the DbContext
// registration, and the validation contract arrive with the failing test that requires them — see
// docs/tests/2026-09-11-test-m3-backend-api.md, behaviours 026 onward. The template's `MapGet("/", ...)`
// sample is deleted rather than left behind: an endpoint with no test is untested behaviour in the tree.
builder.Services.AddProblemDetails();

var app = builder.Build();

// RFC 9457 (spec DECISION-m3-backend-api-004): unhandled exceptions become application/problem+json, and
// empty 4xx/5xx responses get a problem body too, so the client adapter never parses a bare status code.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.Run();

/// <summary>
/// Exposes the entry point to <c>WebApplicationFactory&lt;Program&gt;</c>. Minimal APIs compile
/// <c>Program</c> as an internal class, so without this partial declaration the integration tests could not
/// host the app in-process and would have to shell out to a live server.
/// </summary>
public partial class Program;
