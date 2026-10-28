using LiveQuiz.Web.Client.Localization;
using Bunit;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Components.Pages.Host;
using LiveQuiz.Web.Host;
using LiveQuiz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace LiveQuiz.Web.Tests;

public sealed class HostSessionTests : BunitContext
{
    private readonly FakeHostSessionClient _lobby = new();
    private readonly Guid _sessionId = Guid.NewGuid();

    public HostSessionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new Loc("en"));
        AddAuthorization().SetAuthorized("host@example.test");
        Services.AddSingleton<IHostSessionClient>(_lobby);
        Services.AddScoped<BrowserApi>();
        Services.AddSingleton(new ApiClient(new HttpClient { BaseAddress = new Uri("http://localhost") }, new ApiSession(new HttpContextAccessor())));
    }

    private IRenderedComponent<HostSession> RenderLobby() => Render<HostSession>(p => p.Add(c => c.SessionId, _sessionId));

    [Fact]
    public void Shows_the_pin_and_players_already_in_the_lobby()
    {
        _lobby.Snapshot = new HostSnapshot(_sessionId, "123456", "Capitals",
            [new PlayerInfo(Guid.NewGuid(), "Ann", true), new PlayerInfo(Guid.NewGuid(), "Bob", false)]);

        var cut = RenderLobby();

        Assert.Equal("123456", cut.Find("#session-pin").TextContent.Trim());
        Assert.Equal("2 players", cut.Find("#lobby-count").TextContent.Trim());
        Assert.Equal(["Ann", "Bob"], cut.FindAll(".player-chip").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public async Task A_joining_player_appears_without_reloading()
    {
        var cut = RenderLobby();
        Assert.Empty(cut.FindAll(".player-chip"));

        await cut.InvokeAsync(() => _lobby.RaisePlayerJoined(new PlayerInfo(Guid.NewGuid(), "Zoe", false)));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("1 player", cut.Find("#lobby-count").TextContent.Trim());
            Assert.Equal("Zoe", Assert.Single(cut.FindAll(".player-chip")).TextContent.Trim());
        });
    }

    [Fact]
    public async Task The_same_player_announced_twice_is_listed_once()
    {
        var cut = RenderLobby();
        var player = new PlayerInfo(Guid.NewGuid(), "Zoe", false);

        await cut.InvokeAsync(() => _lobby.RaisePlayerJoined(player));
        await cut.InvokeAsync(() => _lobby.RaisePlayerJoined(player));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".player-chip")));
    }

    [Fact]
    public async Task A_connection_change_marks_the_player_as_connected()
    {
        var player = new PlayerInfo(Guid.NewGuid(), "Zoe", false);
        _lobby.Snapshot = new HostSnapshot(_sessionId, "123456", "Capitals", [player]);
        var cut = RenderLobby();
        Assert.DoesNotContain("mud-chip-color-success", cut.Find(".player-chip").ClassName);

        await cut.InvokeAsync(() => _lobby.RaiseConnection(player.PlayerId, true));

        cut.WaitForAssertion(() => Assert.Contains("mud-chip-color-success", cut.Find(".player-chip").ClassName));
    }

    private static QuestionForPlayer Question(int index = 0) =>
        new(index, 2, "Capital of France?", ["Paris", "Lyon", "Nice", "Lille"], 20, 20_000);

    [Fact]
    public async Task Start_is_disabled_until_a_player_joins_and_then_starts_the_game()
    {
        var cut = RenderLobby();
        Assert.True(cut.Find("#start-game").HasAttribute("disabled"));

        await cut.InvokeAsync(() => _lobby.RaisePlayerJoined(new PlayerInfo(Guid.NewGuid(), "Zoe", false)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("#start-game").HasAttribute("disabled")));
        await cut.Find("#start-game").ClickAsync();

        Assert.Equal(1, _lobby.StartCalls);
    }

    [Fact]
    public async Task A_rejected_start_shows_the_servers_reason()
    {
        _lobby.StartError = new Microsoft.AspNetCore.SignalR.HubException("Wait for at least one player.");
        var cut = RenderLobby();
        await cut.InvokeAsync(() => _lobby.RaisePlayerJoined(new PlayerInfo(Guid.NewGuid(), "Zoe", false)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("#start-game").HasAttribute("disabled")));

        await cut.Find("#start-game").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains("at least one player", cut.Markup));
    }

    [Fact]
    public async Task The_question_screen_shows_options_and_a_live_answer_count_but_no_answer()
    {
        var cut = RenderLobby();
        await cut.InvokeAsync(() => _lobby.RaisePlayerJoined(new PlayerInfo(Guid.NewGuid(), "Zoe", false)));

        await cut.InvokeAsync(() => _lobby.RaiseQuestion(Question()));
        await cut.InvokeAsync(() => _lobby.RaiseAnswerCount(1, 1));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Capital of France?", cut.Find("#question-text").TextContent.Trim());
            Assert.Equal(4, cut.FindAll(".question-option").Count);
            Assert.Equal("1 of 1 answered", cut.Find("#answer-count").TextContent.Trim());
        });
        Assert.Empty(cut.FindAll(".result-row"));
    }

    [Fact]
    public async Task The_results_screen_marks_the_correct_option_and_advances_on_next()
    {
        var cut = RenderLobby();
        await cut.InvokeAsync(() => _lobby.RaiseQuestion(Question()));
        await cut.InvokeAsync(() => _lobby.RaiseEnded(
            new QuestionResults(0, 2, 0, [3, 1, 0, 0], [new Standing(1, "Zoe", 1000)], false)));

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".result-row").Count));
        Assert.Equal(["yes", "no", "no", "no"], cut.FindAll(".result-row").Select(r => r.GetAttribute("data-correct")!));
        Assert.Contains("Zoe", cut.Find("#leaderboard").TextContent);
        Assert.Contains("Next question", cut.Find("#next-question").TextContent);

        await cut.Find("#next-question").ClickAsync();
        Assert.Equal(1, _lobby.NextCalls);
    }

    [Fact]
    public async Task The_last_question_offers_the_final_results_and_the_end_shows_the_podium()
    {
        var cut = RenderLobby();
        await cut.InvokeAsync(() => _lobby.RaiseQuestion(Question(1)));
        await cut.InvokeAsync(() => _lobby.RaiseEnded(new QuestionResults(1, 2, 1, [0, 1, 0, 0], [new Standing(1, "Zoe", 1500)], true)));
        cut.WaitForAssertion(() => Assert.Contains("Show final results", cut.Find("#next-question").TextContent));

        await cut.InvokeAsync(() => _lobby.RaiseFinished(new FinalResults([new Standing(1, "Zoe", 1500), new Standing(2, "Max", 900)], 2)));

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("#finished"));
            Assert.Equal(2, cut.FindAll("#podium tr").Count);
        });
    }

    [Fact]
    public void Shows_an_error_when_the_session_cannot_be_opened()
    {
        _lobby.ConnectError = new InvalidOperationException("nope");

        var cut = RenderLobby();

        Assert.Contains("not available", cut.Find("[role=alert]").TextContent);
        Assert.Empty(cut.FindAll("#session-pin"));
    }

    [Fact]
    public async Task Cancellation_sends_the_host_back_to_the_quiz_list()
    {
        var cut = RenderLobby();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        await cut.InvokeAsync(_lobby.RaiseCancelled);

        Assert.EndsWith("/host", navigation.Uri);
    }

    private static QuestionForPlayer OpenQuestion => new(0, 2, "Capital of France?", ["Paris", "Lyon", "Nice", "Lille"], 20, 12_000);

    [Fact]
    public async Task A_dropped_connection_shows_a_banner_until_the_snapshot_arrives()
    {
        var cut = RenderLobby();

        await cut.InvokeAsync(_lobby.RaiseReconnecting);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#reconnecting")));

        await cut.InvokeAsync(() => _lobby.RaiseResumed(new HostSnapshot(_sessionId, "654321", "Quiz", [new PlayerInfo(Guid.NewGuid(), "Ann", true)])));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#reconnecting"));
            Assert.Equal("1 player", cut.Find("#lobby-count").TextContent.Trim());
        });
    }

    [Fact]
    public void A_reloaded_host_lands_on_the_open_question_with_the_answer_count()
    {
        _lobby.Snapshot = new HostSnapshot(
            _sessionId, "654321", "Quiz", [new PlayerInfo(Guid.NewGuid(), "Ann", true), new PlayerInfo(Guid.NewGuid(), "Bob", true)],
            SessionPhase.Question, OpenQuestion, Answered: 1);

        var cut = RenderLobby();

        Assert.Equal("Capital of France?", cut.Find("#question-text").TextContent.Trim());
        Assert.Equal("1 of 2 answered", cut.Find("#answer-count").TextContent.Trim());
    }

    [Fact]
    public async Task Resuming_at_the_results_shows_the_leaderboard_and_the_next_button()
    {
        var cut = RenderLobby();
        var results = new QuestionResults(0, 2, 0, [1, 0, 0, 0], [new Standing(1, "Ann", 990)], false);

        await cut.InvokeAsync(() => _lobby.RaiseResumed(new HostSnapshot(
            _sessionId, "654321", "Quiz", [new PlayerInfo(Guid.NewGuid(), "Ann", true)], SessionPhase.Results, OpenQuestion, Results: results)));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("#leaderboard tr"));
            Assert.NotEmpty(cut.FindAll("#next-question"));
        });
    }

    [Fact]
    public async Task Giving_up_on_the_connection_tells_the_host_to_reload()
    {
        var cut = RenderLobby();

        await cut.InvokeAsync(_lobby.RaiseReconnecting);
        await cut.InvokeAsync(_lobby.RaiseConnectionLost);

        cut.WaitForAssertion(() => Assert.Contains("Reload", cut.Markup));
    }

    private sealed class FakeHostSessionClient : IHostSessionClient
    {
        public HostSnapshot? Snapshot { get; set; }
        public Exception? ConnectError { get; set; }

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

        public int StartCalls { get; private set; }
        public int NextCalls { get; private set; }
        public Exception? StartError { get; set; }

        public Task StartGameAsync(Guid sessionId, CancellationToken ct = default)
        {
            StartCalls++;
            return StartError is null ? Task.CompletedTask : Task.FromException(StartError);
        }

        public Task NextQuestionAsync(Guid sessionId, CancellationToken ct = default)
        {
            NextCalls++;
            return Task.CompletedTask;
        }

        public void RaiseQuestion(QuestionForPlayer q) => QuestionStarted?.Invoke(q);
        public void RaiseAnswerCount(int a, int t) => AnswerCount?.Invoke(a, t);
        public void RaiseEnded(QuestionResults r) => QuestionEnded?.Invoke(r);
        public void RaiseFinished(FinalResults f) => Finished?.Invoke(f);

        public Task<HostSnapshot> ConnectAsync(Guid sessionId, CancellationToken ct = default) =>
            ConnectError is not null
                ? Task.FromException<HostSnapshot>(ConnectError)
                : Task.FromResult(Snapshot ?? new HostSnapshot(sessionId, "654321", "Quiz", []));

        public void RaisePlayerJoined(PlayerInfo p) => PlayerJoined?.Invoke(p);
        public void RaiseConnection(Guid id, bool connected) => PlayerConnectionChanged?.Invoke(id, connected);
        public void RaiseCancelled() => Cancelled?.Invoke();
        public void RaiseReconnecting() => Reconnecting?.Invoke();
        public void RaiseResumed(HostSnapshot s) => Resumed?.Invoke(s);
        public void RaiseConnectionLost() => ConnectionLost?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
