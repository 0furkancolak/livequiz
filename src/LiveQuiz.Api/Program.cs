using LiveQuiz.Api.Features.Diagnostics;
using LiveQuiz.Api.Persistence;
using LiveQuiz.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AppDbContext>("livequizdb");
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapPing();

app.Run();

// Exposed for WebApplicationFactory<Program> in LiveQuiz.Api.Tests.
public partial class Program;
