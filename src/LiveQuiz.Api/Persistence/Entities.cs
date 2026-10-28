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
