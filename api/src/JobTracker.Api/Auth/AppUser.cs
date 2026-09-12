namespace JobTracker.Api.Auth;

/// <summary>
/// The user whose credentials a password is attached to.
///
/// Empty on purpose, and it will stay empty for a while. <c>PasswordHasher&lt;TUser&gt;</c> is generic over a user type
/// because ASP.NET Identity uses it to decide whether a stored hash needs rehashing (it can read a per-user security
/// stamp); our implementation never consults it. It exists here so that <see cref="PasswordService"/> can name the type
/// it hashes for, which keeps the door open to Identity's hasher without adopting Identity's stores — the distinction
/// <c>DECISION-m4-auth-002</c> narrowed and <c>003</c> reversed to reach.
///
/// Adding columns to this class is a schema decision, not an implementation detail: the test fixture and the migration
/// both have to agree with it, and the grill's Q13 finding (a fixture list and a database <c>CHECK</c> constraint that
/// nothing asserts are the same list) is the cautionary tale.
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
