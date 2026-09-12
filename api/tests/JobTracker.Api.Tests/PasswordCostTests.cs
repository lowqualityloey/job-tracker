using System.Buffers.Binary;
using JobTracker.Api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-048 — the password cost is **chosen**, not inherited.
///
/// **The first version of this file asserted something that is not true, and the correction is the interesting part.**
/// It asked a default-configured <see cref="PasswordHasher{TUser}"/> to verify one of our hashes and expected
/// <c>SuccessRehashNeeded</c> on a count mismatch. Measured (probe, 2026-09-12): the verifier takes the iteration count
/// *from the envelope* and answers <c>Success</c> whether we declare 100,000 or 350,000 — **a cost difference does not
/// raise the rehash signal in this format.** The observable was invented from a belief about the framework instead of a
/// look at it, which is the exact habit this project's whole integrity ledger is about.
///
/// What is left is the assertion the behaviour actually makes: the count written into the stored envelope is not the
/// framework's inherited default. It costs a decode — coupling the test to Identity's layout — and the version-byte guard
/// below is what buys that coupling back: if the layout changes, this test fails loudly rather than reading iterations
/// out of the salt.
/// </summary>
public sealed class PasswordCostTests
{
    private static readonly IPasswordService Service = new PasswordService();
    private const string Password = "correct horse battery staple";

    /// <summary>The default Identity ships today, **measured** on this machine rather than recalled from a blog post.</summary>
    private const uint FrameworkDefaultIterations = 100_000;

    [Fact]
    public void Our_hashes_do_not_declare_the_framework_default_cost()
    {
        var envelope = System.Convert.FromBase64String(Service.Hash(Password));

        // Guard the offset before trusting it: byte 0 is the format marker (measured = 1 for a current hash). If Identity
        // changes its layout, this assert fires and someone re-reads the format — instead of the test quietly decoding
        // iterations out of the salt and passing on nonsense.
        Assert.Equal(1, envelope[0]);
        var declared = BinaryPrimitives.ReadUInt32BigEndian(envelope.AsSpan(5, 4));

        Assert.NotEqual(FrameworkDefaultIterations, declared);
    }

    [Fact]
    public void A_hash_declaring_a_lower_cost_still_verifies_because_the_count_travels_with_the_envelope()
    {
        // The property -048 actually buys: raising the configured cost does not invalidate a single stored hash, because
        // verification derives from the *declared* count. That is also why the probe in the test above returned Success
        // rather than the rehash signal I expected.
        var cheap = new PasswordHasher<AppUser>(
            Options.Create(new PasswordHasherOptions { IterationCount = 1_000 }));
        var cheapHash = cheap.HashPassword(new AppUser(), Password);

        Assert.True(Service.Verify(cheapHash, Password));

        // And a mismatched cost is not a back door: "older envelope" must not be answered before the subkey is compared.
        Assert.False(Service.Verify(cheapHash, "not the password"));
    }
}
