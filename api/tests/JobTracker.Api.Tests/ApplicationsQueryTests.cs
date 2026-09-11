using System.Net;
using JobTracker.Api.Tests.Infrastructure;
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
/// The tests deliberately speak to the API through <see cref="HttpClient"/> with **no reference to any
/// server-side type**. That is the seam the test plan chose: this is the *contract*, and a test that imported
/// <c>Application</c> to build its expectations could never notice the server returning the wrong *names*, which
/// is the exact bug class <c>AC-1</c> exists to prevent ("the frontend changes not at all").
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
        var response = await fixture.Http.GetAsync("/api/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }
}
