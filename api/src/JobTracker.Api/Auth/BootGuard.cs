namespace JobTracker.Api.Auth;

/// <summary>
/// The startup rule from spec §4.2 step 4 / <c>BEHAVIOR-050</c>: <b>a Production deployment must not be able to start with
/// default bootstrap credentials.</b>
///
/// Why a guard rather than a deployment-time check: the seeded account is created from configuration, so the failure mode
/// is not "someone picks a weak password" — it is <b>nobody sets one</b>. An unset key, a secret-manager entry that landed
/// after the image was built, a copy-pasted <c>appsettings</c> from a tutorial. Every one of those produces a deployment
/// that looks healthy from the outside and either has no way in or an obvious way in for everyone.
///
/// <b>The environment test fails closed.</b> <c>IsProduction()</c> is true when <c>ASPNETCORE_ENVIRONMENT</c> is unset, so
/// an operator who deploys without thinking about environment names gets the strict branch. <c>Production</c> is not a
/// setting you have to remember; it is the default you have to escape.
/// </summary>
public static class BootGuard
{
    /// <summary>The only password value that is safe in a git-tracked file and therefore must never reach a real deploy.</summary>
    public const string PlaceholderSentinel = "change-me-before-deploy";

    public const string EmailKey = "Auth:Bootstrap:Email";
    public const string PasswordKey = "Auth:Bootstrap:Password";

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> instead of logging, because logging is a message an orchestrator
    /// reads as "started successfully" and retries forever.
    ///
    /// Messages name the <b>key</b>, never the value: startup exceptions are captured into container logs, crash dumps and
    /// orchestrator output that outlive the process and are shared far more widely than a database row. <c>-050</c>
    /// asserts both halves — that an operator can act on the message, and that it leaks nothing.
    /// </summary>
    public static void EnsureSafeToStart(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsProduction())
        {
            return;
        }

        var email = config[EmailKey];
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                $"Refusing to start in Production: {EmailKey} is not set. The bootstrap account is created from " +
                "configuration and there is no built-in default to fall back to.");
        }

        var password = config[PasswordKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Refusing to start in Production: {PasswordKey} is not set.");
        }

        if (password == PlaceholderSentinel)
        {
            throw new InvalidOperationException(
                $"Refusing to start in Production: {PasswordKey} is still the placeholder value declared in " +
                $"{nameof(BootGuard)}. Set a real credential, or the seeded account is a published account.");
        }
    }
}
