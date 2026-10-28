namespace LiveQuiz.Grains.Sessions;

public sealed class LobbyOptions
{
    /// <summary>
    /// Minimum gap between head-count pushes (lobby size to players, answer count to the host). Individual joins
    /// go to the host only; everything else is coalesced, so a join or answer storm of n players costs O(n)
    /// messages instead of O(n²).
    /// </summary>
    public TimeSpan CountInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long after the deadline an answer is still accepted, to absorb network latency (server receive time
    /// ≤ deadline + grace). The question closes at the end of this window.
    /// </summary>
    public TimeSpan AnswerGrace { get; set; } = TimeSpan.FromMilliseconds(250);
}
