namespace LiveQuiz.Grains.Sessions;

public sealed class LobbyOptions
{
    /// <summary>
    /// Minimum gap between head-count pushes to the players group. Individual joins go to the host only;
    /// players get a coalesced count, so a join storm costs O(n) messages instead of O(n²).
    /// </summary>
    public TimeSpan CountInterval { get; set; } = TimeSpan.FromSeconds(1);
}
