using System.Security.Claims;
using LiveQuiz.Api.Features.RateLimiting;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Players;
using LiveQuiz.Grains.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace LiveQuiz.Api.Features.Sessions;

/// <summary>
/// Credentials are checked inside the invocations (cookie for hosts, reconnect token for players), so nothing
/// secret ever appears in the connection URL. A connection that has not joined yet receives nothing.
/// Joining is idempotent and works in any phase: the returned snapshot is how a refreshed or reconnected client
/// finds out where the game is (ADR 0013).
/// </summary>
public sealed class QuizHub(IGrainFactory grains, IOptions<RateLimitOptions> limits, TimeProvider time) : Hub<IQuizClient>, IQuizHub
{
    private const string SessionKey = "sessionId";
    private const string PlayerKey = "playerId";
    private const string HostSessionKey = "hostSessionId";
    private const string AnswerLimitKey = "answerLimit";

    public async Task<HostSnapshot> JoinAsHost(Guid sessionId)
    {
        await RequireHostAsync(sessionId, allowFinished: true);

        Context.Items[HostSessionKey] = sessionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, QuizHubNames.HostGroup(sessionId));

        // Joined to the group first, then the snapshot is read: nothing that happens in between can be missed.
        var session = grains.GetGrain<ISessionGrain>(sessionId);
        await session.SetHostConnected(Context.ConnectionId, true);
        return SessionViewMapper.ToHost(sessionId, await session.GetHostView());
    }

    public async Task<PlayerGameState> JoinAsPlayer(Guid sessionId, Guid playerId, string reconnectToken)
    {
        var session = grains.GetGrain<ISessionGrain>(sessionId);
        var player = grains.GetGrain<IPlayerGrain>(playerId, PlayerKeys.Extension(sessionId));
        if (!await player.Verify(reconnectToken))
            throw new HubException("Session not found.");

        Context.Items[SessionKey] = sessionId;
        Context.Items[PlayerKey] = playerId;
        await Groups.AddToGroupAsync(Context.ConnectionId, QuizHubNames.PlayersGroup(sessionId));
        await Groups.AddToGroupAsync(Context.ConnectionId, QuizHubNames.PlayerGroup(sessionId, playerId));
        await session.SetPlayerConnected(playerId, Context.ConnectionId, true);

        // One call for the player's own view: GetInfo would ship every player of the game on each join (O(n) per
        // join, so O(n^2) while 2000 people arrive) through the one grain that all of them are waiting on.
        var view = await session.GetPlayerView(playerId);
        if (view is null || !(view.Status.IsLive() || view.Status == SessionStatus.Finished))
        {
            await session.SetPlayerConnected(playerId, Context.ConnectionId, false);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, QuizHubNames.PlayersGroup(sessionId));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, QuizHubNames.PlayerGroup(sessionId, playerId));
            Context.Items.Remove(SessionKey);
            Context.Items.Remove(PlayerKey);
            throw new HubException("Session not found.");
        }
        return SessionViewMapper.ToPlayer(sessionId, view);
    }

    public async Task StartGame(Guid sessionId)
    {
        await RequireHostAsync(sessionId);
        var result = await grains.GetGrain<ISessionGrain>(sessionId).Start();
        if (result != GameCommandResult.Ok)
            throw new HubException(result == GameCommandResult.NoPlayers ? "Wait for at least one player." : "The game cannot be started now.");
    }

    public async Task NextQuestion(Guid sessionId)
    {
        await RequireHostAsync(sessionId);
        if (await grains.GetGrain<ISessionGrain>(sessionId).Next() != GameCommandResult.Ok)
            throw new HubException("There is no result screen to continue from.");
    }

    public async Task<AnswerStatus> SubmitAnswer(int questionIndex, int optionIndex)
    {
        if (!Context.Items.TryGetValue(SessionKey, out var s) || s is not Guid sessionId
            || !Context.Items.TryGetValue(PlayerKey, out var p) || p is not Guid playerId)
            return AnswerStatus.NotInGame;

        // A person taps a button; a script floods. Over-limit calls never reach the grain.
        if (!AnswerLimit.TryAcquire())
            return AnswerStatus.RateLimited;

        return await grains.GetGrain<ISessionGrain>(sessionId).SubmitAnswer(playerId, questionIndex, optionIndex);
    }

    private ConnectionRateLimit AnswerLimit
    {
        get
        {
            if (Context.Items.TryGetValue(AnswerLimitKey, out var existing) && existing is ConnectionRateLimit limit)
                return limit;

            var created = new ConnectionRateLimit(limits.Value.AnswersPerSecond, TimeSpan.FromSeconds(1), time);
            Context.Items[AnswerLimitKey] = created;
            return created;
        }
    }

    private async Task<SessionInfo> RequireHostAsync(Guid sessionId, bool allowFinished = false)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var info = await grains.GetGrain<ISessionGrain>(sessionId).GetInfo();
        var open = info.Status.IsLive() || (allowFinished && info.Status == SessionStatus.Finished);
        if (userId is null || !open || info.OwnerId != userId)
            throw new HubException("Session not found.");
        return info;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionKey, out var s) && s is Guid sessionId
            && Context.Items.TryGetValue(PlayerKey, out var p) && p is Guid playerId)
        {
            await grains.GetGrain<ISessionGrain>(sessionId).SetPlayerConnected(playerId, Context.ConnectionId, false);
        }

        if (Context.Items.TryGetValue(HostSessionKey, out var h) && h is Guid hostSessionId)
            await grains.GetGrain<ISessionGrain>(hostSessionId).SetHostConnected(Context.ConnectionId, false);

        await base.OnDisconnectedAsync(exception);
    }
}
