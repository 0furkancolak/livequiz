using LiveQuiz.Contracts.Game;

namespace LiveQuiz.Grains.Tests;

public class ScoringTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(5, 875)]
    [InlineData(10, 750)]
    [InlineData(20, 500)]
    [InlineData(20.2, 500)]
    public void Correct_answers_score_between_500_and_1000(double seconds, int expected) =>
        Assert.Equal(expected, Scoring.Points(true, TimeSpan.FromSeconds(seconds), Limit));

    [Fact]
    public void Wrong_answers_score_nothing() => Assert.Equal(0, Scoring.Points(false, TimeSpan.Zero, Limit));

    [Fact]
    public void Negative_elapsed_is_treated_as_instant() =>
        Assert.Equal(1000, Scoring.Points(true, TimeSpan.FromSeconds(-1), Limit));
}
