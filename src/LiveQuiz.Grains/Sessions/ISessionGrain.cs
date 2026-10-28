using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Grains.Sessions;

/// <summary>Key: session id. State machine: Lobby → QuestionActive ⇄ QuestionResults → Finished (or Cancelled).</summary>
public interface ISessionGrain : IGrainWithGuidKey
{
    /// <summary>Allocates a PIN, stores the quiz snapshot and opens the lobby. Returns the PIN.</summary>
    Task<string> Open(string ownerId, Guid quizId, string quizTitle, QuestionSnapshot[] questions);

    Task<JoinResult> Join(string nickname);

    Task SetPlayerConnected(Guid playerId, bool connected);

    Task<GameCommandResult> Start();

    /// <summary>From results to the next question, or to Finished after the last one.</summary>
    Task<GameCommandResult> Next();

    Task<AnswerStatus> SubmitAnswer(Guid playerId, int questionIndex, int optionIndex);

    Task Cancel();

    Task<SessionInfo> GetInfo();
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
