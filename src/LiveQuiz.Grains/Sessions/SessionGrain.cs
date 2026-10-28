using System.Security.Cryptography;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Pins;
using LiveQuiz.Grains.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveQuiz.Grains.Sessions;

[GenerateSerializer]
public sealed class SessionState
{
    [Id(0)] public SessionStatus Status { get; set; }
    [Id(1)] public string Pin { get; set; } = string.Empty;
    [Id(2)] public string OwnerId { get; set; } = string.Empty;
    [Id(3)] public Guid QuizId { get; set; }
    [Id(4)] public string QuizTitle { get; set; } = string.Empty;
    [Id(5)] public Dictionary<Guid, LobbyPlayer> Players { get; set; } = [];
}

[GenerateSerializer]
public sealed class LobbyPlayer
{
    [Id(0)] public string Nickname { get; set; } = string.Empty;
    [Id(1)] public bool Connected { get; set; }
}

public sealed class SessionGrain(
    [PersistentState("session")] IPersistentState<SessionState> state,
    ISessionNotifier notifier,
    IOptions<LobbyOptions> options,
    TimeProvider time,
    ILogger<SessionGrain> logger) : Grain, ISessionGrain
{
    private const int MaxPinAttempts = 25;

    private readonly HashSet<string> _nicknameKeys = [];
    private IGrainTimer? _countTimer;
    private DateTimeOffset _lastCountPush = DateTimeOffset.MinValue;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        foreach (var player in state.State.Players.Values)
            _nicknameKeys.Add(NicknamePolicy.UniquenessKey(player.Nickname));
        return Task.CompletedTask;
    }

    public async Task<string> Open(string ownerId, Guid quizId, string quizTitle)
    {
        if (state.State.Status != SessionStatus.NotOpened)
            throw new InvalidOperationException("Session was already opened.");

        var sessionId = this.GetPrimaryKey();
        for (var attempt = 0; attempt < MaxPinAttempts; attempt++)
        {
            var pin = GeneratePin();
            if (!await GrainFactory.GetGrain<IPinRegistryGrain>(pin).TryReserve(sessionId))
                continue;

            state.State.Status = SessionStatus.Lobby;
            state.State.Pin = pin;
            state.State.OwnerId = ownerId;
            state.State.QuizId = quizId;
            state.State.QuizTitle = quizTitle;
            await state.WriteStateAsync();
            return pin;
        }

        throw new InvalidOperationException("Could not allocate a free PIN.");
    }

    public async Task<JoinResult> Join(string nickname)
    {
        if (state.State.Status != SessionStatus.Lobby)
            return new JoinResult(JoinStatus.NotJoinable);

        var error = NicknamePolicy.Validate(nickname);
        if (error != NicknameError.None)
            return new JoinResult(JoinStatus.InvalidNickname, NicknameError: error);

        if (state.State.Players.Count >= SessionLimits.MaxPlayers)
            return new JoinResult(JoinStatus.SessionFull);

        var name = NicknamePolicy.Normalize(nickname);
        if (!_nicknameKeys.Add(NicknamePolicy.UniquenessKey(name)))
            return new JoinResult(JoinStatus.NicknameTaken);

        var playerId = Guid.CreateVersion7();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        state.State.Players[playerId] = new LobbyPlayer { Nickname = name };
        try
        {
            await GrainFactory.GetGrain<IPlayerGrain>(playerId, PlayerKeys.Extension(this.GetPrimaryKey()))
                .Initialize(name, PlayerGrain.HashToken(token));
            await state.WriteStateAsync();
        }
        catch
        {
            state.State.Players.Remove(playerId);
            _nicknameKeys.Remove(NicknamePolicy.UniquenessKey(name));
            throw;
        }

        await Notify(() => notifier.PlayerJoined(this.GetPrimaryKey(), new PlayerInfo(playerId, name, false)));
        QueueLobbyCount();
        return new JoinResult(JoinStatus.Joined, playerId, name, token);
    }

    public async Task SetPlayerConnected(Guid playerId, bool connected)
    {
        if (state.State.Status != SessionStatus.Lobby || !state.State.Players.TryGetValue(playerId, out var player)
            || player.Connected == connected)
            return;

        player.Connected = connected;
        await state.WriteStateAsync();
        await Notify(() => notifier.PlayerConnectionChanged(this.GetPrimaryKey(), playerId, connected));
    }

    public async Task Cancel()
    {
        if (state.State.Status != SessionStatus.Lobby)
            return;

        state.State.Status = SessionStatus.Cancelled;
        await state.WriteStateAsync();
        await GrainFactory.GetGrain<IPinRegistryGrain>(state.State.Pin).Release();
        await Notify(() => notifier.SessionCancelled(this.GetPrimaryKey()));
    }

    public Task<SessionInfo> GetInfo()
    {
        var s = state.State;
        var players = s.Players
            .Select(p => new PlayerSnapshot(p.Key, p.Value.Nickname, p.Value.Connected))
            .ToArray();
        return Task.FromResult(new SessionInfo(s.Status, s.Pin, s.OwnerId, s.QuizId, s.QuizTitle, players));
    }

    // At most one count push per interval: the first goes out immediately, later ones are coalesced into a single timer tick.
    private void QueueLobbyCount()
    {
        if (_countTimer is not null)
            return;

        var wait = _lastCountPush + options.Value.CountInterval - time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
        {
            _ = PushCount();
            return;
        }

        _countTimer = this.RegisterGrainTimer(async () =>
        {
            _countTimer?.Dispose();
            _countTimer = null;
            await PushCount();
        }, new GrainTimerCreationOptions(wait, Timeout.InfiniteTimeSpan));
    }

    private async Task PushCount()
    {
        _lastCountPush = time.GetUtcNow();
        await Notify(() => notifier.LobbyCount(this.GetPrimaryKey(), state.State.Players.Count));
    }

    private async Task Notify(Func<Task> push)
    {
        // Delivery is best effort: a hub outage must not fail or roll back a join.
        try
        {
            await push();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push a lobby update for session {SessionId}", this.GetPrimaryKey());
        }
    }

    private static string GeneratePin() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, SessionLimits.PinLength))
            .ToString($"D{SessionLimits.PinLength}");
}
