namespace LiveQuiz.Contracts.Quizzes;

/// <summary>Content limits shared by the API validator and the host panel editor.</summary>
public static class QuizLimits
{
    public const int MaxTitleLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxQuestions = 50;
    public const int MaxQuestionTextLength = 300;
    public const int MinTimeLimitSeconds = 5;
    public const int MaxTimeLimitSeconds = 120;
    public const int DefaultTimeLimitSeconds = 20;
    public const int MinOptions = 2;
    public const int MaxOptions = 4;
    public const int MaxOptionTextLength = 120;
}
