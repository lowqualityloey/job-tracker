namespace JobTracker.Api.Auth;

/// <summary>
/// A real PBKDF2 envelope that authenticates nobody, so that the absent-user branch of login can do the <b>same amount of
/// work</b> as the present-user branch (<c>BEHAVIOR-053</c>, spec §2.4, AC-3).
///
/// ## Why it is generated at runtime instead of being a constant in this file
///
/// Because <c>-048</c> made the iteration count a tunable security parameter (<see cref="PasswordService.IterationCount"/>,
/// currently 350,000). A pasted base64 envelope freezes whatever count was current the day someone generated it — and when
/// that constant is next raised, the absent path would verify at the <i>old</i> cost while real accounts verify at the new
/// one. **The fix would then re-create, in its own implementation, the exact oracle it exists to close**, and it would do so
/// silently: the response is still a byte-identical 401 and every message-level test still passes. Deriving the envelope
/// through <see cref="PasswordService"/> makes "same declared cost" a structural property rather than a number two files
/// have to agree on — the same reasoning that made <c>-049</c>'s guard a ratio.
///
/// ## Why lazy, and what that costs
///
/// Generating it at startup would put ~300 ms of PBKDF2 on every process boot, including the ones that never see a login
/// attempt, and Testcontainers hosts boot per test collection. <see cref="Lazy{T}"/> (default
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>, so exactly one thread pays and the rest wait) moves that to
/// the first failed login with an unknown email. One ~300 ms hiccup, once per process, on the path an attacker is by
/// definition already on — versus a first request that is then <i>slower</i> than a wrong-password response, which is an
/// oracle pointing the other way. Stated rather than discovered later.
///
/// ## What this constant is not
///
/// <b>It is not a secret and not a credential.</b> Knowing <see cref="Source"/> buys nothing: it hashes into an envelope
/// that belongs to no user row, so presenting it authenticates nobody. It must nonetheless never be treated as a valid
/// stored hash for a real account — which is why it lives here, next to the one call site, rather than in configuration.
/// </summary>
public static class DummyCredential
{
    /// <summary>The material the dummy envelope is derived from. Public for tests that need to reproduce the envelope's
    /// declared cost; nothing reads it to make an authentication decision.</summary>
    public const string Source = "dummy-credential-for-cost-equalisation-4d1";

    private static readonly Lazy<string> _envelope =
        new(() => new PasswordService().Hash(Source));

    /// <summary>A stored-hash-shaped string that <see cref="IPasswordService.Verify"/> will do full work on and reject.</summary>
    public static string Envelope => _envelope.Value;
}
