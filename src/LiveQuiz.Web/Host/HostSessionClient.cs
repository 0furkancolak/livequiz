using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Client.Lobby;
using LiveQuiz.Web.Services;
using Microsoft.Extensions.ServiceDiscovery;

namespace LiveQuiz.Web.Host;

/// <summary>
/// Server-side hub connection from the Blazor circuit straight to the Api, authenticated with the host's Identity
/// cookie held in <see cref="ApiSession"/> (never sent to the browser). Service discovery supplies the Api address.
/// </summary>
public sealed class HostSessionClient(ServiceEndpointResolver resolver, ApiSession session) : IHostSessionClient
{
    private QuizHubConnection? _hub;

    public event Action<PlayerInfo>? PlayerJoined;
    public event Action<Guid, bool>? PlayerConnectionChanged;
    public event Action? Cancelled;
    public event Action<QuestionForPlayer>? QuestionStarted;
    public event Action<int, int>? AnswerCount;
    public event Action<QuestionResults>? QuestionEnded;
    public event Action<FinalResults>? Finished;

    public async Task<HostLobbySnapshot> ConnectAsync(Guid sessionId, CancellationToken ct = default)
    {
        var source = await resolver.GetEndpointsAsync("https+http://api", ct);
        var baseUri = source.Endpoints.Select(e => (e.EndPoint as UriEndPoint)?.Uri).FirstOrDefault(u => u is not null)
            ?? throw new InvalidOperationException("The Api endpoint could not be resolved.");

        var hub = new QuizHubConnection(new Uri(baseUri, QuizHubNames.Path), options =>
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
