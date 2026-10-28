namespace LiveQuiz.Contracts.Sessions;

public sealed record CreateSessionRequest(Guid QuizId);

public sealed record CreateSessionResponse(Guid SessionId, string Pin);

public sealed record JoinSessionRequest(string Pin, string Nickname);

/// <summary>Returned once at join. The reconnect token is never shown again; the server stores only its hash.</summary>
public sealed record JoinSessionResponse(Guid SessionId, Guid PlayerId, string Nickname, string ReconnectToken);

public sealed record PlayerInfo(Guid PlayerId, string Nickname, bool Connected);

/// <summary>What a host sees in the lobby: PIN and every player.</summary>
public sealed record HostLobbySnapshot(Guid SessionId, string Pin, string QuizTitle, IReadOnlyList<PlayerInfo> Players);

/// <summary>What a player sees in the lobby: only a head count, never the other nicknames (O(n) fan-out instead of O(n²)).</summary>
public sealed record PlayerLobbySnapshot(Guid SessionId, string Nickname, int PlayerCount);
