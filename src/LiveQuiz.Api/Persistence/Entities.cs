using Microsoft.AspNetCore.Identity;

namespace LiveQuiz.Api.Persistence;

/// <summary>A host (teacher). Players never have accounts.</summary>
public sealed class AppUser : IdentityUser;

public sealed class Quiz
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string OwnerId { get; set; }
    public AppUser? Owner { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<Question> Questions { get; set; } = [];
}

public sealed class Question
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid QuizId { get; set; }
    public int Order { get; set; }
    public required string Text { get; set; }
    public int TimeLimitSeconds { get; set; }
    public List<AnswerOption> Options { get; set; } = [];
}

public sealed class AnswerOption
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid QuestionId { get; set; }
    public int Order { get; set; }
    public required string Text { get; set; }
    public bool IsCorrect { get; set; }
}

/// <summary>
/// The permanent record of a finished game. It copies what it needs (title, nicknames, question text) and has no
/// foreign key to the quiz, so deleting or editing the quiz later never changes history.
/// </summary>
public sealed class SessionRecord
{
    public Guid Id { get; set; }
    public Guid QuizId { get; set; }
    public required string QuizTitle { get; set; }
    public required string OwnerId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset FinishedAtUtc { get; set; }
    public int PlayerCount { get; set; }
    public List<SessionPlayerResult> Players { get; set; } = [];
    public List<SessionQuestionStat> Questions { get; set; } = [];
}

public sealed class SessionPlayerResult
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionRecordId { get; set; }
    public required string Nickname { get; set; }
    public int Points { get; set; }
    public int Rank { get; set; }
    public int CorrectAnswers { get; set; }
}

public sealed class SessionQuestionStat
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionRecordId { get; set; }
    public int Index { get; set; }
    public required string Text { get; set; }
    public int CorrectIndex { get; set; }
    public int[] OptionCounts { get; set; } = [];
}
