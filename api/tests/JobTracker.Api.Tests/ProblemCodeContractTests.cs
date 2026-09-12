using System.Reflection;
using System.Text.Json;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-060 (server half) — the API's problem <c>code</c> vocabulary is enumerated **by invoking its own factories**, and
/// must equal <c>contracts/problem-codes.json</c>, the artifact the frontend's <c>PROBLEM_CODE_TABLE</c> is asserted against.
/// Seam Unit (API), companion to <c>src/data/problemCodeContract.test.ts</c>; the row itself is Unit (FE), p0.
///
/// ## Why reflection over the factories rather than a hand-written list
///
/// A second list of the same codes is a second thing to forget, which is how <c>unauthorized</c> reached the client's
/// fallback branch: the server added a code in one file and nobody edited a file in another language. So nothing here
/// restates the vocabulary — <see cref="Emitted"/> walks every static method on <c>JobTracker.Api.Problems</c> that hands back
/// a problem document, calls it with inert arguments, and reads the <c>code</c> extension member off the result. Adding
/// <c>Problems.SessionExpired(…)</c> with a code that is not in the contract therefore fails the suite that ships the code,
/// which is the only moment the omission is cheap to fix.
///
/// Duck-typed rather than typed: <c>Problems</c> is <c>internal</c> and <c>ProblemDetails</c> is a framework type, so both are
/// reached by name and by reflection. That is deliberate — this test is about a boundary, and a test that needs the
/// production type to be public in order to observe it would quietly encourage making it public.
/// </summary>
public sealed class ProblemCodeContractTests
{
    private static readonly Type? ProblemsType = Type.GetType("JobTracker.Api.Problems, JobTracker.Api");

    // Two lists so an empty result is never mistaken for an empty surface: a test that enumerates nothing must say which
    // methods it could not call, because that is the difference between "there are no other codes" and "I could not look".
    private static readonly List<string> Diagnostics = [];
    private static readonly List<string> Unreadable = [];

    private static IReadOnlyList<string> Contract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "problem-codes.json");
        // Copied by the csproj from contracts/, the same mechanism the M3 validation fixture uses: one file, both suites.
        Assert.True(File.Exists(path),
            $"the shared contract was not copied to the output directory (looked for {path}); the csproj Content entry is load-bearing");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("codes").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static object? Coerce(Type parameter)
    {
        var target = Nullable.GetUnderlyingType(parameter) ?? parameter;

        if (parameter.IsGenericType && parameter.GetGenericTypeDefinition().Name.StartsWith("IEnumerable", StringComparison.Ordinal)
            || parameter.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(parameter)
               && target != typeof(string))
        {
            var element = parameter.GenericTypeArguments.Length == 1
                ? parameter.GenericTypeArguments[0]
                : typeof(object);
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
        }

        if (target == typeof(Guid))
        {
            return Guid.Empty;
        }
    ;
        if (target == typeof(string))
        {
            return "probe";
        }
        if (target == typeof(int) || target == typeof(long) || target == typeof(short))
        {
            return 1;
        }
        if (target == typeof(bool))
        {
            return true;
        }
        if (target == typeof(DateTime) || target == typeof(DateTimeOffset))
        {
            return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        try
        {
            return parameter.IsValueType ? Activator.CreateInstance(target) : null;
        }
        catch (MissingMethodException)
        {
            return null;
        }
    }

    private static List<(string Code, int Status, string Method)> Emitted()
    {
        Diagnostics.Clear();
        Unreadable.Clear();
        Assert.NotNull(ProblemsType); // the type moved; this test's premise is a name, and a silent empty set is the worst answer
        var found = new List<(string, int, string)>();

        foreach (var method in ProblemsType!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.GetParameters().Any(p => p.IsOut || p.ParameterType.IsByRef))
            {
                continue;
            }

            object? result;
            try
            {
                result = method.Invoke(null, method.GetParameters().Select(p => Coerce(p.ParameterType)).ToArray());
            }
            catch (Exception exception)
            {
                Diagnostics.Add($".{method.Name}({string.Join(", ", method.GetParameters().Select(x => x.ParameterType.Name))}) -> "
                    + $"{(exception is TargetInvocationException tie ? tie.InnerException?.GetType().Name ?? tie.GetType().Name : exception.GetType().Name)}");
                continue; // not a factory we can call inertly; the assertions below report what was skipped rather than passing on it
            }

            if (result is null)
            {
                continue;
            }

            // `Problems.NotFound(...)` does not hand back a ProblemDetails: it hands back Results.Problem(...), which is a
            // ProblemHttpResult whose document sits on an INTERNAL ProblemDetails property. Found by running this, not by
            // reading Problems.cs -- the first version enumerated nothing and one of its three tests passed on the empty set.
            var document = result.GetType()
                .GetProperty("ProblemDetails", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(result) ?? result;
            // GetValue(document), not GetValue(result): the TargetException this line used to throw was my reflection
            // bug -- reading a property of the inner document off the outer result object.

            var extensions = document.GetType().GetProperty("Extensions")?.GetValue(document) as System.Collections.IDictionary;
            var code = extensions?["code"] as string;
            var status = (int?)document.GetType().GetProperty("Status")?.GetValue(document);

            if (code is not null && status is not null)
            {
                found.Add((code, status.Value, method.Name));
            }
            else
            {
                Unreadable.Add($".{method.Name} returned {result.GetType().Name} with code={code ?? "<none>"} status={status?.ToString() ?? "<none>"}");
            }
        }

        return found;
    }

    [Fact]
    public void The_codes_the_api_can_emit_are_exactly_the_shared_contract()
    {
        var emitted = Emitted();
        Assert.True(emitted.Count > 0,
            $"no problem factory could be invoked, so this test is measuring nothing. Problems type: "
            + $"{ProblemsType?.FullName ?? "<TYPE NOT FOUND>"}; skipped: {(Diagnostics.Count == 0 ? "none" : string.Join("; ", Diagnostics))}; "
            + $"unreadable: {(Unreadable.Count == 0 ? "none" : string.Join("; ", Unreadable))}");

        var distinct = emitted.Select(e => e.Code).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var expected = Contract().OrderBy(c => c, StringComparer.Ordinal).ToList();

        Assert.True(distinct.SequenceEqual(expected),
            $"server codes {string.Join(", ", distinct)} vs contract {string.Join(", ", expected)}. "
            + "Both sides of this drift are cheap to fix and expensive to discover: the frontend table is asserted against "
            + "the same file (src/data/problemCodeContract.test.ts).");
    }

    [Fact]
    public void Every_emitted_code_is_a_stable_lowercase_token()
    {
        // The wire value is what the client switches on, so casing and separators are contract, not style.
        foreach (var (code, _, method) in Emitted())
        {
            Assert.Matches("^[a-z][a-z-]{1,40}$", code);
            Assert.False(code.EndsWith('-'), $"{method} emits a code with a trailing hyphen: {code}");
        }
    }

    [Fact]
    public void Each_code_arrives_with_the_status_the_client_also_reads()
    {
        // The adapter branches on the code, but a code paired with a surprising status is a bug neither suite would see on
        // its own -- and 401 is the one that matters most here, because it is the code that currently renders as corrupt-data
        // while DECISION-m4-auth-005 waits for the owner.
        var pairs = Emitted().Select(e => (e.Code, e.Status)).Distinct().OrderBy(p => p.Code, StringComparer.Ordinal).ToList();

        foreach (var (code, status) in new[] { ("validation", 400), ("not-found", 404), ("conflict", 409), ("unauthorized", 401) })
        {
            Assert.Contains((code, status), pairs);
        }
    }
}
