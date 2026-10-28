using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Grains.Sessions;

/// <summary>
/// How grains reach connected clients. Implemented by the Api on top of SignalR (see ADR 0009); grains stay
/// free of any transport dependency and tests substitute a recording fake.
/// </summary>
public interface ISessionNotifier
{
    Task PlayerJoined(Guid sessionId, PlayerInfo player);

    Task PlayerConnectionChanged(Guid sessionId, Guid playerId, bool connected);

    Task LobbyCount(Guid sessionId, int count);

    Task SessionCancelled(Guid sessionId);
}
