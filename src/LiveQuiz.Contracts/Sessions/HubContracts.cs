using LiveQuiz.Contracts.Game;

namespace LiveQuiz.Contracts.Sessions;

/// <summary>Server-to-client messages of <c>/hubs/quiz</c>.</summary>
public interface IQuizClient
{
    Task PlayerJoined(PlayerInfo player);
    Task PlayerConnectionChanged(Guid playerId, bool connected);
    Task LobbyCount(int count);
    Task SessionCancelled();

    Task QuestionStarted(QuestionForPlayer question);

    /// <summary>Host only: how many players have answered so far (coalesced).</summary>
    Task AnswerCount(int answered, int total);

    Task QuestionEnded(QuestionResults results);

    /// <summary>One player only: their own outcome for the question that just ended.</summary>
    Task PlayerResult(PlayerQuestionResult result);

    Task SessionFinished(FinalResults results);
}

/// <summary>
/// Client-to-server methods of <c>/hubs/quiz</c>. Credentials travel in the invocation, never in the URL.
/// </summary>
public interface IQuizHub
{
    /// <summary>Host only (Identity cookie, must own the session). Joins the host group and returns the lobby.</summary>
    Task<HostSnapshot> JoinAsHost(Guid sessionId);

    /// <summary>Player. Proves identity with the reconnect token and joins the players group.</summary>
    Task<PlayerGameState> JoinAsPlayer(Guid sessionId, Guid playerId, string reconnectToken);

    /// <summary>Host only. Lobby to the first question; needs at least one player.</summary>
    Task StartGame(Guid sessionId);

    /// <summary>Host only. Results to the next question, or to the final results after the last one.</summary>
    Task NextQuestion(Guid sessionId);

    /// <summary>Player. The server timestamps the arrival; the client never supplies a time.</summary>
    Task<AnswerStatus> SubmitAnswer(int questionIndex, int optionIndex);
}

public static class QuizHubNames
{
    public const string Path = "/hubs/quiz";

    public static string HostGroup(Guid sessionId) => $"session:{sessionId}:host";

    public static string PlayersGroup(Guid sessionId) => $"session:{sessionId}:players";

    public static string PlayerGroup(Guid sessionId, Guid playerId) => $"session:{sessionId}:player:{playerId}";
}
