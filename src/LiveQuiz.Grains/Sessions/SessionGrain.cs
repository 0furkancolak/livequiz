using System.Security.Cryptography;
using LiveQuiz.Contracts.Game;
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
    [Id(6)] public QuestionSnapshot[] Questions { get; set; } = [];
    [Id(7)] public int CurrentIndex { get; set; } = -1;
    [Id(8)] public DateTimeOffset QuestionStartedAtUtc { get; set; }
    [Id(9)] public DateTimeOffset StartedAtUtc { get; set; }
    [Id(10)] public List<QuestionStat> Stats { get; set; } = [];
}

[GenerateSerializer]
public sealed class LobbyPlayer
{
    [Id(0)] public string Nickname { get; set; } = string.Empty;
    [Id(1)] public bool Connected { get; set; }
    [Id(2)] public int Points { get; set; }
    [Id(3)] public int CorrectAnswers { get; set; }
}

[GenerateSerializer]
public sealed class QuestionStat
{
    [Id(0)] public int Index { get; set; }
    [Id(1)] public int[] OptionCounts { get; set; } = [];
}

public sealed class SessionGrain : Grain, ISessionGrain
{
    private const int MaxPinAttempts = 25;
    private const int TopForResults = 5;
    private const int TopForFinal = 10;

    private readonly IPersistentState<SessionState> _state;
    private readonly ISessionNotifier _notifier;
    private readonly ISessionArchive _archive;
    private readonly LobbyOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionGrain> _logger;

    private readonly HashSet<string> _nicknameKeys = [];
    // Answers of the open question live in memory only; they are persisted when the question closes (ADR 0012).
    private readonly Dictionary<Guid, (int Option, TimeSpan Elapsed)> _answers = [];
    private readonly CoalescedPush _lobbyCount;
    private readonly CoalescedPush _answerCount;
    private IGrainTimer? _closeTimer;

    public SessionGrain(
        [PersistentState("session")] IPersistentState<SessionState> state,
        ISessionNotifier notifier,
        ISessionArchive archive,
        IOptions<LobbyOptions> options,
        TimeProvider time,
        ILogger<SessionGrain> logger)
    {
        _state = state;
        _notifier = notifier;
        _archive = archive;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _lobbyCount = new CoalescedPush(this, time, () => _options.CountInterval,
            () => Notify(() => _notifier.LobbyCount(this.GetPrimaryKey(), _state.State.Players.Count)));
        _answerCount = new CoalescedPush(this, time, () => _options.CountInterval,
            () => Notify(() => _notifier.AnswerCount(this.GetPrimaryKey(), _answers.Count, _state.State.Players.Count)));
    }

    private SessionState S => _state.State;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        foreach (var player in S.Players.Values)
            _nicknameKeys.Add(NicknamePolicy.UniquenessKey(player.Nickname));

        // After a silo restart mid-question the open answers are gone but the question can still end on time.
        if (S.Status == SessionStatus.QuestionActive)
            ArmCloseTimer();
        return Task.CompletedTask;
    }

    public async Task<string> Open(string ownerId, Guid quizId, string quizTitle, QuestionSnapshot[] questions)
    {
        if (S.Status != SessionStatus.NotOpened)
            throw new InvalidOperationException("Session was already opened.");
        if (questions.Length == 0)
            throw new ArgumentException("A session needs at least one question.", nameof(questions));

        var sessionId = this.GetPrimaryKey();
        for (var attempt = 0; attempt < MaxPinAttempts; attempt++)
        {
            var pin = GeneratePin();
            if (!await GrainFactory.GetGrain<IPinRegistryGrain>(pin).TryReserve(sessionId))
                continue;

            S.Status = SessionStatus.Lobby;
            S.Pin = pin;
            S.OwnerId = ownerId;
            S.QuizId = quizId;
            S.QuizTitle = quizTitle;
            S.Questions = questions;
            await _state.WriteStateAsync();
            return pin;
        }

        throw new InvalidOperationException("Could not allocate a free PIN.");
    }

    public async Task<JoinResult> Join(string nickname)
    {
        if (S.Status != SessionStatus.Lobby)
            return new JoinResult(JoinStatus.NotJoinable);

        var error = NicknamePolicy.Validate(nickname);
        if (error != NicknameError.None)
            return new JoinResult(JoinStatus.InvalidNickname, NicknameError: error);

        if (S.Players.Count >= SessionLimits.MaxPlayers)
            return new JoinResult(JoinStatus.SessionFull);

        var name = NicknamePolicy.Normalize(nickname);
        if (!_nicknameKeys.Add(NicknamePolicy.UniquenessKey(name)))
            return new JoinResult(JoinStatus.NicknameTaken);

        var playerId = Guid.CreateVersion7();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        S.Players[playerId] = new LobbyPlayer { Nickname = name };
        try
        {
            await GrainFactory.GetGrain<IPlayerGrain>(playerId, PlayerKeys.Extension(this.GetPrimaryKey()))
                .Initialize(name, PlayerGrain.HashToken(token));
            await _state.WriteStateAsync();
        }
        catch
        {
            S.Players.Remove(playerId);
            _nicknameKeys.Remove(NicknamePolicy.UniquenessKey(name));
            throw;
        }

        await Notify(() => _notifier.PlayerJoined(this.GetPrimaryKey(), new PlayerInfo(playerId, name, false)));
        _lobbyCount.Queue();
        return new JoinResult(JoinStatus.Joined, playerId, name, token);
    }

    public async Task SetPlayerConnected(Guid playerId, bool connected)
    {
        if (!S.Status.IsLive() || !S.Players.TryGetValue(playerId, out var player) || player.Connected == connected)
            return;

        player.Connected = connected;
        await _state.WriteStateAsync();
        await Notify(() => _notifier.PlayerConnectionChanged(this.GetPrimaryKey(), playerId, connected));
    }

    public async Task<GameCommandResult> Start()
    {
        if (S.Status != SessionStatus.Lobby)
            return GameCommandResult.InvalidState;
        if (S.Players.Count == 0)
            return GameCommandResult.NoPlayers;

        S.StartedAtUtc = _time.GetUtcNow();
        await BeginQuestion(0);
        return GameCommandResult.Ok;
    }

    public async Task<GameCommandResult> Next()
    {
        if (S.Status != SessionStatus.QuestionResults)
            return GameCommandResult.InvalidState;

        if (S.CurrentIndex + 1 < S.Questions.Length)
            await BeginQuestion(S.CurrentIndex + 1);
        else
            await Finish();
        return GameCommandResult.Ok;
    }

    public async Task<AnswerStatus> SubmitAnswer(Guid playerId, int questionIndex, int optionIndex)
    {
        // The arrival time is taken first and is the only clock that counts.
        var received = _time.GetUtcNow();

        if (!S.Players.ContainsKey(playerId))
            return AnswerStatus.NotInGame;
        if (!S.Status.IsLive() || S.Status == SessionStatus.Lobby || questionIndex != S.CurrentIndex)
            return AnswerStatus.WrongQuestion;
        if (S.Status != SessionStatus.QuestionActive)
            return AnswerStatus.TooLate;

        var question = S.Questions[S.CurrentIndex];
        if (optionIndex < 0 || optionIndex >= question.Options.Length)
            return AnswerStatus.InvalidOption;

        var deadline = S.QuestionStartedAtUtc + TimeSpan.FromSeconds(question.TimeLimitSeconds);
        if (received > deadline + _options.AnswerGrace)
            return AnswerStatus.TooLate;
        if (_answers.ContainsKey(playerId))
            return AnswerStatus.Duplicate;

        _answers[playerId] = (optionIndex, received - S.QuestionStartedAtUtc);
        _answerCount.Queue();

        if (_answers.Count == S.Players.Count)
            await CloseQuestion();
        return AnswerStatus.Accepted;
    }

    public async Task Cancel()
    {
        if (!S.Status.IsLive())
            return;

        DisarmTimers();
        S.Status = SessionStatus.Cancelled;
        await _state.WriteStateAsync();
        await GrainFactory.GetGrain<IPinRegistryGrain>(S.Pin).Release();
        await Notify(() => _notifier.SessionCancelled(this.GetPrimaryKey()));
    }

    public Task<SessionInfo> GetInfo()
    {
        var players = S.Players
            .Select(p => new PlayerSnapshot(p.Key, p.Value.Nickname, p.Value.Connected))
            .ToArray();
        return Task.FromResult(new SessionInfo(
            S.Status, S.Pin, S.OwnerId, S.QuizId, S.QuizTitle, players, S.CurrentIndex, S.Questions.Length));
    }

    private async Task BeginQuestion(int index)
    {
        S.CurrentIndex = index;
        S.Status = SessionStatus.QuestionActive;
        S.QuestionStartedAtUtc = _time.GetUtcNow();
        _answers.Clear();
        await _state.WriteStateAsync();
        ArmCloseTimer();

        var q = S.Questions[index];
        var dto = new QuestionForPlayer(index, S.Questions.Length, q.Text, q.Options, q.TimeLimitSeconds, q.TimeLimitSeconds * 1000);
        await Notify(() => _notifier.QuestionStarted(this.GetPrimaryKey(), dto));
    }

    private void ArmCloseTimer()
    {
        _closeTimer?.Dispose();
        var question = S.Questions[S.CurrentIndex];
        var closeAt = S.QuestionStartedAtUtc + TimeSpan.FromSeconds(question.TimeLimitSeconds) + _options.AnswerGrace;
        var due = closeAt - _time.GetUtcNow();
        _closeTimer = this.RegisterGrainTimer(
            () => CloseQuestion(),
            new GrainTimerCreationOptions(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan));
    }

    private void DisarmTimers()
    {
        _closeTimer?.Dispose();
        _closeTimer = null;
        _lobbyCount.Cancel();
        _answerCount.Cancel();
    }

    private async Task CloseQuestion()
    {
        if (S.Status != SessionStatus.QuestionActive)
            return;

        DisarmTimers();
        var sessionId = this.GetPrimaryKey();
        var index = S.CurrentIndex;
        var question = S.Questions[index];
        var limit = TimeSpan.FromSeconds(question.TimeLimitSeconds);

        var counts = new int[question.Options.Length];
        var outcomes = new Dictionary<Guid, (int Option, int Points, bool Correct, TimeSpan Elapsed)>();
        foreach (var (playerId, answer) in _answers)
        {
            var correct = answer.Option == question.CorrectIndex;
            var points = Scoring.Points(correct, answer.Elapsed, limit);
            counts[answer.Option]++;
            outcomes[playerId] = (answer.Option, points, correct, answer.Elapsed);

            var player = S.Players[playerId];
            player.Points += points;
            if (correct)
                player.CorrectAnswers++;
        }
        _answers.Clear();

        S.Stats.Add(new QuestionStat { Index = index, OptionCounts = counts });
        S.Status = SessionStatus.QuestionResults;
        await _state.WriteStateAsync();

        await Task.WhenAll(outcomes.Select(o => RecordForPlayer(sessionId, o.Key, index, o.Value)));

        var standings = Rank();
        var isLast = index + 1 >= S.Questions.Length;
        var results = new QuestionResults(
            index, S.Questions.Length, question.CorrectIndex, counts, standings.Take(TopForResults).Select(s => s.Standing).ToList(), isLast);
        await Notify(() => _notifier.QuestionEnded(sessionId, results));

        await Task.WhenAll(standings.Select(s =>
        {
            outcomes.TryGetValue(s.PlayerId, out var o);
            var answered = outcomes.ContainsKey(s.PlayerId);
            var result = new PlayerQuestionResult(
                index, answered, answered ? o.Option : null, o.Correct, o.Points, s.Standing.Points, s.Standing.Rank, standings.Count);
            return Notify(() => _notifier.PlayerResult(sessionId, s.PlayerId, result));
        }));
    }

    private async Task RecordForPlayer(Guid sessionId, Guid playerId, int index, (int Option, int Points, bool Correct, TimeSpan Elapsed) o)
    {
        try
        {
            await GrainFactory.GetGrain<IPlayerGrain>(playerId, PlayerKeys.Extension(sessionId))
                .RecordAnswer(index, o.Option, o.Points, (int)o.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the answer of player {PlayerId} in session {SessionId}", playerId, sessionId);
        }
    }

    private async Task Finish()
    {
        var sessionId = this.GetPrimaryKey();
        DisarmTimers();
        S.Status = SessionStatus.Finished;
        await _state.WriteStateAsync();
        await GrainFactory.GetGrain<IPinRegistryGrain>(S.Pin).Release();

        var standings = Rank();
        await Notify(() => _notifier.SessionFinished(
            sessionId, new FinalResults(standings.Take(TopForFinal).Select(s => s.Standing).ToList(), standings.Count)));

        var summary = new SessionSummary(
            sessionId, S.QuizId, S.QuizTitle, S.OwnerId, S.StartedAtUtc, _time.GetUtcNow(),
            standings.Select(s => new PlayerSummary(s.Standing.Nickname, s.Standing.Points, s.Standing.Rank, S.Players[s.PlayerId].CorrectAnswers)).ToList(),
            S.Stats.Select(st => new QuestionSummary(st.Index, S.Questions[st.Index].Text, S.Questions[st.Index].CorrectIndex, st.OptionCounts)).ToList());
        await Notify(() => _archive.Save(summary));
    }

    // Competition ranking: equal points share a rank and the next rank is skipped (1, 1, 3).
    private List<(Guid PlayerId, Standing Standing)> Rank()
    {
        var ordered = S.Players
            .OrderByDescending(p => p.Value.Points)
            .ThenBy(p => p.Value.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<(Guid, Standing)>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var rank = i > 0 && ordered[i].Value.Points == ordered[i - 1].Value.Points ? result[i - 1].Item2.Rank : i + 1;
            result.Add((ordered[i].Key, new Standing(rank, ordered[i].Value.Nickname, ordered[i].Value.Points)));
        }
        return result;
    }

    private async Task Notify(Func<Task> push)
    {
        // Delivery is best effort: a hub outage must not fail or roll back a game step.
        try
        {
            await push();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push an update for session {SessionId}", this.GetPrimaryKey());
        }
    }

    private static string GeneratePin() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, SessionLimits.PinLength))
            .ToString($"D{SessionLimits.PinLength}");
}
