using LiveQuiz.Contracts.Game;

namespace LiveQuiz.Contracts.Sessions;

public sealed record CreateSessionRequest(Guid QuizId);

public sealed record CreateSessionResponse(Guid SessionId, string Pin);

public sealed record JoinSessionRequest(string Pin, string Nickname);

/// <summary>Returned once at join. The reconnect token is never shown again; the server stores only its hash.</summary>
public sealed record JoinSessionResponse(Guid SessionId, Guid PlayerId, string Nickname, string ReconnectToken);

public sealed record PlayerInfo(Guid PlayerId, string Nickname, bool Connected);

/// <summary>Where a session is, as far as a client is concerned. Cancelled sessions cannot be joined at all.</summary>
public enum SessionPhase
{
    Lobby,
    Question,
    Results,
    Finished
}

/// <summary>
/// Everything a host needs to draw the current screen, returned when the host (re)joins at any point of the game.
/// <see cref="Question"/> is set for the question and results phases, <see cref="Results"/> for results, and
/// <see cref="Final"/> once finished. The remaining time inside <see cref="Question"/> is computed by the server.
/// </summary>
public sealed record HostSnapshot(
    Guid SessionId, string Pin, string QuizTitle, IReadOnlyList<PlayerInfo> Players,
    SessionPhase Phase = SessionPhase.Lobby,
    QuestionForPlayer? Question = null, int Answered = 0,
    QuestionResults? Results = null, FinalResults? Final = null);

/// <summary>
/// What a player needs to resume at any point: only a head count in the lobby, never the other nicknames
/// (O(n) fan-out instead of O(n²)). <see cref="ChosenIndex"/> is the option this player already locked in.
/// </summary>
public sealed record PlayerGameState(
    Guid SessionId, string Nickname, int PlayerCount,
    SessionPhase Phase = SessionPhase.Lobby,
    QuestionForPlayer? Question = null, int? ChosenIndex = null,
    QuestionResults? Results = null, PlayerQuestionResult? Result = null, FinalResults? Final = null);
