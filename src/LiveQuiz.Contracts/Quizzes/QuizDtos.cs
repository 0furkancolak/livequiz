namespace LiveQuiz.Contracts.Quizzes;

public sealed record SaveOption(string Text, bool IsCorrect);

public sealed record SaveQuestion(string Text, int TimeLimitSeconds, IReadOnlyList<SaveOption> Options);

public sealed record SaveQuizRequest(string Title, string? Description, IReadOnlyList<SaveQuestion> Questions);

public sealed record QuizSummary(Guid Id, string Title, string? Description, int QuestionCount, DateTimeOffset UpdatedAtUtc);

public sealed record OptionDto(Guid Id, string Text, bool IsCorrect);

public sealed record QuestionDto(Guid Id, string Text, int TimeLimitSeconds, IReadOnlyList<OptionDto> Options);

/// <summary>Host-facing view of a quiz. It exposes correct answers, so it must never be sent to players.</summary>
public sealed record QuizDetail(
    Guid Id,
    string Title,
    string? Description,
    IReadOnlyList<QuestionDto> Questions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
