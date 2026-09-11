namespace JobTracker.Api;

/// <summary>
/// The API's error responses, in one place — the extraction BEHAVIOR-…-034's commit promised: the second
/// problem-document factory was the trigger to stop writing them inline.
///
/// RFC 9457 with a <c>code</c> extension member (DECISION-m3-backend-api-004), because the HTTP status is not the
/// contract: <c>409</c> arrives from two different causes (a duplicate id from 034, a stale <c>If-Match</c> from
/// 033/044) and the client's adapter must map both to <c>conflict</c> while a framework-generated 409 — an
/// unfilled template, a CSRF rejection — must not be read as one of them. <c>code</c> is what makes the client's
/// "fail closed on an unknown code" rule usable: an envelope without a discriminator is worse than no envelope,
/// because it looks machine-readable.
/// </summary>
internal static class Problems
{
    /// <summary>The 404 the client's adapter can read; see 028's Red commit for what the framework sends instead.</summary>
    public static IResult NotFound(string id) => Results.Problem(
        title: "No application record exists with that id.",
        statusCode: StatusCodes.Status404NotFound,
        type: "https://job-tracker.local/probs/not-found",
        instance: $"/api/applications/{id}",
        extensions: new Dictionary<string, object?> { ["code"] = "not-found" });

    /// <summary>Same id already stored (034), or a write based on a version that is no longer current (033/044).</summary>
    public static IResult Conflict(Guid id) => Results.Problem(
        title: "Another request already wrote that record, or this one is based on a stale version.",
        statusCode: StatusCodes.Status409Conflict,
        type: "https://job-tracker.local/probs/conflict",
        instance: $"/api/applications/{id}",
        extensions: new Dictionary<string, object?> { ["code"] = "conflict" });

    /// <summary>
    /// §4.3's validation envelope: RFC 9457 members plus <c>code</c> and an <c>errors[]</c> list of
    /// <c>{ pointer, detail }</c>, where the pointer names the *wire* member, not the column.
    ///
    /// A list rather than a single reason because a form shows every problem at once — the client's validator
    /// already returns all of them, and an API that answered only the first would make the user retype a field,
    /// resubmit, and be told about the second. Order comes from the validator, which the fixture pins by
    /// comparing lists rather than sets.
    /// </summary>
    public static IResult Validation(IReadOnlyList<FieldError> errors, string instance) => Results.Problem(
        title: "The application record is not valid.",
        detail: errors.Count == 1 ? "One field failed validation." : $"{errors.Count} fields failed validation.",
        statusCode: StatusCodes.Status400BadRequest,
        type: "https://job-tracker.local/probs/validation",
        instance: instance,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "validation",
            ["errors"] = errors.Select(error => new { pointer = $"#/{error.Field}", detail = error.Message }).ToList(),
        });
}
