using LiveQuiz.Api.Persistence;
using LiveQuiz.Contracts.Diagnostics;

namespace LiveQuiz.Api.Features.Diagnostics;

public static class PingEndpoint
{
    public static IEndpointRouteBuilder MapPing(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ping", async (AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var reachable = await db.Database.CanConnectAsync(ct);
            return new PingResponse("api", reachable, time.GetUtcNow());
        });

        return app;
    }
}
