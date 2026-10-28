using System.Data.Common;
using System.Net;
using ManagedCode.Orleans.SignalR.Server.Extensions;
using Npgsql;
using Orleans.Configuration;
using Orleans.Serialization;

namespace LiveQuiz.Api.Features.Sessions;

public static class OrleansSetup
{
    private const string PostgresInvariant = "Npgsql";

    /// <summary>
    /// Co-hosts the Orleans silo in the Api (ADR 0003). Grain state lives in Postgres through ADO.NET persistence.
    /// <c>Cluster:Provider</c> is <c>Localhost</c> (one silo, the default for tests and a plain <c>dotnet run</c>) or
    /// <c>AdoNet</c>: silos find each other through the membership table in Postgres (ADR 0016).
    /// </summary>
    public static WebApplicationBuilder AddOrleansSilo(this WebApplicationBuilder builder)
    {
        DbProviderFactories.RegisterFactory(PostgresInvariant, NpgsqlFactory.Instance);
        var connectionString = builder.Configuration.GetConnectionString("livequizdb")
            ?? throw new InvalidOperationException("ConnectionStrings:livequizdb is missing.");
        var section = builder.Configuration.GetSection("Cluster");
        var clustering = section["Provider"] ?? "Localhost";

        builder.UseOrleans(silo =>
        {
            switch (clustering)
            {
                case "Localhost":
                    silo.UseLocalhostClustering();
                    break;
                case "AdoNet":
                    silo.Configure<ClusterOptions>(o =>
                    {
                        o.ClusterId = section["ClusterId"] ?? "livequiz";
                        o.ServiceId = section["ServiceId"] ?? "livequiz";
                    });
                    silo.UseAdoNetClustering(o =>
                    {
                        o.Invariant = PostgresInvariant;
                        o.ConnectionString = connectionString;
                    });
                    // Every instance needs its own ports; the orchestrator hands them out. On one machine the silos
                    // find each other on loopback; in containers Cluster:AdvertisedHost names the address the others use.
                    silo.ConfigureEndpoints(
                        ResolveAdvertisedAddress(section["AdvertisedHost"]),
                        section.GetValue<int>("SiloPort"),
                        section.GetValue<int>("GatewayPort"),
                        listenOnAnyHostAddress: section["AdvertisedHost"] is { Length: > 0 });
                    break;
                default:
                    throw new InvalidOperationException($"Cluster:Provider must be Localhost or AdoNet, not '{clustering}'.");
            }

            silo.AddAdoNetGrainStorageAsDefault(options =>
            {
                options.Invariant = PostgresInvariant;
                options.ConnectionString = connectionString;
            });

            // The SignalR backplane keeps connection and group bookkeeping in grains (ADR 0015). That data is
            // rebuilt by reconnecting clients, so it stays in memory rather than in Postgres.
            // The backplane carries hub messages between silos with the Orleans serializer, which knows nothing of the
            // DTOs in LiveQuiz.Contracts. They stay free of Orleans attributes (the browser uses them too), so
            // Orleans writes them as JSON.
            silo.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type.Namespace?.StartsWith("LiveQuiz.Contracts", StringComparison.Ordinal) == true));
            silo.ConfigureOrleansSignalR();
            silo.AddOrleansSignalRInMemoryStorage();
        });
        return builder;
    }

    private static IPAddress ResolveAdvertisedAddress(string? host) =>
        string.IsNullOrWhiteSpace(host)
            ? IPAddress.Loopback
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
}
