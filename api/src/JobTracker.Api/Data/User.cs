namespace JobTracker.Api.Data;

/// <summary>
/// The one account M4 ships with. Spec <c>DECISION-m4-auth-002</c>: this is <b>not</b> ASP.NET Identity's <c>ApplicationUser</c> —
/// the Identity *stores* were rejected for pulling recovery, external providers and role tables into a project that has
/// none of those concerns. <c>PasswordHasher</c> was kept, which is why the class here has no security stamp, no
/// concurrency stamp and no lockout fields.
///
/// <c>ASSUMPTION-m4-auth-002</c> is the honest label on this file: single user for M4's lifetime, registration out of scope.
/// A table with one row is the wrong shape for a multi-user app, and the spec says so — the table is built as a table
/// precisely so that assumption can be withdrawn without a rewrite.
/// </summary>
public sealed class User
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
}
