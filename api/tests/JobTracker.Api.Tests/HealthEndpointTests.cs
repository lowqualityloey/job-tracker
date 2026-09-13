using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.Api.Tests;

/// <summary>
/// M5: `GET /api/health` — the ALB health probe. Must be reachable without a session cookie (outside
/// `SessionGate.IsProtected`'s `/api/applications*` and `/api/auth/*` scopes) and must report DB reachability
/// so a sick instance fails its health check.
///
/// ## Why this test exists
///
/// The endpoint is a deploy gate: without it, the ALB marks every instance healthy regardless of DB state, and a
/// broken instance stays in the load balancer pool serving 500s to real users. A compile-only verification cannot
/// prove the endpoint is reachable or that it returns the expected contract.
///
/// ## What is not tested here
///
/// The 503 (DB unreachable) case requires a dead or unreachable Postgres, which is expensive to simulate in
/// Testcontainers (needs a stopped container or network partition). That case is verified by the infra plan's
/// manual smoke test (`docs/aws-deployment.md` §5) and by the ALB's own health-check retry logic. The unit
/// contract (200 when healthy) is what this test proves.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HealthEndpointTests(PostgresFixture postgres) : IDisposable
{
    private sealed class HealthFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            // Supply bootstrap credentials so BootGuard doesn't block the boot (we're testing health, not auth).
            builder.UseSetting("Auth:Bootstrap:Email", $"health.{Guid.NewGuid():N}@example.test");
            builder.UseSetting("Auth:Bootstrap:Password", "health-row-password-long-enough");
        }
    }

    private readonly HealthFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GET_health_returns_200_with_ok_status_when_database_is_reachable()
    {
        // Arrange: a healthy app with a real Postgres container.
        var client = _factory.CreateClient();

        // Act: hit the health endpoint without any session cookie or authentication.
        var response = await client.GetAsync("/api/health");

        // Assert: the endpoint responds 200 and reports the DB as reachable.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal("reachable", body.Database);
    }

    [Fact]
    public async Task GET_health_is_unauthenticated_and_needs_no_session_cookie()
    {
        // Arrange: a client with no cookies or auth headers.
        var client = _factory.CreateClient();

        // Act: hit the health endpoint raw.
        var response = await client.GetAsync("/api/health");

        // Assert: we got 200, not 401 or 403. The endpoint is outside the session gate's protected scopes.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // And we got a JSON body, not a problem document.
        Assert.Contains("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GET_health_is_idempotent_and_safe_to_call_repeatedly()
    {
        // Arrange: the ALB calls this every N seconds. It must not mutate state or degrade under repetition.
        var client = _factory.CreateClient();

        // Act: hit it three times in quick succession.
        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/api/health");

            // Assert each call returns 200 with the same contract.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(body);
            Assert.Equal("ok", body.Status);
        }
    }

    /// <summary>Shape of the JSON response from <c>GET /api/health</c>.</summary>
    private record HealthResponse(string Status, string Database);
}
