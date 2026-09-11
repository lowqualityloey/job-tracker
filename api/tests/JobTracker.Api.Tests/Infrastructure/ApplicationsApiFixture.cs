using System.Net.Http;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace JobTracker.Api.Tests.Infrastructure;

/// <summary>
/// The real host, in-process, pointed at a real container.
///
/// <see cref="WebApplicationFactory{TEntryPoint}"/> boots the application exactly as <c>dotnet run</c> would —
/// including <c>Program.cs</c>'s middleware order — and hands back an <see cref="HttpClient"/> that talks to it
/// over an in-memory pipeline. Nothing is mocked. The one thing replaced is the connection string, via ordinary
/// configuration, because "which database" is environment and not behaviour.
///
/// This is why <c>Program.cs</c> carries <c>public partial class Program;</c>: the factory needs the entry point
/// as a type, and the top-level-statements file otherwise keeps it internal.
/// </summary>
public sealed class ApplicationsApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        // UseSetting, not environment variables: it lands in the same configuration pipeline the app reads at
        // startup, and it keeps the fixture honest about *what* it is overriding.
        builder.UseSetting("ConnectionStrings:Default", connectionString);
}

/// <summary>
/// Container + host, started together. Also the tests' back door for arranging rows: <see cref="OpenConnection"/>
/// speaks raw SQL, so a test can put the database into a state **without** using the endpoints that test is about
/// to assert. A POST that proves GET would let a round-tripping bug in both directions pass — the same reason
/// 027 seeds with SQL rather than calling a create endpoint that does not exist yet.
/// </summary>
public sealed class ApplicationsApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresFixture.PinnedImage).Build();

    // private set, not init: the factory cannot exist until the container is running (see InitializeAsync), and
    // an init-only property can only be assigned in a constructor or object initializer.
    public ApplicationsApiFactory Factory { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;

    /// <summary>Valid only after <see cref="InitializeAsync"/>; mapped port, not 5432, by construction.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlConnection OpenConnection() => new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Built after the start on purpose: a Testcontainers connection string needs the mapped public port,
        // which does not exist until the container is running.
        Factory = new ApplicationsApiFactory(ConnectionString);
        Http = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Http.Dispose();
        Factory.Dispose();
        await _container.DisposeAsync();
    }
}
