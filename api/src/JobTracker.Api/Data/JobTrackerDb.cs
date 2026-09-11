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
public sealed class JobTrackerDb(DbContextOptions<JobTrackerDb> options) : DbContext(options);
