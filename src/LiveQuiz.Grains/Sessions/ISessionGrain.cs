using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Grains.Sessions;

/// <summary>Key: session id. State machine: Lobby → QuestionActive ⇄ QuestionResults → Finished (or Cancelled).</summary>
public interface ISessionGrain : IGrainWithGuidKey
{
    /// <summary>Allocates a PIN, stores the quiz snapshot and opens the lobby. Returns the PIN.</summary>
    Task<string> Open(string ownerId, Guid quizId, string quizTitle, QuestionSnapshot[] questions);

    Task<JoinResult> Join(string nickname);

    /// <summary>
    /// Tracks hub connections per player: a player counts as connected while at least one of their connections
    /// is open, so closing one of two tabs does not mark them away.
    /// </summary>
    Task SetPlayerConnected(Guid playerId, string connectionId, bool connected);

    /// <summary>
    /// Tracks the host's hub connections. A live session whose host has no connection for
    /// <see cref="LobbyOptions.HostAbsenceLimit"/> is cancelled, which also frees its PIN.
    /// </summary>
    Task SetHostConnected(string connectionId, bool connected);

    Task<GameCommandResult> Start();

    /// <summary>From results to the next question, or to Finished after the last one.</summary>
    Task<GameCommandResult> Next();

    Task<AnswerStatus> SubmitAnswer(Guid playerId, int questionIndex, int optionIndex);

    Task Cancel();

    Task<SessionInfo> GetInfo();

    /// <summary>Everything a (re)joining host needs to draw the current screen, including the player list.</summary>
    Task<SessionView> GetHostView();

    /// <summary>The same for one player; null when the player is not in this session.</summary>
    Task<SessionView?> GetPlayerView(Guid playerId);
}

public enum SessionStatus
{
    NotOpened,
    Lobby,
    QuestionActive,
    QuestionResults,
    Finished,
    Cancelled
}

public static class SessionStatusExtensions
{
    /// <summary>True while the session can still be joined, played or cancelled.</summary>
    public static bool IsLive(this SessionStatus status) =>
        status is SessionStatus.Lobby or SessionStatus.QuestionActive or SessionStatus.QuestionResults;
}

public enum GameCommandResult
{
    Ok,
    InvalidState,
    NoPlayers
}

public enum JoinStatus
{
    Joined,
    InvalidNickname,
    NicknameTaken,
    SessionFull,
    NotJoinable
}

/// <summary>A question as stored in the session: the quiz is snapshotted when the session opens (ADR 0008).</summary>
[GenerateSerializer, Immutable]
public sealed record QuestionSnapshot(
    [property: Id(0)] string Text,
    [property: Id(1)] int TimeLimitSeconds,
    [property: Id(2)] string[] Options,
    [property: Id(3)] int CorrectIndex);

[GenerateSerializer, Immutable]
public sealed record PlayerSnapshot(
    [property: Id(0)] Guid PlayerId,
    [property: Id(1)] string Nickname,
    [property: Id(2)] bool Connected);

[GenerateSerializer, Immutable]
public sealed record JoinResult(
    [property: Id(0)] JoinStatus Status,
    [property: Id(1)] Guid PlayerId = default,
    [property: Id(2)] string Nickname = "",
    [property: Id(3)] string? ReconnectToken = null,
    [property: Id(4)] NicknameError NicknameError = NicknameError.None);

[GenerateSerializer, Immutable]
public sealed record SessionInfo(
    [property: Id(0)] SessionStatus Status,
    [property: Id(1)] string Pin,
    [property: Id(2)] string OwnerId,
    [property: Id(3)] Guid QuizId,
    [property: Id(4)] string QuizTitle,
    [property: Id(5)] PlayerSnapshot[] Players,
    [property: Id(6)] int CurrentQuestion,
    [property: Id(7)] int QuestionCount);

[GenerateSerializer, Immutable]
public sealed record StandingView(
    [property: Id(0)] int Rank,
    [property: Id(1)] string Nickname,
    [property: Id(2)] int Points);

[GenerateSerializer, Immutable]
public sealed record QuestionView(
    [property: Id(0)] int Index,
    [property: Id(1)] int Total,
    [property: Id(2)] string Text,
    [property: Id(3)] string[] Options,
    [property: Id(4)] int TimeLimitSeconds,
    [property: Id(5)] int RemainingMs);

[GenerateSerializer, Immutable]
public sealed record ResultsView(
    [property: Id(0)] int Index,
    [property: Id(1)] int Total,
    [property: Id(2)] int CorrectIndex,
    [property: Id(3)] int[] OptionCounts,
    [property: Id(4)] StandingView[] Top,
    [property: Id(5)] bool IsLast);

[GenerateSerializer, Immutable]
public sealed record PlayerResultView(
    [property: Id(0)] int QuestionIndex,
    [property: Id(1)] bool Answered,
    [property: Id(2)] int? ChosenIndex,
    [property: Id(3)] bool Correct,
    [property: Id(4)] int Points,
    [property: Id(5)] int TotalPoints,
    [property: Id(6)] int Rank,
    [property: Id(7)] int PlayerCount);

/// <summary>
/// A point-in-time picture of a session for a client that has just (re)connected. <see cref="Players"/> is only
/// filled for hosts; <see cref="MyResult"/> and <see cref="ChosenIndex"/> only for players.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record SessionView(
    [property: Id(0)] SessionStatus Status,
    [property: Id(1)] string Pin,
    [property: Id(2)] string OwnerId,
    [property: Id(3)] string QuizTitle,
    [property: Id(4)] string Nickname,
    [property: Id(5)] int PlayerCount,
    [property: Id(6)] PlayerSnapshot[] Players,
    [property: Id(7)] QuestionView? Question,
    [property: Id(8)] int Answered,
    [property: Id(9)] int? ChosenIndex,
    [property: Id(10)] ResultsView? Results,
    [property: Id(11)] PlayerResultView? MyResult,
    [property: Id(12)] StandingView[]? Final);
