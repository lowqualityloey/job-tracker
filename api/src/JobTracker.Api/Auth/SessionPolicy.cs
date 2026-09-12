namespace JobTracker.Api.Auth;

/// <summary>
/// The whole lifetime of a session, in one file. BEHAVIOR-066 / AC-17.
///
/// Before this row the numbers were split across two files that had to agree by accident: <c>AuthCatalog</c> wrote
/// <c>expires_at = UtcNow + 30 min</c> from a private const it called "provisional", and <c>SessionGate</c> read the column
/// without knowing where the window came from. A change to one and not the other is a session that either never expires or
/// expires on the request after it is minted, and nothing in the type system can see it.
///
/// ## Two lifetimes, because they answer different attacks
///
/// <see cref="IdleWindow"/> answers "this laptop went to sleep on a train": it is measured from the last use, so activity
/// slides it. <see cref="HardCap"/> answers "the cookie was copied": it is measured from the login, and NOTHING extends it —
/// that is the entire point, because a sliding-only design gives a stolen cookie immortality as long as the thief keeps
/// clicking. The cap is why an <c>expires_at</c> alone cannot express session lifetime, and why the gate reads both.
///
/// ## The prune and the audit trail
///
/// AC-17 asks for expired and revoked rows to be removed so <c>sessions</c> cannot grow without bound. `-052` revokes rather
/// than deletes for a reason recorded in its own comment: so "when did this session end, and was that logout, expiry, or a
/// second login?" stays answerable. <see cref="RevokedRetention"/> is the resolution — dead rows stay readable for a month
/// and then go. Both sides of that boundary are asserted (`SessionExpiryTests`), because a retention window is only real if
/// something enforces each end of it.
/// </summary>
public static class SessionPolicy
{
    /// <summary>How long a session survives with no activity. Measured from last use, which is what makes it idle.</summary>
    public static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(30);

    /// <summary>How long a session survives at all, measured from the login that minted it. Activity does not extend this.</summary>
    public static readonly TimeSpan HardCap = TimeSpan.FromHours(12);

    /// <summary>
    /// Only slide when less than half the idle window is left. Without a threshold, every authenticated request writes a
    /// row in <c>sessions</c>: a read-heavy screen becomes a write-amplification machine, and AC-10's index work exists to
    /// keep reads cheap. With one, a session in active use costs one extra statement per half-window — bounded, and invisible
    /// to the user either way.
    /// </summary>
    public static readonly TimeSpan SlideThreshold = TimeSpan.FromMinutes(15);

    /// <summary>How long a revoked row stays in the table for the audit question -052 preserved.</summary>
    public static readonly TimeSpan RevokedRetention = TimeSpan.FromDays(30);
}
