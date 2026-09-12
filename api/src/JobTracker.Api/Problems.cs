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
    /// <summary>
    /// The validation discriminator and its type URI, shared by the factory below and by the exception mapper in
    /// <c>Program.cs</c>. BEHAVIOR-069 is the reason: a body the binder could not read must arrive as the SAME code the
    /// validator emits for a field the user mistyped, because the client's table (-060) maps one code to one behaviour and
    /// has no interest in which of the two server paths produced it. Two literals in two files is how those drift apart.
    /// </summary>
    internal const string ValidationCode = "validation";

    /// <summary>See <see cref="ValidationCode"/> for why this is public to the assembly rather than inline.</summary>
    internal const string ValidationTypeUri = "https://job-tracker.local/probs/validation";

    /// <summary>The name of the discriminator member DECISION-m3-backend-api-004 put on every problem document.</summary>
    internal const string CodeExtensionKey = "code";
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
    /// <summary>
    /// The problem a request body that will not deserialize produces. BEHAVIOR-069.
    ///
    /// Deliberately separate from <see cref="Validation"/>: that one reports *what the user got wrong* across a whole form
    /// and takes its pointers from the validator's field list; this one reports *what the binder could not parse*, and knows
    /// only the single JSON path the reader stopped on. They share a <c>code</c> because the client's answer is the same --
    /// show it on the field, do not retry, do not sign the user out -- and the -060 table maps one code to one behaviour
    /// with no interest in which server path produced it.
    /// </summary>
    public static Microsoft.AspNetCore.Mvc.ProblemDetails InvalidRequestBody(
        BadHttpRequestException badRequest, Microsoft.AspNetCore.Http.PathString instance)
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Title = "The request body could not be read.",
            Detail = "The JSON in the request body does not match the shape this endpoint accepts.",
            Status = StatusCodes.Status400BadRequest,
            Type = ValidationTypeUri,
            Instance = instance.HasValue ? instance.Value : null,
        };

        problem.Extensions[CodeExtensionKey] = ValidationCode;

        // The reader knows where it stopped: JsonException.Path is e.g. "$.companyName", which becomes the same
        // { pointer, detail } wire shape the validator emits (#/companyName) so a form can put the message on the field the
        // user mistyped. A truncated document has no path at all, and the code must still arrive -- asserted separately.
        if (badRequest.InnerException is System.Text.Json.JsonException { Path: { Length: > 0 } jsonPath })
        {
            problem.Extensions["errors"] = new[]
            {
                new
                {
                    pointer = PointerFromJsonPath(jsonPath),
                    detail = "This member has the wrong JSON type.",
                },
            };
        }

        return problem;
    }

    /// <summary>
    /// A JSON pointer in the shape <see cref="Validation"/> uses, from whatever <c>JsonException.Path</c> handed over.
    ///
    /// Measured, not assumed: the documented form is <c>$.companyName</c>, and the first version of this line stripped the
    /// <c>$</c> and prefixed a <c>#</c> -- which produced <c>#.jobTitle</c> on the wire, because a member failure on the
    /// ROOT object comes back as <c>.jobTitle</c> with no <c>$</c> at all. Parsing segments from either form makes the
    /// output agree with the validator's pointers, which is what the client's <c>toFieldErrors</c> matches on; a pointer
    /// the client cannot turn into a field name is a message that goes nowhere.
    /// </summary>
    internal static string PointerFromJsonPath(string path)
    {
        var segments = path
            .Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Where(static segment => segment != "$")
            .ToArray();

        return segments.Length == 0 ? "#" : "#/" + string.Join("/", segments);
    }

    public static IResult Validation(IReadOnlyList<FieldError> errors, string instance,
        string title = "The application record is not valid.") => Results.Problem(
        title: title,
        detail: errors.Count == 1 ? "One field failed validation." : $"{errors.Count} fields failed validation.",
        statusCode: StatusCodes.Status400BadRequest,
        type: ValidationTypeUri,
        instance: instance,
        extensions: new Dictionary<string, object?>
        {
                [CodeExtensionKey] = ValidationCode,
            ["errors"] = errors.Select(error => new { pointer = $"#/{error.Field}", detail = error.Message }).ToList(),
        });

    /// <summary>
    /// The <c>unauthorized</c> code of DECISION-m4-auth-005 — the ninth RepositoryError variant, added because a 401 must
    /// be distinguishable from every other 4xx the client already maps, and because "log in again" is a different user
    /// action from "your input was wrong" or "that record isn't yours".
    ///
    /// <b>Deliberately vague, and that is the point.</b> One title, no field names, no hint whether the email or the
    /// password failed: <c>BEHAVIOR-053</c> asserts wrong-email and wrong-password are byte-identical. A message that said
    /// "no such email" would be a user-experience courtesy and an enumeration oracle at the same time, and DECISION-004
    /// already settled which way this app leans (404 rather than 403 for records that aren't yours).
    /// </summary>
    public static IResult Unauthorized(string instance) => Results.Problem(
        title: "Those credentials were not accepted, or the session is no longer valid.",
        statusCode: StatusCodes.Status401Unauthorized,
        type: "https://job-tracker.local/probs/unauthorized",
        instance: instance,
        extensions: new Dictionary<string, object?> { ["code"] = "unauthorized" });

    /// <summary>
    /// The <c>antiforgery</c> code of DECISION-m4-auth-006 / BEHAVIOR-m4-auth-063. A fourth 4xx the client must be able to
    /// tell apart, and the distinction is not cosmetic: <c>unauthorized</c> means "log in again", which is the right advice
    /// for an expired session and useless here — a user with a valid session who is told to log in again will do it and
    /// arrive back at the same failure. <c>validation</c> would be worse, because it would put a field error on a form the
    /// user never mistyped.
    ///
    /// Vague for the same reason <see cref="Unauthorized"/> is, and by <c>BEHAVIOR-053</c>'s logic: this response says the
    /// request was refused, not *which* token was expected, and naming the mechanism in the body hands a page the answer
    /// to "does the server check a header at all".
    /// </summary>
    public static IResult Antiforgery(string instance) => Results.Problem(
        title: "This request was refused because it could not be confirmed as coming from the application.",
        statusCode: StatusCodes.Status403Forbidden,
        type: "https://job-tracker.local/probs/antiforgery",
        instance: instance,
        extensions: new Dictionary<string, object?> { ["code"] = "antiforgery" });

}
