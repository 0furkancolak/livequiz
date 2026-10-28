namespace LiveQuiz.Grains.Sessions;

public sealed record PlayerSummary(string Nickname, int Points, int Rank, int CorrectAnswers);

public sealed record QuestionSummary(int Index, string Text, int CorrectIndex, IReadOnlyList<int> OptionCounts);

public sealed record SessionSummary(
    Guid SessionId, Guid QuizId, string QuizTitle, string OwnerId,
    DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc,
    IReadOnlyList<PlayerSummary> Players, IReadOnlyList<QuestionSummary> Questions);

/// <summary>Where a finished game is recorded for good (the Api writes it to the relational database).</summary>
public interface ISessionArchive
{
    Task Save(SessionSummary summary);
}
