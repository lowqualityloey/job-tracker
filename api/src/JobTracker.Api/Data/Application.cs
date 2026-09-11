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

    public required string CompanyName { get; set; }
    public required string JobTitle { get; set; }

    /// <summary>
    /// Nullable in §4.1 while the client models `location: string` as required-but-possibly-empty. The mismatch is
    /// real and is Slice 3's to close in the HTTP adapter (`location ?? ''`), not to hide here by making the column
    /// NOT NULL — an existing row with no location must still be representable.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Plain string, not an enum: the five values are enforced by the table's CHECK constraint, and mapping a CLR
    /// enum would add a converter whose only job is to translate a database the API already trusts into a type the
    /// compiler cannot check anyway. Out-of-range values arrive from Slice 2's validator, which is tested for it
    /// (BEHAVIOR-…-032), and from raw SQL, which the CHECK rejects (AC-4).
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// <see cref="DateOnly"/> and not <see cref="DateTime"/>, because the column is `date`: the question is which
    /// day, and a DateTime invites the server's timezone to answer it. 027 asserts the wire form is exactly
    /// `yyyy-MM-dd`, which is what src/types/application.ts already carries.
    /// </summary>
    public DateOnly? AppliedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>timestamptz, UTC by Npgsql's rule; never rewritten after create (the client sorts on it).</summary>
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
