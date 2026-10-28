using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Web.Client.Lobby;

/// <summary>
/// Retry schedule after a dropped connection: immediately, then 1, 2 and 5 seconds, then every 5 to 7 seconds (the
/// jitter keeps hundreds of players of a dead server instance from reconnecting in lockstep), and give up after
/// <see cref="GiveUpAfter"/> (a game rarely outlasts a five minute outage). When a server instance dies the cluster needs
/// about 20 seconds to notice, so a short cap on the delay is what makes recovery quick once it has.
/// </summary>
public sealed class ReconnectPolicy : IRetryPolicy
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan[] Steps =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.ElapsedTime >= GiveUpAfter)
            return null;
        return retryContext.PreviousRetryCount < Steps.Length
            ? Steps[retryContext.PreviousRetryCount]
            : TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 2000));
    }
}

/// <summary>
/// Typed wrapper over the <c>/hubs/quiz</c> connection, shared by the WASM player screen (browser) and the
/// host panel (server-side circuit). Method and event names come from the Contracts interfaces.
/// </summary>
public sealed class QuizHubConnection : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private Func<CancellationToken, Task>? _rejoin;
    private bool _disposed, _lost;

    public QuizHubConnection(Uri url, Action<HttpConnectionOptions>? configure = null)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // Several Api instances sit behind the proxy without sticky sessions (ADR 0015), so the connection
                // must not depend on a negotiate request landing on the same instance as the socket.
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                configure?.Invoke(options);
            })
            .WithAutomaticReconnect(new ReconnectPolicy())
            .Build();

        _connection.Reconnecting += _ =>
        {
            Reconnecting?.Invoke();
            return Task.CompletedTask;
        };
        // A new connection has no group memberships, so joining again is part of reconnecting.
        _connection.Reconnected += async _ => await RejoinAsync();
        _connection.Closed += _ =>
        {
            Lose();
            return Task.CompletedTask;
        };

        _connection.On<PlayerInfo>(nameof(IQuizClient.PlayerJoined), p => PlayerJoined?.Invoke(p));
        _connection.On<Guid, bool>(nameof(IQuizClient.PlayerConnectionChanged), (id, c) => PlayerConnectionChanged?.Invoke(id, c));
        _connection.On<int>(nameof(IQuizClient.LobbyCount), c => LobbyCount?.Invoke(c));
        _connection.On(nameof(IQuizClient.SessionCancelled), () => SessionCancelled?.Invoke());
        _connection.On<QuestionForPlayer>(nameof(IQuizClient.QuestionStarted), q => QuestionStarted?.Invoke(q));
        _connection.On<int, int>(nameof(IQuizClient.AnswerCount), (a, t) => AnswerCount?.Invoke(a, t));
        _connection.On<QuestionResults>(nameof(IQuizClient.QuestionEnded), r => QuestionEnded?.Invoke(r));
        _connection.On<PlayerQuestionResult>(nameof(IQuizClient.PlayerResult), r => PlayerResult?.Invoke(r));
        _connection.On<FinalResults>(nameof(IQuizClient.SessionFinished), f => SessionFinished?.Invoke(f));
    }

    /// <summary>The connection dropped and the client is retrying.</summary>
    public event Action? Reconnecting;

    /// <summary>The connection is back and the host has rejoined; carries the current state of the game.</summary>
    public event Action<HostSnapshot>? HostResumed;

    public event Action<PlayerGameState>? PlayerResumed;

    /// <summary>Retries ran out, or the server no longer accepts this client (session over).</summary>
    public event Action? ConnectionLost;

    public event Action<PlayerInfo>? PlayerJoined;
    public event Action<Guid, bool>? PlayerConnectionChanged;
    public event Action<int>? LobbyCount;
    public event Action? SessionCancelled;
    public event Action<QuestionForPlayer>? QuestionStarted;
    public event Action<int, int>? AnswerCount;
    public event Action<QuestionResults>? QuestionEnded;
    public event Action<PlayerQuestionResult>? PlayerResult;
    public event Action<FinalResults>? SessionFinished;

    public async Task<HostSnapshot> JoinAsHostAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _connection.StartAsync(ct);
        var snapshot = await _connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), sessionId, ct);
        _rejoin = async token => HostResumed?.Invoke(
            await _connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), sessionId, token));
        return snapshot;
    }

    public async Task<PlayerGameState> JoinAsPlayerAsync(
        Guid sessionId, Guid playerId, string reconnectToken, CancellationToken ct = default)
    {
        await _connection.StartAsync(ct);
        var state = await _connection.InvokeAsync<PlayerGameState>(
            nameof(IQuizHub.JoinAsPlayer), sessionId, playerId, reconnectToken, ct);
        _rejoin = async token => PlayerResumed?.Invoke(await _connection.InvokeAsync<PlayerGameState>(
            nameof(IQuizHub.JoinAsPlayer), sessionId, playerId, reconnectToken, token));
        return state;
    }

    // Rejoining can fail for a while after a server instance dies: the cluster needs some time to notice and to
    // reactivate the session on a live silo. Only a definite "not found" ends the game; anything else is retried.
    private async Task RejoinAsync()
    {
        var started = DateTime.UtcNow;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (_rejoin is { } rejoin)
                    await rejoin(CancellationToken.None);
                return;
            }
            catch (HubException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                await GiveUpAsync(); // the session is over or this player is not in it
                return;
            }
            catch (Exception)
            {
                // The connection dropped again: the automatic reconnect will raise Reconnected and call this again.
                if (_disposed || _connection.State != HubConnectionState.Connected)
                    return;
                if (DateTime.UtcNow - started >= ReconnectPolicy.GiveUpAfter)
                {
                    await GiveUpAsync();
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 + attempt, 5)));
        }
    }

    private async Task GiveUpAsync()
    {
        Lose();
        await _connection.StopAsync();
    }

    private void Lose()
    {
        if (_disposed || _lost)
            return;
        _lost = true;
        ConnectionLost?.Invoke();
    }

    public Task StartGameAsync(Guid sessionId, CancellationToken ct = default) =>
        _connection.InvokeAsync(nameof(IQuizHub.StartGame), sessionId, ct);

    public Task NextQuestionAsync(Guid sessionId, CancellationToken ct = default) =>
        _connection.InvokeAsync(nameof(IQuizHub.NextQuestion), sessionId, ct);

    public Task<AnswerStatus> SubmitAnswerAsync(int questionIndex, int optionIndex, CancellationToken ct = default) =>
        _connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), questionIndex, optionIndex, ct);

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return _connection.DisposeAsync();
    }
}
