using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobTracker.Api.Tests.Infrastructure;
using Xunit;
using Xunit.Sdk;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-m3-backend-api-031 (AC-12, grill F-1) — the server half of one shared rule set.
///
/// Every case comes from <c>api/tests/fixtures/validation-cases.json</c>, which
/// <c>src/domain/validationFixtureContract.test.ts</c> reads too. That sharing *is* the AC: "the C# validator
/// cannot pass its own opinion". A validator with its own hand-written test can agree with a rule the client
/// dropped three months ago and stay green forever; this one fails the moment the two sides disagree, because the
/// expectation lives in a third file that neither side owns.
///
/// Deliberate limits, so the test is not asked to be something it is not:
///   - HTTP and JSON only. No server type is imported: a contract test that can read the implementation's own
///     constants stops testing the contract;
///   - the case's <c>id</c> is replaced with a fresh <see cref="Guid"/> before posting. All rows share one
///     placeholder id, so a second accept-case would otherwise fail with 409 from BEHAVIOR-…-034 — a real rule,
///     and not the one under test;
///   - rejecting cases assert field pointers *and* message text. The message is what a form shows a user, and it
///     is in the fixture for that reason.
/// </summary>
public sealed class ValidationContractTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    private static JsonArray LoadCases()
    {
        // Copied beside the assembly by <Content … CopyToOutputDirectory> in the csproj, so there is one file on
        // disk. A second checked-in copy would be two sources of truth — the failure mode this behaviour exists to
        // prevent, applied to the test asset itself.
        var path = Path.Combine(AppContext.BaseDirectory, "validation-cases.json");
        var document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new XunitException($"the validation fixture at {path} is not JSON");
        var cases = document["cases"]?.AsArray()
            ?? throw new XunitException("the validation fixture has no \"cases\" array");
        Assert.NotEmpty(cases);
        return cases;
    }

    public static TheoryData<string, string> FixtureCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var node in LoadCases())
            {
                if (node?.AsObject() is not { } testCase)
                {
                    throw new XunitException("a validation fixture case is not a JSON object");
                }

                data.Add(
                    testCase["id"]?.GetValue<string>() ?? throw new XunitException("a case has no id"),
                    testCase.ToJsonString());
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public Task Validation_matches_declared_cases(string caseId, string caseJson) =>
        WithCaseId(caseId, () => CheckAgainstFixture(caseId, caseJson));

    /// <summary>
    /// Prefixes every failure with its fixture case id. The first version produced seventeen red rows and no way
    /// to tell which rule each was about: xUnit prints MemberData parameter *names*, and a 300-character JSON blob
    /// is not a case label. A parameterised test that cannot identify its own case is not debuggable, and that is
    /// worth the four lines it costs.
    /// </summary>
    private static async Task WithCaseId(string caseId, Func<Task> assertions)
    {
        try
        {
            await assertions();
        }
        catch (XunitException exception)
        {
            throw new XunitException($"[{caseId}] {exception.Message}");
        }
    }

    private async Task CheckAgainstFixture(string caseId, string caseJson)
    {
        var testCase = JsonNode.Parse(caseJson)!.AsObject();
        var verdict = testCase["expect"]?["api"]?.GetValue<string>()
            ?? throw new XunitException("the case declares no api expectation");
        await fixture.ExecuteAsync("delete from applications");

        var input = testCase["input"]!.AsObject();
        input["id"] = Guid.NewGuid().ToString();

        var response = await fixture.Http.PostAsJsonAsync("/api/applications", input);

        if (verdict == "accept")
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"the fixture declares this input valid, but the API answered {(int)response.StatusCode} " +
                $"{response.StatusCode} with: {body}");

            var stored = testCase["stored"]?.AsObject();
            using var document = JsonDocument.Parse(body);
            foreach (var field in new[] { "companyName", "jobTitle", "location", "status", "notes" })
            {
                // `stored` names only the fields normalisation can move (a trim, or '' where trimming yields '');
                // everything else must come back byte-identical, which is the testable meaning of "the server is
                // not quietly editing my data".
                var expected = stored?[field]?.GetValue<string>() ?? input[field]?.GetValue<string>();
                var actual = document.RootElement.GetProperty(field);
                Assert.Equal(expected, actual.ValueKind == JsonValueKind.Null ? null : actual.GetString());
            }

            return;
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation", problem.RootElement.GetProperty("code").GetString());

        // apiViolations overrides violations for the server side only; see the fixture's $comment for the one row
        // where the two sides reject different sets of fields from the same request.
        var violations = (testCase["apiViolations"] ?? testCase["violations"])?.AsArray()
            ?? throw new XunitException("the case expects rejection but declares no violations");
        // Typed as nullable pairs on both sides: JsonElement.GetString() returns string?, so an unannotated
        // 'var' here builds List<(string, string)> and Assert.Equal refuses List<(string?, string?)> for a
        // nullability difference alone (CS8620) — an error that reads like a data bug and is a type-inference one.
        List<(string? Pointer, string? Detail)> declared = violations
            .Select(node => (
                (string?)("#/" + node!["field"]!.GetValue<string>()),
                (string?)node["message"]!.GetValue<string>()))
            .ToList();

        List<(string? Pointer, string? Detail)> returned = problem.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => (
                error.GetProperty("pointer").GetString(),
                error.GetProperty("detail").GetString()))
            .ToList();

        // One comparison of (pointer, detail) pairs as a list rather than two comparisons of two projections:
        // Assert.Equal on lists reports the first difference including length and order, so an API that collapses
        // three problems into one, or answers them in a different order than declared, fails with a message that
        // already says how.
        Assert.Equal(declared, returned);
    }
}
