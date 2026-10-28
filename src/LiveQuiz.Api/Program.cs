using LiveQuiz.Api.Features.Auth;
using LiveQuiz.Api.Features.Diagnostics;
using LiveQuiz.Api.Features.Quizzes;
using LiveQuiz.Api.Features.RateLimiting;
using LiveQuiz.Api.Features.Sessions;
using LiveQuiz.Api.Persistence;
using ManagedCode.Orleans.SignalR.Server.Extensions;
using LiveQuiz.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedDataProtection();
builder.AddNpgsqlDbContext<AppDbContext>("livequizdb");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostAuth();

// Migrations run automatically in Development unless overridden; production applies them as a deployment step.
if (builder.Configuration.GetValue("Database:MigrateOnStartup", builder.Environment.IsDevelopment()))
{
    builder.Services.AddHostedService<MigrationHostedService>();
}

builder.Services.AddApiRateLimiting(builder.Configuration);
builder.AddOrleansSilo();
builder.Services.AddSignalR().AddOrleans();
builder.Services.AddSingleton<LiveQuiz.Grains.Sessions.ISessionNotifier, SignalRSessionNotifier>();
builder.Services.AddSingleton<LiveQuiz.Grains.Sessions.ISessionArchive, EfSessionArchive>();

var app = builder.Build();

// First, so everything after it sees the real client address.
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapPing();
app.MapStats();
app.MapHostAuth();
app.MapQuizzes();
app.MapSessions();

app.Run();

// Exposed for WebApplicationFactory<Program> in LiveQuiz.Api.Tests.
public partial class Program;
