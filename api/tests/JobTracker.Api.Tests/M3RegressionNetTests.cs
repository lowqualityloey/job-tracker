using System.Net;
using System.Reflection;
using JobTracker.Api.Tests.Infrastructure;
using Xunit.Sdk;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-058 / AC-13 — **all 65 M3 API tests pass under auth with <c>Skipped: 0</c>**, expressed as a net rather than a
/// sentence. Test-plan row `-058`, seam Integration, p0.
///
/// ## Why this row is not a TDD row, and what replaced the Red
///
/// AC-13 asserts a property that is *true today*: `-050` and `-055` made the harness authenticate through
/// <c>POST /api/auth/login</c>, and every M3 class has been riding that session since — the suite has reported
/// <c>Skipped: 0</c> on every run since. So there is no failing behaviour to reproduce, and pretending otherwise would mean
/// writing a test against a bug that does not exist. The honest substitute is a **mutation probe**: each guard below was run
/// against a deliberately broken tree (an <c>Skip</c> added to an M3 test; a test method deleted; one <c>InlineData</c> row
/// removed) and the measured failure is recorded in the task record. **A regression net nobody has proven to catch anything
/// is a comment with an <c>Assert</c> in it.**
///
/// ## The number, measured rather than inherited
///
/// AC-13 says "65", which is M3's delivered count and was reasonable to distrust after eight M4 behaviours had rewritten the
/// harness underneath it. It resolves exactly, and this file is what keeps it resolving:
///
/// | class | methods | cases |
/// | :--- | ---: | ---: |
/// | `ValidationContractTests` | 1 | 36 |
/// | `ApplicationsCommandTests` | 6 | 7 |
/// | `ApplicationsQueryTests` | 5 | 6 |
/// | `CorsContractTests` | 5 | 5 |
/// | `EventStreamTests` | 4 | 4 |
/// | `PostgresHarnessTests` | 4 | 4 |
/// | `ApplicationConstraintTests` | 3 | 3 |
/// | | **28** | **65** |
///
/// Case counts come from the data attributes themselves (an <c>InlineData</c> per row, a <c>MemberData</c> property
/// enumerated, a <c>ClassData</c> type instantiated) because **method-level equality would leave a hole**: deleting one row
/// from a 36-row table removes 35 assertions while every method name stays in place. `ValidationContractTests` is exactly
/// that shape — one method, more than half of M3's whole coverage — which is why the count is per method and not per class.
///
/// ## What this net does NOT catch, stated plainly
///
/// Assertion *weakening* — replacing <c>Assert.Equal(404, …)</c> with <c>Assert.True(status &lt; 500)</c> keeps every name and
/// every count and passes all of this. That failure mode is a review problem, not a reflection problem, and the record says
/// so instead of implying the machine has the whole job.
/// </summary>
public sealed class M3RegressionNetTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    /// <summary>
    /// The frozen manifest, generated from <c>dotnet test --list-tests</c> output rather than recalled — the runner is the
    /// only thing that knows how a theory's data rows expand. Regenerate and review the diff if M3's coverage legitimately
    /// changes; a silent change to this array is what the whole file exists to prevent.
    /// </summary>
    private static readonly (string Class, string Method)[] M3Manifest =
    [
        ("ApplicationConstraintTests", "All_five_statuses_the_client_models_are_accepted_by_the_database"),
        ("ApplicationConstraintTests", "No_text_column_carries_an_unchosen_default"),
        ("ApplicationConstraintTests", "Status_check_constraint_rejects_sixth_value"),
        ("ApplicationsCommandTests", "Create_visible_to_second_client"),
        ("ApplicationsCommandTests", "Delete_then_missing"),
        ("ApplicationsCommandTests", "Delete_with_stale_revision_is_refused"),
        ("ApplicationsCommandTests", "Non_json_body_rejected"),
        ("ApplicationsCommandTests", "Retry_of_create_is_idempotent"),
        ("ApplicationsCommandTests", "Stale_if_match_conflicts_without_writing"),
        ("ApplicationsQueryTests", "A_known_id_answers_200_with_that_record_and_nothing_else"),
        ("ApplicationsQueryTests", "A_persisted_row_is_returned_with_every_field_the_client_already_models"),
        ("ApplicationsQueryTests", "An_unknown_id_answers_404_with_a_problem_document"),
        ("ApplicationsQueryTests", "Empty_catalog_returns_200_with_an_empty_array"),
        ("ApplicationsQueryTests", "Revision_changes_after_update"),
        ("CorsContractTests", "A_preflight_for_delete_names_delete"),
        ("CorsContractTests", "A_read_from_an_allowed_origin_carries_the_origin_header"),
        ("CorsContractTests", "An_unlisted_origin_gets_an_answer_but_no_permission"),
        ("CorsContractTests", "Preflight_from_an_allowed_origin_is_answered_with_the_policy"),
        ("CorsContractTests", "The_event_stream_is_cross_origin_readable_too"),
        ("EventStreamTests", "A_committed_create_publishes_a_change_event_carrying_its_id"),
        ("EventStreamTests", "A_committed_update_and_delete_also_publish"),
        ("EventStreamTests", "A_rejected_write_publishes_nothing"),
        ("EventStreamTests", "Event_stream_is_served_as_text_event_stream"),
        ("PostgresHarnessTests", "A_check_constraint_is_enforced_by_this_container_and_not_silently_accepted"),
        ("PostgresHarnessTests", "Database_under_test_was_started_by_this_run_not_reused_from_an_earlier_one"),
        ("PostgresHarnessTests", "Dotnet_process_reaches_the_pinned_container_and_reports_the_pinned_server_version"),
        ("PostgresHarnessTests", "Harness_is_independent_of_whatever_database_the_developer_is_running"),
        ("ValidationContractTests", "Validation_matches_declared_cases"),
    ];

    private const int Ac13ExpectedCases = 65;

    private static IEnumerable<MethodInfo> M3TestMethods(Assembly assembly) =>
        assembly.DefinedTypes
            .Where(t => M3Manifest.Select(m => m.Class).Contains(t.Name, StringComparer.Ordinal))
            .SelectMany(t => t.DeclaredMethods.Where(m => m.GetCustomAttribute<FactAttribute>(inherit: true) is not null));

    /// <summary>
    /// How many assertions the runner will actually execute for a method: one per <c>InlineData</c>, the enumerated size of a
    /// <c>MemberData</c> member or <c>ClassData</c> collection, otherwise one.
    /// </summary>
    private static int CaseCount(MethodInfo method)
    {
        var inline = method.GetCustomAttributes<InlineDataAttribute>(inherit: true).Count();
        if (inline > 0)
        {
            return inline;
        }

        // MemberDataAttribute.Type and ClassDataAttribute.ClassType are not public in this xUnit version, and M3's classes
        // never use them: every case table here is declared on the same class as its test. So the declaring type is the
        // whole search, and an unresolvable member is a LOUD failure rather than a silent `return 1` -- a counter that
        // under-counts would make AC-13's number look wrong in the safe direction, which is the worst possible bug in a
        // regression net.
        var memberData = method.GetCustomAttributes<MemberDataAttribute>(inherit: true).FirstOrDefault();
        if (memberData is not null)
        {
            var owner = method.DeclaringType ?? throw new InvalidOperationException($"no declaring type for {method.Name}");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            object? rows = owner.GetProperty(memberData.MemberName, flags)?.GetValue(null)
                ?? owner.GetField(memberData.MemberName, flags)?.GetValue(null)
                ?? owner.GetMethod(memberData.MemberName, flags)?.Invoke(null, memberData.Parameters ?? []);
            return rows is IEnumerable<object[]> enumerable
                ? enumerable.Count()
                : throw new InvalidOperationException(
                    $"{owner.Name}.{memberData.MemberName} resolved to {rows?.GetType().Name ?? "null"}, not "
                    + "IEnumerable<object[]>: this case counter cannot measure it, and guessing would understate AC-13");
        }

        if (method.GetCustomAttributes<DataAttribute>(inherit: true).Any(a => a is not InlineDataAttribute and not MemberDataAttribute))
        {
            throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} uses a data source this counter does not enumerate. "
                + "AC-13's case count must be measured, not assumed -- extend CaseCount rather than letting it silently read 1.");
        }

        return 1; // a plain [Fact]
    }

    [Fact]
    public void Every_M3_test_named_by_AC_13_still_exists_and_runs_the_number_of_cases_recorded()
    {
        var assembly = typeof(ApplicationsQueryTests).Assembly;
        var actual = M3TestMethods(assembly)
            .GroupBy(m => m.DeclaringType!.Name)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var expected = M3Manifest.GroupBy(e => e.Class)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Method).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        // Both directions matter and they mean different things. Missing = an assertion was deleted or renamed to get green.
        // Unexpected = someone extended an M3 class, which is allowed but must be a visible decision in the diff.
        var missing = expected.SelectMany(kv => kv.Value.Where(m => !actual.TryGetValue(kv.Key, out var a) || !a.Contains(m)))
            .Select(m => m).ToList();
        var vanishedClasses = expected.Keys.Where(k => !actual.ContainsKey(k)).ToList();
        Assert.Empty(vanishedClasses);
        Assert.True(missing.Count == 0,
            $"AC-13 regression net: {missing.Count} M3 test method(s) are gone from the assembly: {string.Join(", ", missing)}");

        foreach (var (cls, methods) in actual)
        {
            var extra = methods.Except(expected[cls], StringComparer.Ordinal).ToList();
            Assert.True(extra.Count == 0,
                $"{cls} carries {extra.Count} test method(s) not in AC-13's manifest: {string.Join(", ", extra)}");
        }

        Assert.Equal(M3Manifest.Length, M3TestMethods(assembly).Count());
    }

    [Fact]
    public void The_M3_case_count_AC_13_promises_is_still_the_number_of_executable_assertions()
    {
        var assembly = typeof(ApplicationsQueryTests).Assembly;
        var perClass = M3TestMethods(assembly)
            .GroupBy(m => m.DeclaringType!.Name)
            .ToDictionary(g => g.Key, g => g.Sum(CaseCount), StringComparer.Ordinal);
        var total = perClass.Values.Sum();

        // AC-13's headline number, made executable. If this ever reads 64, the diff that changed it is the conversation.
        Assert.True(total == Ac13ExpectedCases,
            $"AC-13 counts {Ac13ExpectedCases} M3 assertions; the assembly now yields {total}. Per class: "
            + string.Join(", ", perClass.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

        // The count that would silently move most is ValidationContractTests: one method, 36 cases, more than half of M3.
        Assert.Equal(36, perClass["ValidationContractTests"]);
    }

    [Fact]
    public void Nothing_in_the_API_test_assembly_is_skipped_or_disabled()
    {
        // AC-13's second half, as a standing property rather than a one-time observation. "Skipped: 0" in a run summary is
        // an outcome; `Skip = "..."` on an attribute is how M4 would quietly earn it, and a skipped test is indistinguishable
        // from a passing one in a log that only reads the failure count.
        var assembly = typeof(ApplicationsQueryTests).Assembly;
        var offenders = assembly.DefinedTypes
            .Where(t => t.Namespace == "JobTracker.Api.Tests" && t.IsClass && !t.IsAbstract)
            .SelectMany(t => t.DeclaredMethods
                .Select(m => (Type: t, Method: m, Attribute: m.GetCustomAttribute<FactAttribute>(inherit: true))))
            .Where(x => x.Attribute is not null && !string.IsNullOrEmpty(x.Attribute.Skip))
            .Select(x => $"{x.Type.Name}.{x.Method.Name} -> \"{x.Attribute!.Skip}\"")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} test(s) carry a Skip reason, so a green run no longer means the behaviour was checked: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public async Task Auth_is_actually_on_the_path_the_M3_tests_ride_rather_than_bypassed_by_the_harness()
    {
        // Without this, AC-13 could be satisfied vacuously: if the harness ever injected a session row by SQL, or the gate
        // stopped covering the data routes, M3's 65 assertions would keep passing on an unauthenticated app and "passes under
        // auth" would mean nothing. So: the same routes, with and without the fixture's login-derived cookie.
        using var bare = new ApplicationsApiFactory(fixture.ConnectionString).CreateClient();

        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "/api/applications"),
            new HttpRequestMessage(HttpMethod.Post, "/api/applications"),
            new HttpRequestMessage(HttpMethod.Put, "/api/applications/" + Guid.NewGuid()),
            new HttpRequestMessage(HttpMethod.Delete, "/api/applications/" + Guid.NewGuid()),
        })
        {
            // await, not Send/Result: TestServer's ClientHandler throws NotSupportedException on the synchronous path
            // ("risk of threadpool exhaustion when running multiple tests in parallel"), which is the framework refusing a
            // habit that would deadlock a suite under xUnit's parallel runner. First draft of this test failed for exactly
            // that reason, and it is a better teacher than the passing version would have been.
            using var anonymous = await bare.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Contains("unauthorized", await anonymous.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using var authenticated = await fixture.Http.GetAsync("/api/applications", HttpCompletionOption.ResponseContentRead);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }
}
