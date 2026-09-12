using JobTracker.Api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-048 — the password cost is **chosen**, not inherited.
///
/// <c>PasswordHasher&lt;TUser&gt;</c> ships a default iteration count. Using it means every hash this app stores was made at
/// a number decided by a framework release, and the spec's target was explicit: the count is configured, so raising it is a
/// one-line change with an audit trail rather than an accident of upgrades.
///
/// **Deliberately absent, and the absence is the honest part:** no assertion here pins *which* number is right. A magic
/// constant in a test would be exactly the unmeasured threshold this project has already been punished for twice (an
/// unrun §2.8 latency target; a 5 ms enumeration bound that may have been noise). <c>BEHAVIOR-049</c> measures the
/// <c>Verify</c>/Hash cost band, and **that measurement is what turns the number in -048's Green from a guess into a
/// decision** — with the standing tension recorded: a higher count is a stronger hash and a slower login, and one of the
/// two has to give.
/// </summary>
public sealed class PasswordCostTests
{
    private static readonly IPasswordService Service = new PasswordService();
    private const string Password = "correct horse battery staple";

    [Fact]
    public void Our_hashes_are_not_at_the_framework_default_cost()
    {
        // A hasher built with no options carries the framework's inherited default. Asked to verify one of *our* hashes, it
        // can only answer SuccessRehashNeeded if our stored envelope declares a *different* iteration count — which is the
        // entire property under test, observed through behaviour rather than by reaching into the envelope format.
        //
        // It is a Red today for the plainest possible reason: -047's Green constructed `new PasswordHasher<AppUser>()` with
        // no options at all, so our hashes are at the default and the probe answers Success.
        var frameworkDefault = new PasswordHasher<AppUser>();
        var ourHash = Service.Hash(Password);

        Assert.Equal(
            PasswordVerificationResult.SuccessRehashNeeded,
            frameworkDefault.VerifyHashedPassword(new AppUser(), ourHash, Password));
    }

    [Fact]
    public void A_hash_made_at_a_lower_cost_still_verifies_while_needing_rehash()
    {
        // The decision -047 recorded in prose and could not test: SuccessRehashNeeded counts as verified.
        //
        // It earns its place as a test now because -048 is precisely the change that would otherwise make it a lockout —
        // raising the configured cost means every existing account's envelope suddenly declares a lower count, and an
        // implementation treating that as failure would reject correct passwords on the strength of them being *older*.
        // `Options.Create` rather than the options object: PasswordHasher's constructor takes IOption<PasswordHasherOptions>
        // (CS1503 said so, and a compile error is not a Red — it proves nothing about behaviour, which is exactly the
        // distinction -047's commit message drew when it made its stub throw instead of leaving the type unknown).
        var cheap = new PasswordHasher<AppUser>(
            Options.Create(new PasswordHasherOptions { IterationCount = 1_000 }));
        var cheapHash = cheap.HashPassword(new AppUser(), Password);

        Assert.True(Service.Verify(cheapHash, Password));

        // And the wrong password is still wrong at a mismatched cost: the rehash path must not become a back door where
        // "needs rehash" is answered before the subkey is compared.
        Assert.False(Service.Verify(cheapHash, "not the password"));
    }
}
