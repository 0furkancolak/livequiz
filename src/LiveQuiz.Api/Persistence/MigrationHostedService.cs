using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Persistence;

/// <summary>
/// Applies pending EF migrations on startup. Enabled by <c>Database:MigrateOnStartup</c> (on in Development);
/// production should apply migrations as a separate deployment step. Several instances may start together, so
/// the migration runs under a PostgreSQL advisory lock: the vendored Orleans scripts are not idempotent.
/// </summary>
public sealed class MigrationHostedService(IServiceScopeFactory scopes, ILogger<MigrationHostedService> logger) : IHostedService
{
    private const long MigrationLockKey = 0x4c51_4d49_4752; // "LQMIGR"

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        logger.LogInformation("Applying database migrations");

        // EF reuses a connection that is already open, so the lock and the migration share one session.
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT pg_advisory_lock({MigrationLockKey})";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync(); // closing the session releases the lock
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
