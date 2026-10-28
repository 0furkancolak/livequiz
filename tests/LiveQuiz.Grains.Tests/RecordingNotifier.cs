using System.Collections.Concurrent;
using LiveQuiz.Contracts.Game;
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

    public IReadOnlyList<T> Of<T>(Guid sessionId, string kind) =>
        For(sessionId).Where(e => e.Kind == kind).Select(e => (T)e.Data!).ToList();

    public Task PlayerJoined(Guid sessionId, PlayerInfo player) => Record(sessionId, nameof(PlayerJoined), player);

    public Task PlayerConnectionChanged(Guid sessionId, Guid playerId, bool connected) =>
        Record(sessionId, nameof(PlayerConnectionChanged), (playerId, connected));

    public Task LobbyCount(Guid sessionId, int count) => Record(sessionId, nameof(LobbyCount), count);

    public Task SessionCancelled(Guid sessionId) => Record(sessionId, nameof(SessionCancelled), null);

    public Task QuestionStarted(Guid sessionId, QuestionForPlayer question) => Record(sessionId, nameof(QuestionStarted), question);

    public Task AnswerCount(Guid sessionId, int answered, int total) => Record(sessionId, nameof(AnswerCount), (answered, total));

    public Task QuestionEnded(Guid sessionId, QuestionResults results) => Record(sessionId, nameof(QuestionEnded), results);

    public Task PlayerResult(Guid sessionId, Guid playerId, PlayerQuestionResult result) =>
        Record(sessionId, nameof(PlayerResult), (playerId, result));

    public Task SessionFinished(Guid sessionId, FinalResults results) => Record(sessionId, nameof(SessionFinished), results);

    private Task Record(Guid sessionId, string kind, object? data)
    {
        _events.Enqueue((sessionId, kind, data));
        return Task.CompletedTask;
    }
}

public sealed class RecordingArchive : ISessionArchive
{
    public static RecordingArchive Instance { get; } = new();

    private readonly ConcurrentQueue<SessionSummary> _saved = new();
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();
    private readonly Dictionary<Guid, int> _failuresLeft = [];

    public IReadOnlyList<SessionSummary> For(Guid sessionId) => _saved.Where(s => s.SessionId == sessionId).ToList();

    /// <summary>Number of times <see cref="Save"/> was called for the session, failed calls included.</summary>
    public int Attempts(Guid sessionId) => _attempts.GetValueOrDefault(sessionId);

    /// <summary>Makes the next <paramref name="times"/> saves of this session throw.</summary>
    public void FailNext(Guid sessionId, int times)
    {
        lock (_failuresLeft)
            _failuresLeft[sessionId] = times;
    }

    public Task Save(SessionSummary summary)
    {
        _attempts.AddOrUpdate(summary.SessionId, 1, (_, n) => n + 1);
        if (TakeFailure(summary.SessionId))
            throw new InvalidOperationException("The archive is down (test).");

        _saved.Enqueue(summary);
        return Task.CompletedTask;
    }

    private bool TakeFailure(Guid sessionId)
    {
        lock (_failuresLeft)
        {
            if (_failuresLeft.GetValueOrDefault(sessionId) <= 0)
                return false;
            _failuresLeft[sessionId]--;
            return true;
        }
    }
}
