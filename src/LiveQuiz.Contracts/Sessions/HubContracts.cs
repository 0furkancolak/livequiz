namespace LiveQuiz.Contracts.Sessions;

/// <summary>Server-to-client messages of <c>/hubs/quiz</c>.</summary>
public interface IQuizClient
{
    Task PlayerJoined(PlayerInfo player);
    Task PlayerConnectionChanged(Guid playerId, bool connected);
    Task LobbyCount(int count);
    Task SessionCancelled();
}

/// <summary>
/// Client-to-server methods of <c>/hubs/quiz</c>. Credentials travel in the invocation, never in the URL.
/// </summary>
public interface IQuizHub
{
    /// <summary>Host only (Identity cookie, must own the session). Joins the host group and returns the lobby.</summary>
    Task<HostLobbySnapshot> JoinAsHost(Guid sessionId);

    /// <summary>Player. Proves identity with the reconnect token and joins the players group.</summary>
    Task<PlayerLobbySnapshot> JoinAsPlayer(Guid sessionId, Guid playerId, string reconnectToken);
}

public static class QuizHubNames
{
    public const string Path = "/hubs/quiz";

    public static string HostGroup(Guid sessionId) => $"session:{sessionId}:host";

    public static string PlayersGroup(Guid sessionId) => $"session:{sessionId}:players";
}
