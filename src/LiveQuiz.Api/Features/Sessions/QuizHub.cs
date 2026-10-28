using System.Security.Claims;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Players;
using LiveQuiz.Grains.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace LiveQuiz.Api.Features.Sessions;

/// <summary>
/// Credentials are checked inside the invocations (cookie for hosts, reconnect token for players), so nothing
/// secret ever appears in the connection URL. A connection that has not joined yet receives nothing.
/// </summary>
public sealed class QuizHub(IGrainFactory grains) : Hub<IQuizClient>, IQuizHub
{
    private const string SessionKey = "sessionId";
    private const string PlayerKey = "playerId";

    public async Task<HostLobbySnapshot> JoinAsHost(Guid sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var info = await grains.GetGrain<ISessionGrain>(sessionId).GetInfo();
        if (userId is null || info.Status != SessionStatus.Lobby || info.OwnerId != userId)
            throw new HubException("Session not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, QuizHubNames.HostGroup(sessionId));
        var players = info.Players.Select(p => new PlayerInfo(p.PlayerId, p.Nickname, p.Connected)).ToList();
        return new HostLobbySnapshot(sessionId, info.Pin, info.QuizTitle, players);
    }

    public async Task<PlayerLobbySnapshot> JoinAsPlayer(Guid sessionId, Guid playerId, string reconnectToken)
    {
        var player = grains.GetGrain<IPlayerGrain>(playerId, PlayerKeys.Extension(sessionId));
        var info = await grains.GetGrain<ISessionGrain>(sessionId).GetInfo();
        var me = info.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (info.Status != SessionStatus.Lobby || me is null || !await player.Verify(reconnectToken))
            throw new HubException("Session not found.");

        Context.Items[SessionKey] = sessionId;
        Context.Items[PlayerKey] = playerId;
        await Groups.AddToGroupAsync(Context.ConnectionId, QuizHubNames.PlayersGroup(sessionId));
        await player.SetConnected(true);
        await grains.GetGrain<ISessionGrain>(sessionId).SetPlayerConnected(playerId, true);
        return new PlayerLobbySnapshot(sessionId, me.Nickname, info.Players.Length);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionKey, out var s) && s is Guid sessionId
            && Context.Items.TryGetValue(PlayerKey, out var p) && p is Guid playerId)
        {
            await grains.GetGrain<IPlayerGrain>(playerId, PlayerKeys.Extension(sessionId)).SetConnected(false);
            await grains.GetGrain<ISessionGrain>(sessionId).SetPlayerConnected(playerId, false);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
