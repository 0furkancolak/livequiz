using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Grains.Sessions;

/// <summary>Key: session id. Phase 2 covers only the lobby; later phases extend the state machine.</summary>
public interface ISessionGrain : IGrainWithGuidKey
{
    /// <summary>Allocates a PIN and opens the lobby. Returns the PIN. Fails if the session was already opened.</summary>
    Task<string> Open(string ownerId, Guid quizId, string quizTitle);

    Task<JoinResult> Join(string nickname);

    Task SetPlayerConnected(Guid playerId, bool connected);

    Task Cancel();

    Task<SessionInfo> GetInfo();
}

public enum SessionStatus
{
    NotOpened,
    Lobby,
    Cancelled
}

public enum JoinStatus
{
    Joined,
    InvalidNickname,
    NicknameTaken,
    SessionFull,
    NotJoinable
}

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
    [property: Id(5)] PlayerSnapshot[] Players);
