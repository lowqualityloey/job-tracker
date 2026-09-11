using System.Globalization;

namespace JobTracker.Api;

/// <summary>
/// One field the server found wrong, in the shape §4.3's envelope needs: <c>Field</c> becomes the JSON pointer
/// <c>#/&lt;field&gt;</c> and <c>Message</c> becomes the human line beside it.
/// </summary>
/// <param name="Field">The camelCase member name as it appears on the wire, not the snake_case column.</param>
internal sealed record FieldError(string Field, string Message);

/// <summary>
/// The record after validation: trimmed, parsed, and safe to store.
///
/// A separate type rather than mutation of <see cref="NewApplicationRequest"/> because the request object is what
/// the client sent and the values below are what the server will keep. Collapsing the two is how a validator ends
/// quietly rewriting input in place, so that nothing in the codebase can any longer tell whether a value was
/// trimmed by the user or by a rule.
/// </summary>
internal sealed record ValidatedApplication(
    Guid Id,
    string CompanyName,
    string JobTitle,
    string? Location,
    string Status,
    DateOnly? AppliedAt,
    string? Notes);

/// <summary>
/// The server's opinion of a record, which is not permitted to be an opinion.
///
/// <c>api/tests/fixtures/validation-cases.json</c> is the authority (grill F-1, AC-12) and
/// <c>src/domain/validationFixtureContract.test.ts</c> feeds the same rows to the client's validator, so this class
/// and the TypeScript one are siblings with a shared parent rather than rivals who happen to agree. Changing a rule
/// here without a fixture row will not fail a test — which is the honest way to describe a risk that the sharing
/// narrows but cannot remove; what the sharing does remove is *silent* disagreement.
///
/// Three deliberate asymmetries against the client, each a fixture row with a reason attached:
///   - <c>location</c> is optional here and required there (§4.3 says <c>location?</c>, the client's model says
///     <c>string</c>);
///   - <c>status</c> is checked here and nowhere in the client, whose guard is a TS union erased at build time;
///   - <c>notes</c> has no ceiling on either side, which is an open owner question and not an oversight — the
///     10 kB row flips for the server alone if that changes.
/// </summary>
internal static class ApplicationValidation
{
    /// <summary>
    /// §4.1's five values, in the order the <c>CHECK</c> constraint declares them. The rejection message is built
    /// from this list, so the list a user reads, the list the validator tests and the list the database enforces
    /// (BEHAVIOR-…-032) have one source — which is what guard G-6 asks for, enforced by construction rather than by
    /// a comment saying "keep these in sync".
    /// </summary>
    public static readonly string[] Statuses = ["Saved", "Applied", "Interview", "Rejected", "Offer"];

    /// <summary>
    /// <c>MAX_TEXT_LENGTH</c> in <c>src/domain/validation.ts</c>. Duplicated as a literal on purpose: a shared
    /// constant across the two languages would need a code generator or a config service, and AC-12's mechanism is
    /// a fixture both suites read at test time. The fixture's 120/121 rows are what keeps this number honest.
    /// </summary>
    public const int MaxTextLength = 120;

    /// <summary>
    /// Trims the character set a browser calls blank, which is not quite the set .NET calls blank.
    ///
    /// `char.IsWhiteSpace` covers everything ECMAScript's `trim()` does **except U+FEFF** — the byte-order mark,
    /// category Cf rather than Zs, and the one code point where the two definitions diverge. Measured by
    /// `BEHAVIOR-m3-backend-api-041`: a company name of nothing but a BOM was stored by the API (`Created`) while the
    /// client's validator called it empty, so the record appeared in the list with no visible name and then refused to
    /// save any edit the user made to it.
    ///
    /// The direction of the fix is deliberate: the server adopts the **wider** trim, so it can only ever become
    /// stricter, never newly-permissive. Any value the client accepts, the server accepts — an inversion the other way
    /// would mean a form that validates cleanly and then gets a 400 it cannot display.
    ///
    /// Three trims, not one, because the character can sit on either side of ordinary whitespace: `"\uFEFF x"` and
    /// `" x\uFEFF"` both have to reach `"x"`, and `Trim(char[])` replaces the default set rather than extending it.
    /// </summary>
    private static string? TrimInvisible(string? value)
    {
        return value?.Trim().Trim('\uFEFF').Trim();
    }

    public static (List<FieldError> Errors, ValidatedApplication? Value) Validate(NewApplicationRequest request)
    {
        var errors = new List<FieldError>();

        var companyName = TrimInvisible(request.CompanyName) ?? string.Empty;
        var jobTitle = TrimInvisible(request.JobTitle) ?? string.Empty;
        var location = TrimInvisible(request.Location);
        var notes = TrimInvisible(request.Notes);

        RequiredText(errors, "companyName", "Company name", companyName);
        RequiredText(errors, "jobTitle", "Job title", jobTitle);

        // No RequiredText call for location: the wire contract makes it optional even though the client's own model
        // does not, and `location-empty` in the fixture is the row that says so. Bounds still apply when present —
        // optionality and length are different rules, and `location-over-max` keeps them from being conflated.
        if (location is { Length: > MaxTextLength })
        {
            errors.Add(new FieldError("location", $"Location must be {MaxTextLength} characters or fewer."));
        }

        if (!Statuses.Contains(request.Status, StringComparer.Ordinal))
        {
            errors.Add(new FieldError("status", $"Status must be one of: {string.Join(", ", Statuses)}."));
        }

        DateOnly? appliedAt = null;
        if (request.AppliedAt is { } raw)
        {
            // TryParseExact rather than TryParse: "2026-03-07" must be refused, and the invariant culture is what
            // stops this host's locale (or a runner in another one) deciding that a date is legal here and not
            // there. Empty string fails the parse, which is the client's rule too — a cleared form field is not a
            // day, and `applied-at-empty-string` asserts both sides agree.
            if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                errors.Add(new FieldError("appliedAt",
                    "Date applied must be a real date in YYYY-MM-DD form, or left empty."));
            }
            else
            {
                appliedAt = parsed;
            }
        }

        // An exact calendar day, not merely a well-shaped string: DateOnly's parse already refuses 2026-02-30 and
        // 2026-13-01, which is the same conclusion the client's Date.UTC round-trip reaches by different machinery.
        // Two implementations agreeing is the point; neither is allowed to be the only check.
        return errors.Count > 0
            ? (errors, null)
            : (errors, new ValidatedApplication(request.Id, companyName, jobTitle, location, request.Status, appliedAt, notes));
    }

    private static void RequiredText(List<FieldError> errors, string field, string label, string trimmed)
    {
        // if/else-if and not two independent tests: a value that is empty is not also "too long", and reporting
        // both would be the kind of confidently wrong second message that makes a user retype a field twice.
        if (trimmed.Length == 0)
        {
            errors.Add(new FieldError(field, $"{label} is required."));
        }
        else if (trimmed.Length > MaxTextLength)
        {
            errors.Add(new FieldError(field, $"{label} must be {MaxTextLength} characters or fewer."));
        }
    }
}
