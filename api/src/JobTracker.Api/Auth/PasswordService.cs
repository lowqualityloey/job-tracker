using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Auth;

/// <summary>
/// Hashes and verifies passwords via <see cref="PasswordHasher{TUser}"/>. <c>BEHAVIOR-047</c>.
///
/// **Why the framework's hasher and not the hand-rolled PBKDF2 envelope the spec first proposed:**
/// <c>DECISION-m4-auth-003</c> was reversed by the grill
/// ([`docs/reviews/2026-09-12-m4-plan-grill.md`](../../../../docs/reviews/2026-09-12-m4-plan-grill.md), Q2). Its premise —
/// that using Identity would add a runtime dependency — was an **npm** count (3, `src/**`) applied to backend code, and
/// `Microsoft.Extensions.Identity.Core.dll` ships inside the ASP.NET Core shared framework, so this file needs **zero**
/// `PackageReference`s. Compile-proved, not assumed. Argon2id stays deferred with its named trigger (M5 exposure).
///
/// What the framework gives us for free is the part the custom format was reinventing: a per-hash random salt and a
/// self-describing envelope (<c>{version}{PRF}{iterations}{salt}{subkey}</c>) whose iteration count travels with the hash,
/// so a future cost increase can verify old hashes and mark them for rehashing.
/// </summary>
public sealed class PasswordService : IPasswordService
{
    /// <summary>
    /// The iteration count every hash this app writes is made at. <c>BEHAVIOR-048</c>.
    ///
    /// <b>Explicit, and that is the whole behaviour.</b> Inheriting <c>PasswordHasher</c>'s default — <b>100,000, measured
    /// on this machine</b>, not recalled — would mean every stored hash encodes a cost chosen by whichever framework
    /// release was current when the account was created. Naming it in one place makes the next increase a change with an
    /// audit trail; discovering it in release notes does not.
    ///
    /// <b>The number is provisional and says so.</b> Nothing here has measured it. <c>BEHAVIOR-049</c> asserts the
    /// Hash/Verify cost band, and <i>that</i> measurement justifies or moves this constant — an unmeasured threshold is the
    /// species of mistake M3 made twice (§2.8's target unrun for 126 commits; a 5 ms enumeration bound that may have been
    /// noise). The tension is standing: a higher count is a stronger hash and a slower login, and one of the two gives.
    ///
    /// Note that raising this never invalidates an existing hash: <c>PasswordHasher</c> derives from the count declared in
    /// the envelope — the fact that falsified the first draft of <c>-048</c>'s test.
    /// </summary>
    public const int IterationCount = 350_000;

    private readonly PasswordHasher<AppUser> _hasher =
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));

    public string Hash(string password) => _hasher.HashPassword(new AppUser(), password);

    /// <remarks>
    /// <c>SuccessRehashNeeded</c> counts as verified, and this is the one decision in this file that no test in
    /// <c>BEHAVIOR-047</c> proves — so it is named here as an obligation on <c>BEHAVIOR-048</c> rather than left as a
    /// silent judgement call.
    ///
    /// The reasoning is the lockout shape: <c>VerifyHashedPassword</c> returns <c>SuccessRehashNeeded</c> when the stored
    /// envelope is well-formed but predates the current cost settings. Treat that as a failure and <c>-048</c> — whose
    /// whole job is raising the iteration count above Identity's inherited 100,000 default — **silently locks out every
    /// existing account the moment it goes green.** A password that is correct enough to need rehashing is correct.
    ///
    /// `-048` must therefore assert two things, and only the first is guaranteed to fail today: the envelope's iteration
    /// count equals a configured value, **and** a hash made at a lower count verifies `true` while needing rehash.
    /// </remarks>
    public bool Verify(string storedHash, string candidate)
    {
        // Narrow on purpose: FormatException is the base64 decode inside PasswordHasher, and it escapes rather than
        // returning Failed. Swallowing it broadly (catch Exception) would have turned a real bug into a wrong-password
        // answer, which is the one outcome a login path must never produce silently.
        PasswordVerificationResult result;
        try
        {
            result = _hasher.VerifyHashedPassword(new AppUser(), storedHash, candidate);
        }
        catch (FormatException)
        {
            return false;
        }

        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
