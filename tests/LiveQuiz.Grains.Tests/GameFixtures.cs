using LiveQuiz.Grains.Sessions;

namespace LiveQuiz.Grains.Tests;

internal static class GameFixtures
{
    /// <summary>Two questions, 10 s each; the correct option is 0 for the first and 1 for the second.</summary>
    public static QuestionSnapshot[] Questions() =>
    [
        new("Capital of France?", 10, ["Paris", "Lyon", "Nice", "Lille"], 0),
        new("2 + 2?", 10, ["3", "4", "5", "6"], 1)
    ];
}
