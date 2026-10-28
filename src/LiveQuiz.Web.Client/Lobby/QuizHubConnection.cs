using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Web.Client.Lobby;

/// <summary>
/// Retry schedule after a dropped connection: immediately, then 1, 2, 5 and 10 seconds, then every 30 seconds,
/// and give up after <see cref="GiveUpAfter"/> (a game rarely outlasts a five minute outage).
/// </summary>
public sealed class ReconnectPolicy : IRetryPolicy
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan[] Steps =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.ElapsedTime >= GiveUpAfter)
            return null;
        return retryContext.PreviousRetryCount < Steps.Length ? Steps[retryContext.PreviousRetryCount] : TimeSpan.FromSeconds(30);
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
            .WithUrl(url, options => configure?.Invoke(options))
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

    private async Task RejoinAsync()
    {
        try
        {
            if (_rejoin is { } rejoin)
                await rejoin(CancellationToken.None);
        }
        catch (Exception)
        {
            // The server refused (session over) or dropped again before answering: nothing left to resume.
            Lose();
            await _connection.StopAsync();
        }
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
