namespace JobTracker.Api.Data;

/// <summary>
/// The persisted application record. Right now it has exactly one property, and that is the whole design
/// statement of Slice 1's first Green: BEHAVIOR-m3-backend-api-026 says "an empty catalog answers 200 with []",
/// which requires a table to be empty of and a key to identify rows by — and nothing else. The company name,
/// job title, status, dates and notes arrive when the behaviour that reads them out loud is written
/// (BEHAVIOR-m3-backend-api-027), so the migration history will show the table growing under test pressure
/// rather than arriving complete from an up-front design.
///
/// This is also why the approved spec's DDL is not simply pasted in: it is the *destination*, and the columns
/// land with the tests that prove them. `applications` keeps its `id uuid PRIMARY KEY` naming exactly as
/// §4.1 specifies, since the wire contract is what the frontend is already written against.
/// </summary>
public sealed class Application
{
    /// <summary>Client-minted v4 UUID (DECISION-m3-backend-api-007), not a server-generated identity.</summary>
    public required Guid Id { get; set; }
}
