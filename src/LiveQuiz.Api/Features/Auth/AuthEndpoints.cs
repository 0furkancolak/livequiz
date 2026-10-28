using LiveQuiz.Api.Features.RateLimiting;
using LiveQuiz.Api.Persistence;
using Microsoft.AspNetCore.Identity;

namespace LiveQuiz.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IServiceCollection AddHostAuth(this IServiceCollection services)
    {
        services.AddIdentityApiEndpoints<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddEntityFrameworkStores<AppDbContext>();

        // The host panel is an API client: unauthenticated calls must get 401/403, never a login redirect.
        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // No email delivery in this project; accounts are usable immediately.
        services.AddSingleton<IEmailSender<AppUser>, NoOpEmailSender>();
        services.AddAuthorization();
        return services;
    }

    public static IEndpointRouteBuilder MapHostAuth(this IEndpointRouteBuilder app)
    {
        // Everything under /api/auth (login, register, refresh, ...) counts against the per-IP budget.
        var auth = app.MapGroup("/api/auth").RequireRateLimiting(RateLimitSetup.AuthPolicy);
        auth.MapIdentityApi<AppUser>();

        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }

    private sealed class NoOpEmailSender : IEmailSender<AppUser>
    {
        public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) => Task.CompletedTask;
        public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode) => Task.CompletedTask;
    }
}
