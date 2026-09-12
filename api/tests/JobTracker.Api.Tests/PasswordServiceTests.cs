using JobTracker.Api.Auth;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-047 — a password can be hashed and verified, and the result is bound to its own salt.
///
/// Written before the implementation existed (Red), and it stays deliberately at the seam's public contract: nothing here
/// reaches into `PasswordHasher`'s internals or asserts a specific envelope layout. That is `BEHAVIOR-048`'s job — it is
/// the behaviour that asserts the *iteration count is configured rather than inherited*, which is a claim about the
/// format and has to fail today, when the count is Identity's default.
///
/// Why `PasswordHasher` rather than a hand-rolled PBKDF2 envelope: `DECISION-m4-auth-003` was reversed by the grill
/// (docs/reviews/2026-09-12-m4-plan-grill.md, Q2) after `Microsoft.Extensions.Identity.Core.dll` was found in the ASP.NET
/// Core shared framework — compile-proved, not assumed. The dependency argument that justified writing our own was an npm
/// count applied to backend code.
/// </summary>
public sealed class PasswordServiceTests
{
    private static readonly IPasswordService Service = new PasswordService();

    [Fact]
    public void A_password_can_be_verified_against_its_own_hash()
    {
        var stored = Service.Hash("correct horse battery staple");

        Assert.True(Service.Verify(stored, "correct horse battery staple"));
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        var stored = Service.Hash("correct horse battery staple");

        Assert.False(Service.Verify(stored, "wrong horse battery staple"));
    }

    [Fact]
    public void A_tampered_hash_does_not_verify_even_with_the_right_password()
    {
        var stored = Service.Hash("correct horse battery staple");

        // Flip the final character rather than truncating: a truncated base64 string can fail for a *decoding* reason and
        // tell us nothing about the comparison. Changing one character keeps the envelope well-formed and moves only the
        // derived subkey, so a `true` here could only mean the comparison is wrong.
        var last = stored[^1] == 'A' ? 'B' : 'A';
        var tampered = stored[..^1] + last;

        Assert.NotEqual(stored, tampered);
        Assert.False(Service.Verify(tampered, "correct horse battery staple"));
    }

    [Fact]
    public void Two_hashes_of_the_same_password_differ_because_each_gets_its_own_salt()
    {
        // The property that actually matters to a leaked table: with a per-hash salt, one stolen hash tells you nothing
        // about any other account, and two users who chose the same password do not share a hash. An unsalted or
        // fixed-salt implementation passes the round-trip test above and fails this one.
        Assert.NotEqual(Service.Hash("shared-password"), Service.Hash("shared-password"));
    }

    [Fact]
    public void An_empty_password_is_hashed_and_verified_like_any_other()
    {
        // Not a policy statement — M4 has no composition rules (test plan §6 says so). This is the boundary probe that
        // keeps "hash the empty string" from being the one path nobody tried: an implementation that special-cases it,
        // or throws on it, is a login you can never reach.
        var stored = Service.Hash(string.Empty);

        Assert.True(Service.Verify(stored, string.Empty));
        Assert.False(Service.Verify(stored, "x"));
    }
}
