using LiveQuiz.Api.Persistence;
using LiveQuiz.Grains.Sessions;
using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Features.Sessions;

/// <summary>Writes the finished game to Postgres. Idempotent on the session id, so a retried save is harmless.</summary>
internal sealed class EfSessionArchive(IServiceScopeFactory scopes) : ISessionArchive
{
    public async Task Save(SessionSummary summary)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.SessionRecords.AnyAsync(r => r.Id == summary.SessionId))
            return;

        db.SessionRecords.Add(new SessionRecord
        {
            Id = summary.SessionId,
            QuizId = summary.QuizId,
            QuizTitle = summary.QuizTitle,
            OwnerId = summary.OwnerId,
            StartedAtUtc = summary.StartedAtUtc,
            FinishedAtUtc = summary.FinishedAtUtc,
            PlayerCount = summary.Players.Count,
            Players = summary.Players
                .Select(p => new SessionPlayerResult { Nickname = p.Nickname, Points = p.Points, Rank = p.Rank, CorrectAnswers = p.CorrectAnswers })
                .ToList(),
            Questions = summary.Questions
                .Select(q => new SessionQuestionStat { Index = q.Index, Text = q.Text, CorrectIndex = q.CorrectIndex, OptionCounts = [.. q.OptionCounts] })
                .ToList()
        });
        await db.SaveChangesAsync();
    }
}
