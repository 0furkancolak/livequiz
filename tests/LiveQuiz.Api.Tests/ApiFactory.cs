using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace LiveQuiz.Api.Tests;

/// <summary>
/// One real PostgreSQL container for the whole test run, with the real EF migrations applied by the API on startup.
/// Tests isolate themselves by using unique users, not by resetting the database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.11-alpine").Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting (not ConfigureAppConfiguration): Program reads these while building services.
        builder.UseSetting("ConnectionStrings:livequizdb", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
