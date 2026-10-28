using LiveQuiz.Contracts.Quizzes;

namespace LiveQuiz.Api.Tests;

internal static class QuizFactory
{
    public static SaveQuestion Question(int n, int correctIndex = 0, int optionCount = 4) =>
        new($"Question {n}?", 20, Enumerable.Range(0, optionCount).Select(i => new SaveOption($"Option {i}", i == correctIndex)).ToList());

    public static SaveQuizRequest Quiz(int questions = 5, string title = "Geography") =>
        new(title, "A test quiz", Enumerable.Range(1, questions).Select(n => Question(n, n % 4)).ToList());
}
