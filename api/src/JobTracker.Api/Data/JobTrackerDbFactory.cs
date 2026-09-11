using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTracker.Api.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without booting the host, which is what makes migrations
/// runnable from a shell instead of from an IDE.
/// </summary>
/// <remarks>
/// The connection string comes from <c>ConnectionStrings__Default</c> and <b>is never committed</b> — spec §5
/// allows only <c>dotnet user-secrets</c> or the environment. No fallback credential is hard-coded here on
/// purpose: a silent default would let a migration run against an unintended database, and the failure mode
/// of "it worked locally" is exactly what a design-time factory should refuse to guess about.
/// </remarks>
public sealed class JobTrackerDbFactory : IDesignTimeDbContextFactory<JobTrackerDb>
{
    public JobTrackerDb CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__Default is not set. Export it before running dotnet ef " +
                "(see api/README.md); this project deliberately keeps no credential in the repository.");

        var options = new DbContextOptionsBuilder<JobTrackerDb>()
            .UseNpgsql(connectionString)
            .Options;

        return new JobTrackerDb(options);
    }
}
