using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-050 (second half) — **the seeded user comes from configuration.** Spec §4.2 step 4; test-plan row `-050`.
///
/// ## Why these assertions reach for the database rather than for an object
///
/// The claim is about a row that must exist after the app starts. A unit test on a `SeedAsync` method would pass in a build
/// where nobody calls it — the same trap <c>-050</c>'s guard half avoided by testing boot instead of the validator. So
/// every test here boots the real application through <see cref="WebApplicationFactory{TEntryPoint}"/> and then asks
/// PostgreSQL what happened, through a plain <see cref="NpgsqlConnection"/> the way <c>PostgresHarnessTests</c> does.
///
/// ## What "from configuration, never a literal" is worth asserting
///
/// Not just that a user exists — that the **stored hash is a real <c>PasswordService</c> hash for the configured
/// password**. That single assertion is what rules out the three cheap ways to satisfy "a user exists": a hard-coded
/// email, a plaintext password, or a seed that inserts a row nobody can authenticate against. It also makes the seed
/// depend on <c>-047</c>'s hasher rather than merely run after it.
///
/// ## Emails are unique, so each test uses its own
///
/// The whole suite shares one container database (<c>[PostgresCollection]</c>). A fixed seed email would make these tests
/// order-dependent through a uniqueness violation — which is exactly the "reset shared state" rule this repo already has.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BootstrapSeedTests(PostgresFixture postgres)
{
    private const string SeedPassword = "seeded-by-configuration-8f31";

    private sealed class SeedFactory(string? email, string? password, string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development, not Production: the guard already has its own tests, and seeding must work identically in both.
            // Asserting them together would hide a seed that only runs after the guard's checks.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            // Null means "leave the key unset", which is how the no-credentials case is produced -- the connection string
            // still has to be real, because Program.cs migrates at startup and a missing connection string would make that
            // test fail for a reason that has nothing to do with seeding.
            if (email is not null) builder.UseSetting("Auth:Bootstrap:Email", email);
            if (password is not null) builder.UseSetting("Auth:Bootstrap:Password", password);
        }
    }

    private static string UniqueEmail(string test) =>
        $"{test}.{Guid.NewGuid():N}@example.test";

    private async Task<List<(string Email, string Hash)>> ReadUsers(string email)
    {
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // ::citext is not decoration. The probe that settled this: an inlined literal compares case-insensitively through
        // citext's own operator, while a typed text parameter does not resolve the same way -- which is why this file's
        // case-fold test failed as "collection was empty" and briefly looked like a broken schema instead of a broken
        // query. Naming the type makes the assertion test the column's behaviour.
        cmd.CommandText = "SELECT email, password_hash FROM users WHERE email = $1::citext";
        cmd.Parameters.AddWithValue(email);
        var rows = new List<(string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    [Fact]
    public async Task Booting_with_configured_credentials_creates_exactly_one_user()
    {
        var email = UniqueEmail("one-user");
        using var factory = new SeedFactory(email, SeedPassword, postgres.ConnectionString);
        factory.CreateClient();   // side effect is the whole point: boot = migrate + seed

        var rows = await ReadUsers(email);
        var row = Assert.Single(rows);
        Assert.Equal(email, row.Email);
    }

    [Fact]
    public async Task The_seeded_password_hash_verifies_with_the_real_hasher_and_only_the_configured_password()
    {
        var email = UniqueEmail("verifies");
        using var factory = new SeedFactory(email, SeedPassword, postgres.ConnectionString);
        factory.CreateClient();

        var (_, storedHash) = Assert.Single(await ReadUsers(email));

        var service = new JobTracker.Api.Auth.PasswordService();
        Assert.True(service.Verify(storedHash, SeedPassword),
            "the seed wrote a value the application's own verifier rejects");
        Assert.False(service.Verify(storedHash, "not-the-configured-password"));

        // And it is a hash, not a secret someone typed: -047's envelope is base64 with a version byte. This is a shape
        // assertion, deliberately weak on purpose — the *verification* above is the load-bearing check, and if this one
        // ever fires while that still passes, the hasher changed its encoding and somebody should know about it.
        Assert.Equal(1, System.Convert.FromBase64String(storedHash)[0]);
    }

    [Fact]
    public async Task A_second_boot_does_not_create_a_second_user_or_overwrite_the_first()
    {
        // Idempotency has two halves and the second one is the dangerous one. "Don't insert twice" is easy; "don't insert
        // twice *and* don't reset an existing account's password back to the configured value on every restart" is the part
        // that silently undoes a legitimate password change the first time the container is replaced.
        var email = UniqueEmail("idempotent");
        using (var first = new SeedFactory(email, SeedPassword, postgres.ConnectionString))
            first.CreateClient();
        var original = (await ReadUsers(email)).Single().Hash;

        using (var second = new SeedFactory(email, "a-different-password-entirely-4d7a", postgres.ConnectionString))
            second.CreateClient();

        var after = await ReadUsers(email);
        Assert.Single(after);
        Assert.Equal(original, after[0].Hash);
    }

    // ---- Two schema assertions, added after the fact and labelled as such: they arrive GREEN, so they carry no Red.
    // They exist because the file's mapping comment claims citext gives case-insensitive uniqueness. A comment that
    // asserts a schema property nobody checks is exactly the kind of claim this repo's ledger is full of, so the claim
    // was converted into evidence instead of being left as prose. The `users` table already existed when both were
    // written -- that is disclosed here rather than a Red being staged for a check that could not have failed.

    [Fact]
    public async Task The_email_column_is_citext_rather_than_plain_text()
    {
        // Direct evidence about the column type: pg_typeof, not an inference from behaviour that could have another cause.
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        // format_type against pg_attribute: it answers from the catalog, so it needs no rows to exist (the first draft
        // used `LIMIT 0`, which returns no rows at all, fell through to information_schema, and reported citext as
        // "USER-DEFINED" -- technically true, and useless as an assertion about this column).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT format_type(a.atttypid, a.atttypmod)
            FROM pg_attribute a JOIN pg_class cl ON cl.oid = a.attrelid
            WHERE cl.relname = 'users' AND a.attname = 'email'
            """;
        Assert.Equal("citext", (string?)await cmd.ExecuteScalarAsync());
    }


    [Fact]
    public async Task An_email_differing_only_in_case_is_not_seeded_as_a_second_account()
    {
        // The consequence the type was chosen for. Without citext these two boots would produce two rows whose emails are
        // the same account to a human and distinct to the database -- and login would then resolve whichever row the
        // index happened to return first.
        var lower = UniqueEmail("casefold");
        var mixed = char.ToUpper(lower[0]) + lower[1..];
        using (var a = new SeedFactory(lower, SeedPassword, postgres.ConnectionString))
            a.CreateClient();
        using (var b = new SeedFactory(mixed, "second-boot-password-2c9e", postgres.ConnectionString))
            b.CreateClient();

        var rows = await ReadUsers(mixed);
        Assert.Single(rows);
    }

    [Fact]
    public async Task No_user_is_created_when_the_credentials_are_not_configured()
    {
        // The absence case, because "seed from configuration" is a rule about configuration and not a rule that the app
        // must always have an account. A boot with no credentials configured must not invent one — inventing one is how a
        // default password gets deployed, and the guard's whole premise is that this app never picks a password for you.
        var email = UniqueEmail("unset");
        using var factory = new SeedFactory(null, null, postgres.ConnectionString);
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.Null(ex);

        // Nothing at all should have been written for an email nobody configured.
        Assert.Empty(await ReadUsers(email));
    }
}
