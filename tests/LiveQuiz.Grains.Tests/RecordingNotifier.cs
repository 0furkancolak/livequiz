using System.Collections.Concurrent;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Sessions;

namespace LiveQuiz.Grains.Tests;

/// <summary>Process-wide because TestCluster silos run in-process; tests filter by their own session id.</summary>
public sealed class RecordingNotifier : ISessionNotifier
{
    public static RecordingNotifier Instance { get; } = new();

    private readonly ConcurrentQueue<(Guid Session, string Kind, object? Data)> _events = new();

    public IReadOnlyList<(string Kind, object? Data)> For(Guid sessionId) =>
        _events.Where(e => e.Session == sessionId).Select(e => (e.Kind, e.Data)).ToList();

    public Task PlayerJoined(Guid sessionId, PlayerInfo player) => Record(sessionId, nameof(PlayerJoined), player);

    public Task PlayerConnectionChanged(Guid sessionId, Guid playerId, bool connected) =>
        Record(sessionId, nameof(PlayerConnectionChanged), (playerId, connected));

    public Task LobbyCount(Guid sessionId, int count) => Record(sessionId, nameof(LobbyCount), count);

    public Task SessionCancelled(Guid sessionId) => Record(sessionId, nameof(SessionCancelled), null);

    private Task Record(Guid sessionId, string kind, object? data)
    {
        _events.Enqueue((sessionId, kind, data));
        return Task.CompletedTask;
    }
}
