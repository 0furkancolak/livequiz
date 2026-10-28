using LiveQuiz.Grains;

namespace LiveQuiz.Api.Features.Diagnostics;

public static class StatsEndpoint
{
    /// <summary>
    /// Grain write totals of this Api instance since it started, for the load test. Development only: the numbers
    /// reveal nothing secret but the endpoint is not part of the product surface.
    /// </summary>
    public static IEndpointRouteBuilder MapStats(this IEndpointRouteBuilder app)
    {
        if (app is IApplicationBuilder { ApplicationServices: var services }
            && services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            app.MapGet("/api/diagnostics/grain-writes", () => GrainMetrics.Snapshot());
        }

        return app;
    }
}
