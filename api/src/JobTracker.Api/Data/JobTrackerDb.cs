using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Data;

/// <summary>
/// The persistence context for M3. Named and shaped per spec §4 (<c>ApplicationCatalog</c> is the deep
/// module; this is the <see cref="DbContext"/> it uses directly — no <c>IRepository&lt;T&gt;</c> wrapper,
/// because wrapping EF would re-introduce the seam the frontend already has).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately empty in Slice 0.</b> It declares no <c>DbSet</c> and no mapping, because the table it
/// owns is created by the failing tests that need it (<c>BEHAVIOR-m3-backend-api-026</c> onward), not by a
/// schema commit that nothing drives. This class exists so the migration pipeline and the design-time
/// factory can be proven now, while there is nothing to lose.
/// </para>
/// </remarks>
public sealed class JobTrackerDb(DbContextOptions<JobTrackerDb> options) : DbContext(options)
{
    /// <summary>
    /// The one aggregate root M3 owns. `DbSet<Applications>` is spelled as a property with `Set<T>()` because
    /// that is the form EF's own docs use and it keeps the class open for the read-only queries later slices
    /// will want. No repository wrapper over this: §4.1 names ApplicationCatalog as the deep module and it uses
    /// the DbContext directly, because inventing IRepository&lt;T&gt; here would rebuild the seam the frontend
    /// already has and make two seams move together — the thing DECISION-m3-backend-api-005 exists to avoid.
    /// </summary>
    public DbSet<Application> Applications => Set<Application>();

    /// <summary>M4's single bootstrap account (spec DECISION-m4-auth-002, ASSUMPTION-m4-auth-002).</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Server-side sessions, one row per login (spec DECISION-m4-auth-002).</summary>
    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>
    /// Naming is explicit, and it earned its keep: EF's default is the CLR name verbatim, so the first real
    /// migration this context produced was `CREATE TABLE "Applications" ("Id" uuid)`. The approved DDL
    /// (spec §4.1) says `applications` / `company_name`, and quoted mixed-case identifiers would make every
    /// hand-written query and every `psql` session an exercise in escaping.
    ///
    /// `EFCore.NamingConventions` + `UseSnakeCaseNamingConvention()` would do this automatically. It is not
    /// installed, deliberately: the alternative is a dozen `HasColumnName` lines in one file that *is* the
    /// contract, and this repository justifies each dependency rather than adding the convenient one by
    /// reflex. The trade-off is that a new column whose name is not declared arrives as PascalCase — and the
    /// generated migration shows it in review, which is the same visibility the convention would have given.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Application>(entity =>
        {
            // §4.1's CHECK, declared in the model so the migration carries it. It was missing entirely until
            // BEHAVIOR-…-032's Red inserted 'Escalated' into the real table and watched the database take it — see
            // that commit for how a Slice 0 test that asserted the same violation on a *temporary probe table*
            // nearly let AC-4 be recorded as half-verified.
            entity.ToTable("applications", table => table.HasCheckConstraint(
                "applications_status_check",
                "status in ('Saved', 'Applied', 'Interview', 'Rejected', 'Offer')"));
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyName).HasColumnName("company_name");
            entity.Property(e => e.JobTitle).HasColumnName("job_title");
            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.AppliedAt).HasColumnName("applied_at");
            entity.Property(e => e.Notes).HasColumnName("notes");
            // HasDefaultValueSql("now()") is here because the schema had something worse: adding a NOT NULL
            // column to an existing table made EF write `DEFAULT TIMESTAMPTZ '-infinity'` into the DDL, a
            // placeholder nobody chose that §4.1 does not specify. A missing created_at should either mean
            // "this instant" or be an error — never -infinity, which sorts beautifully and means nothing.
            entity.Property(e => e.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("now()");
            // HasDefaultValueSql("now()") is here because the schema had something worse: adding a NOT NULL
            // column to an existing table made EF write `DEFAULT TIMESTAMPTZ '-infinity'` into the DDL, a
            // placeholder nobody chose that §4.1 does not specify. A missing created_at should either mean
            // "this instant" or be an error — never -infinity, which sorts beautifully and means nothing.
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at")
                .HasDefaultValueSql("now()")
                // The store owns this column on both paths: DEFAULT now() on INSERT, and the trigger added by the
                // TouchUpdatedAtOnUpdate migration on UPDATE — spec §5 promises "server-generated timestamps only;
                // updated_at set in the DB, never sent by the client". Declaring it generated is what stops EF
                // writing back the value it read earlier and returning that stale instant in the 200 body of the
                // update that just moved it.
                .ValueGeneratedOnAddOrUpdate();

            // §4.1's second statement. Recorded honestly: this index is the one part of the approved DDL that no
            // AC or behaviour asserts — spec guard G-6 covers the CHECK list agreeing with the validator, and
            // AC-4 covers the CHECK being enforced by the engine, but nothing covers "the index exists". It ships
            // because it is approved design, and the gap is an open item in the task record rather than a quiet
            // omission or a fake test.
            entity.HasIndex(e => e.UpdatedAt, "applications_updated_at_idx").IsDescending(true);

            // The optimistic-concurrency token. IsRowVersion() on Npgsql means xmin: concurrency token, generated
            // on add and on update, so no code path has to remember to bump it and a read cannot bump it by
            // accident.
            entity.Property(e => e.Revision).HasColumnName("xmin").IsRowVersion();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");

            // citext, because "unique email" and "case-insensitive lookup" are contradictory promises in a plain text
            // column: `Ops@Example.test` and `ops@example.test` would both insert, and then login finds whichever row
            // Postgres happened to index first. The extension is created by the migration, and the unique index below
            // therefore enforces case-insensitive uniqueness rather than byte equality.
            entity.Property(e => e.Email).HasColumnName("email").HasColumnType("citext");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasIndex(e => e.Email, "users_email_idx").IsUnique();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");

            // CASCADE, because a session without its user is not a session — and deleting a user is the only way M4 can
            // ever remove one (there is no account-deletion UI), so leaving 401-producing orphans behind would be the
            // design rather than the accident.
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.UserId, "sessions_user_id_idx");
            // Reads by expiry are the shape of the prune -056's login-time cleanup needs, and of every "is this dead"
            // check; index the column the queries range on, not the one they filter by equality after.
            entity.HasIndex(e => e.ExpiresAt, "sessions_expires_at_idx");
        });
    }
}
