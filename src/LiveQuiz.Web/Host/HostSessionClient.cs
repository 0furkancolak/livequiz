using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Client.Lobby;
using LiveQuiz.Web.Services;
using Microsoft.AspNetCore.Components;

namespace LiveQuiz.Web.Host;

/// <summary>
/// Server-side hub connection from the Blazor circuit, authenticated with the host's Identity cookie held in
/// <see cref="ApiSession"/> (never sent to the browser). It connects through Web's own origin, like a browser does, so
/// YARP picks an Api instance on every (re)connect: a connection pinned to one instance could never reconnect once that
/// instance died (ADR 0016).
/// </summary>
public sealed class HostSessionClient(NavigationManager navigation, ApiSession session) : IHostSessionClient
{
    private QuizHubConnection? _hub;

    public event Action<PlayerInfo>? PlayerJoined;
    public event Action<Guid, bool>? PlayerConnectionChanged;
    public event Action? Cancelled;
    public event Action<QuestionForPlayer>? QuestionStarted;
    public event Action<int, int>? AnswerCount;
    public event Action<QuestionResults>? QuestionEnded;
    public event Action<FinalResults>? Finished;
    public event Action? Reconnecting;
    public event Action<HostSnapshot>? Resumed;
    public event Action? ConnectionLost;

    public async Task<HostSnapshot> ConnectAsync(Guid sessionId, CancellationToken ct = default)
    {
        var hub = new QuizHubConnection(new Uri(new Uri(navigation.BaseUri), QuizHubNames.Path), options =>
        {
            if (session.CookieHeader is { } cookie)
                options.Headers["Cookie"] = cookie;
        });
        hub.PlayerJoined += p => PlayerJoined?.Invoke(p);
        hub.PlayerConnectionChanged += (id, c) => PlayerConnectionChanged?.Invoke(id, c);
        hub.SessionCancelled += () => Cancelled?.Invoke();
        hub.QuestionStarted += q => QuestionStarted?.Invoke(q);
        hub.AnswerCount += (a, t) => AnswerCount?.Invoke(a, t);
        hub.QuestionEnded += r => QuestionEnded?.Invoke(r);
        hub.SessionFinished += f => Finished?.Invoke(f);
        hub.Reconnecting += () => Reconnecting?.Invoke();
        hub.HostResumed += snapshot => Resumed?.Invoke(snapshot);
        hub.ConnectionLost += () => ConnectionLost?.Invoke();

        try
        {
            var snapshot = await hub.JoinAsHostAsync(sessionId, ct);
            _hub = hub;
            return snapshot;
        }
        catch
        {
            await hub.DisposeAsync();
            throw;
        }
    }

    public Task StartGameAsync(Guid sessionId, CancellationToken ct = default) => Hub.StartGameAsync(sessionId, ct);

    public Task NextQuestionAsync(Guid sessionId, CancellationToken ct = default) => Hub.NextQuestionAsync(sessionId, ct);

    private QuizHubConnection Hub => _hub ?? throw new InvalidOperationException("Not connected to a session.");

    public async ValueTask DisposeAsync()
    {
        if (_hub is { } hub)
        {
            _hub = null;
            await hub.DisposeAsync();
        }
    }
}
