using Bunit;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Client.Lobby;
using LiveQuiz.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace LiveQuiz.Web.Tests;

public sealed class PlayScreenTests : BunitContext
{
    private readonly FakePlayerSessionClient _client = new();

    public PlayScreenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IPlayerSessionClient>(_client);
    }

    private IRenderedComponent<Play> RenderPlay() => Render<Play>();

    private static void Submit(IRenderedComponent<Play> cut, string pin, string nickname)
    {
        cut.Find("#pin").Input(pin);
        cut.Find("#nickname").Input(nickname);
        cut.Find("form").Submit();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("")]
    public void A_malformed_pin_is_rejected_without_calling_the_server(string pin)
    {
        var cut = RenderPlay();

        Submit(cut, pin, "Sam");

        Assert.Contains("6 digits", cut.Find("#pin-error").TextContent);
        Assert.Equal(0, _client.JoinCalls);
    }

    [Theory]
    [InlineData("", "Enter a nickname")]
    [InlineData("x", "at least")]
    [InlineData("shit head", "different nickname")]
    public void An_invalid_nickname_shows_the_policy_message(string nickname, string expected)
    {
        var cut = RenderPlay();

        Submit(cut, "123456", nickname);

        Assert.Contains(expected, cut.Find("#nickname-error").TextContent);
        Assert.Equal(0, _client.JoinCalls);
    }

    [Fact]
    public void A_server_side_nickname_conflict_is_shown_next_to_the_nickname()
    {
        _client.Result = new JoinAttempt(null, "That nickname is taken or the game is full.", NicknameProblem: true);
        var cut = RenderPlay();

        Submit(cut, "123456", "Sam");

        cut.WaitForAssertion(() => Assert.Contains("taken", cut.Find("#nickname-error").TextContent));
        Assert.Empty(cut.FindAll("#welcome"));
    }

    [Fact]
    public void An_unknown_pin_is_reported_and_the_form_stays()
    {
        _client.Result = new JoinAttempt(null, "No open game with that PIN.");
        var cut = RenderPlay();

        Submit(cut, "123456", "Sam");

        cut.WaitForAssertion(() => Assert.Contains("No open game", cut.Markup));
        Assert.NotEmpty(cut.FindAll("#pin"));
    }

    [Fact]
    public void Joining_shows_the_lobby_and_live_count_updates()
    {
        _client.Result = new JoinAttempt(new PlayerGameState(Guid.NewGuid(), "Sam", 1));
        var cut = RenderPlay();

        Submit(cut, "123456", "  Sam ");

        cut.WaitForAssertion(() => Assert.Contains("Sam", cut.Find("#welcome").TextContent));
        Assert.Equal("Sam", _client.LastNickname);
        Assert.Equal("1 player in the lobby", cut.Find("#player-count").TextContent.Trim());

        cut.InvokeAsync(() => _client.RaiseCount(7));
        cut.WaitForAssertion(() => Assert.Equal("7 players in the lobby", cut.Find("#player-count").TextContent.Trim()));
    }

    [Fact]
    public void A_saved_identity_puts_the_player_straight_back_in_the_lobby()
    {
        _client.Restored = new PlayerGameState(Guid.NewGuid(), "Sam", 4);

        var cut = RenderPlay();

        cut.WaitForAssertion(() => Assert.Equal("4 players in the lobby", cut.Find("#player-count").TextContent.Trim()));
    }

    [Fact]
    public void When_the_host_cancels_the_player_is_told()
    {
        _client.Result = new JoinAttempt(new PlayerGameState(Guid.NewGuid(), "Sam", 1));
        var cut = RenderPlay();
        Submit(cut, "123456", "Sam");
        cut.WaitForElement("#welcome");

        cut.InvokeAsync(_client.RaiseCancelled);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#cancelled")));
    }

    private static readonly QuestionForPlayer Capital =
        new(0, 2, "Capital of France?", ["Paris", "Lyon", "Nice", "Lille"], 20, 12_000);

    [Fact]
    public async Task A_dropped_connection_shows_a_banner_that_goes_away_when_the_game_resumes()
    {
        var cut = await InQuestionAsync();

        await cut.InvokeAsync(_client.RaiseReconnecting);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#reconnecting")));

        await cut.InvokeAsync(() => _client.RaiseResumed(new PlayerGameState(Guid.NewGuid(), "Sam", 2, SessionPhase.Question, Capital)));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#reconnecting"));
            Assert.Equal("Capital of France?", cut.Find("#question-text").TextContent.Trim());
        });
    }

    [Fact]
    public void A_reloaded_page_lands_on_the_open_question_with_the_saved_choice()
    {
        _client.Restored = new PlayerGameState(Guid.NewGuid(), "Sam", 2, SessionPhase.Question, Capital, ChosenIndex: 2);

        var cut = RenderPlay();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Question 1 of 2", cut.Find("#question-progress").TextContent.Trim());
            Assert.Contains("chosen", cut.Find("#option-2").ClassName);
            Assert.All(cut.FindAll(".option"), o => Assert.True(o.HasAttribute("disabled")));
            Assert.Equal("Answer locked in!", cut.Find("#answer-status").TextContent.Trim());
        });
    }

    [Fact]
    public async Task Resuming_at_the_results_shows_the_outcome_and_the_correct_answer()
    {
        var cut = await InQuestionAsync();
        var state = new PlayerGameState(
            Guid.NewGuid(), "Sam", 2, SessionPhase.Results, Capital,
            Results: new QuestionResults(0, 2, 0, [1, 0, 0, 0], [new Standing(1, "Sam", 980)], false),
            Result: new PlayerQuestionResult(0, true, 0, true, 980, 980, 1, 2));

        await cut.InvokeAsync(() => _client.RaiseResumed(state));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Correct!", cut.Find("#result-headline").TextContent.Trim());
            Assert.Contains("Paris", cut.Find("#correct-answer").TextContent);
            Assert.Empty(cut.FindAll("#question-text"));
        });
    }

    [Fact]
    public async Task Resuming_after_the_game_ended_shows_the_podium()
    {
        var cut = await InQuestionAsync();
        var state = new PlayerGameState(
            Guid.NewGuid(), "Sam", 2, SessionPhase.Finished,
            Result: new PlayerQuestionResult(1, true, 1, true, 900, 1830, 2, 2),
            Final: new FinalResults([new Standing(1, "Ann", 1900), new Standing(2, "Sam", 1830)], 2));

        await cut.InvokeAsync(() => _client.RaiseResumed(state));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("#2", cut.Find("#final-rank").TextContent);
            Assert.Equal(2, cut.FindAll("#podium li").Count);
        });
    }

    [Fact]
    public async Task Giving_up_on_the_connection_tells_the_player_to_reload()
    {
        var cut = await InQuestionAsync();

        await cut.InvokeAsync(_client.RaiseReconnecting);
        await cut.InvokeAsync(_client.RaiseConnectionLost);

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#reconnecting"));
            Assert.Contains("Reload", cut.Find("form").TextContent);
        });
    }

    [Fact]
    public void Too_many_wrong_pins_shows_the_wait_message()
    {
        _client.Result = new JoinAttempt(null, "Too many attempts. Try again in 42 seconds.");
        var cut = RenderPlay();

        Submit(cut, "123456", "Sam");

        cut.WaitForAssertion(() => Assert.Contains("Too many attempts", cut.Find("form").TextContent));
    }

    [Fact]
    public async Task A_rate_limited_answer_says_to_slow_down()
    {
        var cut = await InQuestionAsync();
        _client.AnswerResult = AnswerStatus.RateLimited;

        cut.Find("#option-1").Click();

        cut.WaitForAssertion(() => Assert.Contains("Slow down", cut.Find("#answer-status").TextContent));
    }

    private async Task<IRenderedComponent<Play>> InQuestionAsync()
    {
        _client.Result = new JoinAttempt(new PlayerGameState(Guid.NewGuid(), "Sam", 2));
        var cut = RenderPlay();
        Submit(cut, "123456", "Sam");
        cut.WaitForElement("#welcome");
        await cut.InvokeAsync(() => _client.RaiseQuestion(
            new QuestionForPlayer(0, 2, "Capital of France?", ["Paris", "Lyon", "Nice", "Lille"], 20, 20_000)));
        cut.WaitForElement("#question-text");
        return cut;
    }

    [Fact]
    public async Task A_question_shows_four_options_and_no_correct_answer()
    {
        var cut = await InQuestionAsync();

        Assert.Equal(4, cut.FindAll("button.option").Count);
        Assert.Empty(cut.FindAll("#correct-answer"));
        Assert.DoesNotContain("Correct", cut.Markup);
    }

    [Fact]
    public async Task Choosing_an_option_sends_one_answer_and_locks_the_buttons()
    {
        var cut = await InQuestionAsync();

        cut.Find("#option-1").Click();

        cut.WaitForAssertion(() => Assert.Contains("locked in", cut.Find("#answer-status").TextContent));
        Assert.Equal([(0, 1)], _client.Answers);
        Assert.All(cut.FindAll("button.option"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public async Task A_too_late_answer_is_explained()
    {
        _client.AnswerResult = AnswerStatus.TooLate;
        var cut = await InQuestionAsync();

        await cut.Find("#option-0").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains("Too late", cut.Find("#answer-status").TextContent));
    }

    [Fact]
    public async Task After_the_question_the_player_sees_their_outcome_and_the_correct_answer()
    {
        var cut = await InQuestionAsync();
        await cut.Find("#option-0").ClickAsync();

        await cut.InvokeAsync(() => _client.RaiseEnded(new QuestionResults(0, 2, 0, [2, 0, 0, 0], [new Standing(1, "Sam", 930)], false)));
        await cut.InvokeAsync(() => _client.RaiseResult(new PlayerQuestionResult(0, true, 0, true, 930, 930, 1, 2)));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Correct!", cut.Find("#result-headline").TextContent.Trim());
            Assert.Contains("+930", cut.Find("#result-points").TextContent);
            Assert.Contains("Rank 1 of 2", cut.Find("#result-rank").TextContent);
            Assert.Contains("Paris", cut.Find("#correct-answer").TextContent);
        });
    }

    [Fact]
    public async Task A_missed_question_says_no_answer()
    {
        var cut = await InQuestionAsync();

        await cut.InvokeAsync(() => _client.RaiseEnded(new QuestionResults(0, 2, 0, [0, 0, 0, 0], [], false)));
        await cut.InvokeAsync(() => _client.RaiseResult(new PlayerQuestionResult(0, false, null, false, 0, 0, 1, 2)));

        cut.WaitForAssertion(() => Assert.Equal("No answer", cut.Find("#result-headline").TextContent.Trim()));
    }

    [Fact]
    public async Task The_final_screen_shows_the_players_rank_and_the_podium()
    {
        var cut = await InQuestionAsync();
        await cut.InvokeAsync(() => _client.RaiseResult(new PlayerQuestionResult(1, true, 1, true, 900, 1830, 2, 2)));

        await cut.InvokeAsync(() => _client.RaiseFinished(new FinalResults([new Standing(1, "Ann", 1900), new Standing(2, "Sam", 1830)], 2)));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("#2", cut.Find("#final-rank").TextContent);
            Assert.Equal(2, cut.FindAll("#podium li").Count);
        });
    }

    private sealed class FakePlayerSessionClient : IPlayerSessionClient
    {
        public JoinAttempt Result { get; set; } = new(null, "unset");
        public PlayerGameState? Restored { get; set; }
        public int JoinCalls { get; private set; }
        public string? LastNickname { get; private set; }

        public event Action<int>? CountChanged;
        public event Action? Cancelled;
        public event Action<QuestionForPlayer>? QuestionStarted;
        public event Action<QuestionResults>? QuestionEnded;
        public event Action<PlayerQuestionResult>? ResultReceived;
        public event Action<FinalResults>? Finished;
        public event Action? Reconnecting;
        public event Action<PlayerGameState>? Resumed;
        public event Action? ConnectionLost;

        public List<(int Question, int Option)> Answers { get; } = [];
        public AnswerStatus AnswerResult { get; set; } = AnswerStatus.Accepted;

        public Task<AnswerStatus> SubmitAnswerAsync(int questionIndex, int optionIndex, CancellationToken ct = default)
        {
            Answers.Add((questionIndex, optionIndex));
            return Task.FromResult(AnswerResult);
        }

        public void RaiseQuestion(QuestionForPlayer q) => QuestionStarted?.Invoke(q);
        public void RaiseEnded(QuestionResults r) => QuestionEnded?.Invoke(r);
        public void RaiseResult(PlayerQuestionResult r) => ResultReceived?.Invoke(r);
        public void RaiseFinished(FinalResults f) => Finished?.Invoke(f);

        public Task<JoinAttempt> JoinAsync(string pin, string nickname, CancellationToken ct = default)
        {
            JoinCalls++;
            LastNickname = nickname;
            return Task.FromResult(Result);
        }

        public Task<PlayerGameState?> RestoreAsync(CancellationToken ct = default) => Task.FromResult(Restored);

        public void RaiseCount(int count) => CountChanged?.Invoke(count);
        public void RaiseCancelled() => Cancelled?.Invoke();
        public void RaiseReconnecting() => Reconnecting?.Invoke();
        public void RaiseResumed(PlayerGameState s) => Resumed?.Invoke(s);
        public void RaiseConnectionLost() => ConnectionLost?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
