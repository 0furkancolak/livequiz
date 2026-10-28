using System.Data.Common;
using Npgsql;

namespace LiveQuiz.Api.Features.Sessions;

public static class OrleansSetup
{
    private const string PostgresInvariant = "Npgsql";

    /// <summary>
    /// Co-hosts the Orleans silo in the Api (ADR 0003). Grain state lives in Postgres through ADO.NET persistence;
    /// clustering is localhost-only until a second silo arrives in Phase 5.
    /// </summary>
    public static WebApplicationBuilder AddOrleansSilo(this WebApplicationBuilder builder)
    {
        DbProviderFactories.RegisterFactory(PostgresInvariant, NpgsqlFactory.Instance);
        var connectionString = builder.Configuration.GetConnectionString("livequizdb")
            ?? throw new InvalidOperationException("ConnectionStrings:livequizdb is missing.");

        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering();
            silo.AddAdoNetGrainStorageAsDefault(options =>
            {
                options.Invariant = PostgresInvariant;
                options.ConnectionString = connectionString;
            });
        });
        return builder;
    }
}
