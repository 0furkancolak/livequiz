using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Web.Host;

/// <summary>The host panel's live view of a session lobby; faked in component tests.</summary>
public interface IHostLobbyClient : IAsyncDisposable
{
    event Action<PlayerInfo>? PlayerJoined;
    event Action<Guid, bool>? PlayerConnectionChanged;
    event Action<int>? CountChanged;
    event Action? Cancelled;

    Task<HostLobbySnapshot> ConnectAsync(Guid sessionId, CancellationToken ct = default);
}
