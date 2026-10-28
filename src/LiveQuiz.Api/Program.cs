using LiveQuiz.Api.Features.Auth;
using LiveQuiz.Api.Features.Diagnostics;
using LiveQuiz.Api.Features.Quizzes;
using LiveQuiz.Api.Features.Sessions;
using LiveQuiz.Api.Persistence;
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

builder.AddOrleansSilo();
builder.Services.AddSignalR();
builder.Services.AddSingleton<LiveQuiz.Grains.Sessions.ISessionNotifier, SignalRSessionNotifier>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapPing();
app.MapHostAuth();
app.MapQuizzes();
app.MapSessions();

app.Run();

// Exposed for WebApplicationFactory<Program> in LiveQuiz.Api.Tests.
public partial class Program;
