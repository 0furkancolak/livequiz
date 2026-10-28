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

    /// <summary>
    /// How long a live session may go without any host connection before it is cancelled. A host that drops for a
    /// moment (refresh, network blip, laptop lid) keeps the game; one that never comes back frees the PIN.
    /// </summary>
    public TimeSpan HostAbsenceLimit { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Total attempts to archive a finished game (the first one runs inline, the rest in the background).</summary>
    public int ArchiveAttempts { get; set; } = 3;

    /// <summary>Wait before the first archive retry; doubles for each further one.</summary>
    public TimeSpan ArchiveRetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
