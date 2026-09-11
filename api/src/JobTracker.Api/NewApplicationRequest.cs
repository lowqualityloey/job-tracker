namespace JobTracker.Api;

/// <summary>
/// The write contract of §4.3 — the fields a user supplies, which is exactly <c>ApplicationInput</c> plus the
/// client-minted <c>id</c> (DECISION-m3-backend-api-007). Record and not class: it is a boundary shape with no
/// behaviour, and init-only members mean a payload missing company_name fails during binding rather than as a
/// null-ref somewhere downstream.
/// </summary>
/// <param name="AppliedAt">
/// <c>string?</c> and not <c>DateOnly?</c>, deliberately: a mistyped date must come back as <c>400 validation</c>
/// with a pointer at <c>#/appliedAt</c>. Bound straight to <c>DateOnly?</c>, System.Text.Json fails first, and a
/// binding failure answers with the framework's envelope — right status, no <c>code</c> — which the client's
/// fail-closed rule reads as <c>corrupt-data</c> about a typo in a form field. The parse therefore lives in
/// <see cref="ApplicationValidation"/>, where it is a finding rather than a transport accident. Found by the
/// fixture row <c>applied-at-empty-string</c>.
/// </param>
public sealed record NewApplicationRequest(
    Guid Id,
    string CompanyName,
    string JobTitle,
    string? Location,
    string Status,
    string? AppliedAt,
    string? Notes);
