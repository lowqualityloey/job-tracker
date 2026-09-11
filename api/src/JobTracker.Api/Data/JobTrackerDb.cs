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
            entity.ToTable("applications");
            entity.Property(e => e.Id).HasColumnName("id");
        });
    }
}
