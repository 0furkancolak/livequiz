using LiveQuiz.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Persistence;

/// <summary>
/// Creates a demo host with two ready-to-play quizzes so a fresh install can run a game straight away.
/// Enabled by <c>Seed:Demo</c> (on in Development); idempotent, and safe when several instances start together.
/// </summary>
public sealed class DemoSeedHostedService(IServiceScopeFactory scopes, TimeProvider time, ILogger<DemoSeedHostedService> logger) : IHostedService
{
    private const string DemoEmail = DemoAccount.Email;
    private const string DemoPassword = DemoAccount.Password;

    private const long SeedLockKey = 0x4c51_5345_4544; // "LQSEED"

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT pg_advisory_lock({SeedLockKey})";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var user = await users.FindByEmailAsync(DemoEmail);
            if (user is null)
            {
                user = new AppUser { UserName = DemoEmail, Email = DemoEmail, EmailConfirmed = true };
                var created = await users.CreateAsync(user, DemoPassword);
                if (!created.Succeeded)
                {
                    logger.LogWarning("Could not create the demo host: {Errors}", string.Join("; ", created.Errors.Select(e => e.Description)));
                    return;
                }
            }

            if (await db.Quizzes.AnyAsync(q => q.OwnerId == user.Id, cancellationToken))
                return;

            var now = time.GetUtcNow();
            foreach (var (title, description, questions) in Quizzes())
            {
                var quiz = new Quiz { OwnerId = user.Id, Title = title, Description = description, CreatedAtUtc = now, UpdatedAtUtc = now };
                for (var q = 0; q < questions.Length; q++)
                {
                    var (text, seconds, correct, options) = questions[q];
                    quiz.Questions.Add(new Question
                    {
                        Order = q,
                        Text = text,
                        TimeLimitSeconds = seconds,
                        Options = options.Select((o, i) => new AnswerOption { Order = i, Text = o, IsCorrect = i == correct }).ToList(),
                    });
                }

                db.Quizzes.Add(quiz);
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded the demo host {Email} with sample quizzes", DemoEmail);
        }
        finally
        {
            await connection.CloseAsync(); // closing the session releases the lock
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static (string Title, string Description, (string Text, int Seconds, int Correct, string[] Options)[] Questions)[] Quizzes() =>
    [
        ("World geography warm-up", "Five quick questions to get a room going.",
        [
            ("Which is the largest country by area?", 15, 2, ["Canada", "China", "Russia", "United States"]),
            ("What is the capital of Australia?", 15, 1, ["Sydney", "Canberra", "Melbourne", "Perth"]),
            ("Which river flows through Cairo?", 15, 0, ["The Nile", "The Congo", "The Niger", "The Zambezi"]),
            ("Mount Kilimanjaro is in which country?", 20, 3, ["Kenya", "Uganda", "Ethiopia", "Tanzania"]),
            ("Which ocean is the deepest?", 20, 1, ["Atlantic", "Pacific", "Indian", "Arctic"]),
        ]),
        ("Tech trivia", "Computing history and everyday developer lore.",
        [
            ("What does HTTP stand for?", 15, 0, ["HyperText Transfer Protocol", "High Transfer Text Protocol", "HyperText Transmission Process", "Host Transfer Terminal Protocol"]),
            ("Which language runs natively in every web browser?", 15, 2, ["Python", "Java", "JavaScript", "C#"]),
            ("What year was the first iPhone released?", 20, 1, ["2005", "2007", "2009", "2011"]),
            ("Which of these is a relational database?", 15, 3, ["Redis", "Kafka", "Neo4j", "PostgreSQL"]),
        ]),
    ];
}
