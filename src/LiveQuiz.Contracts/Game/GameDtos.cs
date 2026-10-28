namespace LiveQuiz.Contracts.Game;

public enum AnswerStatus
{
    Accepted,
    Duplicate,
    TooLate,
    WrongQuestion,
    InvalidOption,
    NotInGame
}

/// <summary>
/// A question as shown to players and host while it is open. It deliberately has no correct-answer field:
/// the answer is only revealed in <see cref="QuestionResults"/> after the question has ended.
/// </summary>
public sealed record QuestionForPlayer(int Index, int Total, string Text, IReadOnlyList<string> Options, int TimeLimitSeconds, int RemainingMs);

public sealed record Standing(int Rank, string Nickname, int Points);

/// <summary>Sent to host and players when a question ends.</summary>
public sealed record QuestionResults(int Index, int Total, int CorrectIndex, IReadOnlyList<int> OptionCounts, IReadOnlyList<Standing> Top, bool IsLast);

/// <summary>Sent to one player only.</summary>
public sealed record PlayerQuestionResult(int QuestionIndex, bool Answered, int? ChosenIndex, bool Correct, int Points, int TotalPoints, int Rank, int PlayerCount);

public sealed record FinalResults(IReadOnlyList<Standing> Top, int PlayerCount);

public sealed record SessionHistoryItem(Guid Id, string QuizTitle, DateTimeOffset FinishedAtUtc, int PlayerCount, string? Winner);

public sealed record QuestionStatDto(int Index, string Text, int CorrectIndex, IReadOnlyList<int> OptionCounts);

public sealed record SessionHistoryDetail(
    Guid Id, string QuizTitle, DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc,
    IReadOnlyList<Standing> Standings, IReadOnlyList<QuestionStatDto> Questions);
