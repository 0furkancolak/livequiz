namespace LiveQuiz.Contracts.Game;

/// <summary>
/// Server-side scoring. Correct answers earn 500–1000 depending on how much of the time limit was left;
/// wrong or missing answers earn nothing. Elapsed time is always measured by the server.
/// </summary>
public static class Scoring
{
    public const int MaxPoints = 1000;
    public const int MinCorrectPoints = 500;

    public static int Points(bool correct, TimeSpan elapsed, TimeSpan timeLimit)
    {
        if (!correct || timeLimit <= TimeSpan.Zero)
            return 0;

        // Answers accepted inside the grace period can have elapsed > timeLimit; they score the minimum.
        var fraction = Math.Clamp(elapsed / timeLimit, 0d, 1d);
        return (int)Math.Round(MaxPoints * (1 - fraction / 2), MidpointRounding.AwayFromZero);
    }
}
