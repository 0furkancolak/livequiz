using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LiveQuiz.Api.Tests;

/// <summary>
/// One real PostgreSQL container for the whole test run, with the real EF migrations applied by the API on startup.
/// Tests isolate themselves by using unique users, not by resetting the database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.11-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

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
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, FakePeerFilter>());
    }

    /// <summary>
    /// Every client gets its own forwarded address, like separate phones behind the proxy. Without it all tests would
    /// share one rate-limit bucket and trip the limits they are not testing.
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", NextAddress());
    }

    private static int _next;

    internal static string NextAddress()
    {
        var n = Interlocked.Increment(ref _next);
        return $"198.51.{n / 250 % 250}.{n % 250 + 1}";
    }

    /// <summary>The in-memory server has no TCP peer. This gives it one: loopback, or whatever X-Test-Peer says.</summary>
    private sealed class FakePeerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                var peer = context.Request.Headers["X-Test-Peer"].ToString();
                context.Connection.RemoteIpAddress = peer.Length > 0 ? IPAddress.Parse(peer) : IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
