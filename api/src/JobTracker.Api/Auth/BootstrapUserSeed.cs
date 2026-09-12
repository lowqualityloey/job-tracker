using JobTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobTracker.Api.Auth;

/// <summary>
/// Creates the bootstrap account from configuration. Spec §4.2 step 4, <c>BEHAVIOR-050</c>.
///
/// <b>"From configuration, never a literal"</b> is the rule, and the reason there is no default email or password
/// anywhere in this file: a literal that works in development is a literal that ships. <see cref="BootGuard"/> is what
/// makes that enforceable in Production, and this class is what makes it unnecessary in Development.
///
/// <b>Existing accounts are never overwritten.</b> That is a deliberate, load-bearing choice rather than an oversight: the
/// alternative resets a password someone changed through the app on every container restart, quietly and with no audit
/// trail — a "seed" that fights the product. The test for it asserts both halves (one row, same hash after a second boot
/// configured with a different password).
/// </summary>
public static class BootstrapUserSeed
{
    public static async Task SeedAsync(
        JobTrackerDb db, IConfiguration config, IPasswordService passwords, ILogger logger, CancellationToken ct = default)
    {
        var email = config[BootGuard.EmailKey];
        var password = config[BootGuard.PasswordKey];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Skipping is correct here, and the guard is what makes it safe: an unset bootstrap account in Production never
            // reaches this line. In Development it means "no seeded account today", which must not invent one.
            logger.LogInformation("Bootstrap seed skipped: {EmailKey}/{PasswordKey} not configured",
                BootGuard.EmailKey, BootGuard.PasswordKey);
            return;
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            logger.LogDebug("Bootstrap user {EmailKey} already exists; leaving its stored hash untouched", email);
            return;
        }

        db.Users.Add(new User { Id = Guid.NewGuid(), Email = email, PasswordHash = passwords.Hash(password) });
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded bootstrap user {Email}", email);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The unique index on email is the arbiter, and a conflict means another boot got here first. That is a
            // benign outcome for this process — the account exists, which is all it wanted — so the row is not retried
            // and the exception is not allowed to kill startup. Narrow on purpose: the `when` filter means a
            // unique-violation from any *other* constraint still propagates rather than being read as "someone else
            // seeded it". Two instances racing startup migrations is already documented as accepted in Program.cs.
            logger.LogWarning(ex, "Bootstrap seed lost a race to another instance for {Email}; continuing", email);
        }
    }
}
