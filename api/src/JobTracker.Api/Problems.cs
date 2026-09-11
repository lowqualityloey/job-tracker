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
}
