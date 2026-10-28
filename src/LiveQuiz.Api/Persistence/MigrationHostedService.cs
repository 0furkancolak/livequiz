using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Persistence;

/// <summary>
/// Applies pending EF migrations on startup. Enabled by <c>Database:MigrateOnStartup</c> (on in Development);
/// production should apply migrations as a separate deployment step.
/// </summary>
public sealed class MigrationHostedService(IServiceScopeFactory scopes, ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        logger.LogInformation("Applying database migrations");
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
