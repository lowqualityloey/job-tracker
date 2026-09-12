using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-050 (first half) — **the app must refuse to boot with default credentials in `Production`.**
/// Spec §4.2 step 4; AC-4's sibling risk; test-plan row `-050`, seam **Integration**, priority **p0**.
///
/// ## Why this is a boot test and not a unit test of a validator
///
/// The claim in the spec is about the *application*, not about a function: "a startup guard, because the failure mode of a
/// seeded account is a published one." A unit test on `EnsureSafeToStart` would pass forever in a build where nobody
/// removed the call from `Program.cs` — the guard existing and the guard running are two different facts, and only the
/// second one is worth deploying. So every assertion here goes through `WebApplicationFactory`, which runs the real
/// startup path.
///
/// ## Why there is no Red stub this time
///
/// <c>-047</c> needed a throwing stub so its Red would be behavioural instead of <c>CS0246</c>. Here absence is already
/// observable: with no guard, booting in `Production` with the placeholder password **succeeds**, which is precisely the
/// defect — `Assert.Throws` reports "no exception thrown", and that failure is the property.
///
/// ## The half of `-050` deliberately not here
///
/// "Seeded user comes from configuration" is **not asserted below**, because seeding needs the `users` table and no
/// migration exists (nor does `dotnet ef` on this machine). Spec §4.2 puts the guard before the seed anyway — you must be
/// unable to publish default credentials *before* anything can write them — so the remainder of this behaviour lands with
/// the migration rather than being faked against an in-memory stand-in.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BootGuardTests(PostgresFixture postgres)
{
    /// <summary>
    /// The value `appsettings.Development.json` is expected to carry so local development needs no secrets. Guarding on a
    /// named sentinel rather than "is it strong enough" keeps the rule decidable: the app cannot ship a password that is
    /// also the documented placeholder.
    /// </summary>
    private const string Placeholder = "change-me-before-deploy";

    /// <summary>
    /// A factory whose environment is chosen per test; everything else matches the existing harness.
    ///
    /// <b>It gets a real database, and the first run of this file is why.</b> I gave it a bogus connection string on the
    /// theory that nothing touches the DB at startup. Wrong: <c>Program.cs</c> calls <c>Database.Migrate()</c> before
    /// <c>app.Run()</c>, so every Production test failed with <c>NpgsqlException : Failed to connect to 127.0.0.1:1</c> —
    /// an <c>Assert.Throws</c> mismatch about a socket, not about credentials. That failure would have been *indistinguishable
    /// from the guard working* if I had asserted merely "boot threw". So: a real container, and the asserted exception
    /// <b>type and message</b>, and the guard belongs before the migration — refusing to boot should mean not touching the
    /// schema either.
    /// </summary>
    private sealed class TestFactory(string environment, string connectionString) : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string> _settings = [];

        public TestFactory WithSetting(string key, string value)
        {
            _settings[key] = value;
            return this;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            foreach (var (key, value) in _settings) builder.UseSetting(key, value);
        }
    }

    [Fact]
    public void The_app_refuses_to_boot_in_Production_with_the_placeholder_password()
    {
        var factory = new TestFactory("Production", postgres.ConnectionString)
            .WithSetting("Auth:Bootstrap:Email", "ops@example.com")
            .WithSetting("Auth:Bootstrap:Password", Placeholder);

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public void The_app_refuses_to_boot_in_Production_when_bootstrap_credentials_are_missing()
    {
        // The more likely production accident is not a weak password, it is nobody setting one: an unset key, a forgotten
        // secret manager entry, an image built before the deploy config landed. "Missing" must not mean "no account",
        // because no account means no one can log in and the deployment looks healthy from the outside.
        var factory = new TestFactory("Production", postgres.ConnectionString);

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public void The_refusal_names_the_setting_to_fix_and_never_the_secret()
    {
        // Two halves, and the second one is the reason this is a test rather than a code-review note: startup exceptions
        // land in logs, crash dumps, and container orchestration output that is retained far longer and shared far more
        // widely than a database row. An operator must be able to fix this from the message; an attacker must not be able
        // to read it.
        var secret = "a-real-looking-password-9f2c";
        var factory = new TestFactory("Production", postgres.ConnectionString)
            .WithSetting("Auth:Bootstrap:Password", secret);   // email missing -> refused

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Auth:Bootstrap:Email", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_boots_without_bootstrap_credentials()
    {
        // The other direction, and the one that keeps the guard deployable rather than merely correct: local work and the
        // rest of this suite must not need secrets to start the app. A guard that fails in Development too is a guard that
        // gets commented out by Friday.
        var factory = new TestFactory("Development", postgres.ConnectionString);
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.Null(ex);
    }
}
