using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Web.Client.Lobby;

/// <summary>
/// Typed wrapper over the <c>/hubs/quiz</c> connection, shared by the WASM player screen (browser) and the
/// host panel (server-side circuit). Method and event names come from the Contracts interfaces.
/// </summary>
public sealed class QuizHubConnection : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public QuizHubConnection(Uri url, Action<HttpConnectionOptions>? configure = null)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(url, options => configure?.Invoke(options))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<PlayerInfo>(nameof(IQuizClient.PlayerJoined), p => PlayerJoined?.Invoke(p));
        _connection.On<Guid, bool>(nameof(IQuizClient.PlayerConnectionChanged), (id, c) => PlayerConnectionChanged?.Invoke(id, c));
        _connection.On<int>(nameof(IQuizClient.LobbyCount), c => LobbyCount?.Invoke(c));
        _connection.On(nameof(IQuizClient.SessionCancelled), () => SessionCancelled?.Invoke());
    }

    public event Action<PlayerInfo>? PlayerJoined;
    public event Action<Guid, bool>? PlayerConnectionChanged;
    public event Action<int>? LobbyCount;
    public event Action? SessionCancelled;

    public async Task<HostLobbySnapshot> JoinAsHostAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _connection.StartAsync(ct);
        return await _connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), sessionId, ct);
    }

    public async Task<PlayerLobbySnapshot> JoinAsPlayerAsync(
        Guid sessionId, Guid playerId, string reconnectToken, CancellationToken ct = default)
    {
        await _connection.StartAsync(ct);
        return await _connection.InvokeAsync<PlayerLobbySnapshot>(
            nameof(IQuizHub.JoinAsPlayer), sessionId, playerId, reconnectToken, ct);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
