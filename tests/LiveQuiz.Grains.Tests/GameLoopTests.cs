using LiveQuiz.Contracts.Game;
using LiveQuiz.Grains.Pins;
using LiveQuiz.Grains.Players;
using LiveQuiz.Grains.Sessions;

namespace LiveQuiz.Grains.Tests;

[Collection(ClusterCollection.Name)]
public class GameLoopTests(ClusterFixture fixture)
{
    private static FakeClock Clock => new();

    private sealed class FakeClock
    {
        public void Advance(TimeSpan by) => ClusterFixture.Time.Advance(by);
    }

    private sealed record Game(Guid Id, ISessionGrain Session, string Pin, JoinResult[] Players);

    private async Task<Game> NewGame(int players = 2, bool start = true)
    {
        var id = Guid.CreateVersion7();
        var session = fixture.Grains.GetGrain<ISessionGrain>(id);
        var pin = await session.Open("owner", Guid.NewGuid(), "Quiz", GameFixtures.Questions());
        var joined = new List<JoinResult>();
        for (var i = 0; i < players; i++)
            joined.Add(await session.Join($"Player {i}"));
        if (start)
            Assert.Equal(GameCommandResult.Ok, await session.Start());
        return new Game(id, session, pin, [.. joined]);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "condition not met in time");
    }

    private static IReadOnlyList<T> Events<T>(Guid id, string kind) => RecordingNotifier.Instance.Of<T>(id, kind);

    private static PlayerQuestionResult ResultOf(Guid id, Guid playerId, int question) =>
        Events<(Guid Player, PlayerQuestionResult Result)>(id, nameof(ISessionNotifier.PlayerResult))
            .Single(e => e.Player == playerId && e.Result.QuestionIndex == question).Result;

    [Fact]
    public async Task Start_needs_players_and_a_lobby()
    {
        var empty = await NewGame(players: 0, start: false);
        Assert.Equal(GameCommandResult.NoPlayers, await empty.Session.Start());

        var game = await NewGame();
        Assert.Equal(GameCommandResult.InvalidState, await game.Session.Start());
        Assert.Equal(GameCommandResult.InvalidState, await game.Session.Next());
    }

    [Fact]
    public async Task Starting_pushes_the_first_question_without_the_correct_answer()
    {
        var game = await NewGame();

        var question = Assert.Single(Events<QuestionForPlayer>(game.Id, nameof(ISessionNotifier.QuestionStarted)));
        Assert.Equal(0, question.Index);
        Assert.Equal(2, question.Total);
        Assert.Equal(["Paris", "Lyon", "Nice", "Lille"], question.Options);
        Assert.Equal(10_000, question.RemainingMs);
        Assert.DoesNotContain(typeof(QuestionForPlayer).GetProperties(), p => p.Name.Contains("Correct", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)));
    }

    [Fact]
    public async Task Players_cannot_join_once_the_game_started()
    {
        var game = await NewGame();

        Assert.Equal(JoinStatus.NotJoinable, (await game.Session.Join("Late")).Status);
    }

    [Fact]
    public async Task Points_depend_on_server_measured_elapsed_time()
    {
        var game = await NewGame(players: 3);
        var (a, b, c) = (game.Players[0], game.Players[1], game.Players[2]);

        Assert.Equal(AnswerStatus.Accepted, await game.Session.SubmitAnswer(a.PlayerId, 0, 0));
        Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(AnswerStatus.Accepted, await game.Session.SubmitAnswer(b.PlayerId, 0, 0));
        Assert.Equal(AnswerStatus.Accepted, await game.Session.SubmitAnswer(c.PlayerId, 0, 3));

        await Eventually(() => Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Count == 1);
        Assert.Equal(1000, ResultOf(game.Id, a.PlayerId, 0).Points);
        Assert.Equal(750, ResultOf(game.Id, b.PlayerId, 0).Points);
        var wrong = ResultOf(game.Id, c.PlayerId, 0);
        Assert.Equal((0, false, 3), (wrong.Points, wrong.Correct, wrong.ChosenIndex ?? -1));
    }

    [Fact]
    public async Task The_question_closes_early_when_everyone_has_answered_and_reveals_the_answer()
    {
        var game = await NewGame();

        await game.Session.SubmitAnswer(game.Players[0].PlayerId, 0, 0);
        await game.Session.SubmitAnswer(game.Players[1].PlayerId, 0, 1);

        var results = Assert.Single(Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)));
        Assert.Equal(0, results.CorrectIndex);
        Assert.Equal([1, 1, 0, 0], results.OptionCounts);
        Assert.False(results.IsLast);
        Assert.Equal(SessionStatus.QuestionResults, (await game.Session.GetInfo()).Status);
    }

    [Fact]
    public async Task The_question_closes_by_itself_at_the_deadline_plus_grace()
    {
        var game = await NewGame();

        Clock.Advance(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(200));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)));

        Clock.Advance(TimeSpan.FromMilliseconds(100));
        await Eventually(() => Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Count == 1);
        var results = Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Single();
        Assert.Equal([0, 0, 0, 0], results.OptionCounts);
        Assert.All(game.Players, p => Assert.False(ResultOf(game.Id, p.PlayerId, 0).Answered));
    }

    [Fact]
    public async Task An_answer_inside_the_grace_window_counts_with_minimum_points()
    {
        var game = await NewGame();
        Clock.Advance(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(200));

        var status = await game.Session.SubmitAnswer(game.Players[0].PlayerId, 0, 0);

        Assert.Equal(AnswerStatus.Accepted, status);
        Clock.Advance(TimeSpan.FromMilliseconds(100));
        await Eventually(() => Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Count == 1);
        Assert.Equal(500, ResultOf(game.Id, game.Players[0].PlayerId, 0).Points);
    }

    [Fact]
    public async Task An_answer_after_the_grace_window_is_too_late()
    {
        var game = await NewGame();
        Clock.Advance(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(300));
        await Eventually(() => Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Count == 1);

        Assert.Equal(AnswerStatus.TooLate, await game.Session.SubmitAnswer(game.Players[0].PlayerId, 0, 0));
    }

    [Fact]
    public async Task Answers_are_idempotent_and_validated()
    {
        var game = await NewGame(players: 3);
        var p = game.Players[0].PlayerId;

        Assert.Equal(AnswerStatus.Accepted, await game.Session.SubmitAnswer(p, 0, 1));
        Assert.Equal(AnswerStatus.Duplicate, await game.Session.SubmitAnswer(p, 0, 0));
        Assert.Equal(AnswerStatus.InvalidOption, await game.Session.SubmitAnswer(game.Players[1].PlayerId, 0, 9));
        Assert.Equal(AnswerStatus.InvalidOption, await game.Session.SubmitAnswer(game.Players[1].PlayerId, 0, -1));
        Assert.Equal(AnswerStatus.WrongQuestion, await game.Session.SubmitAnswer(game.Players[1].PlayerId, 1, 0));
        Assert.Equal(AnswerStatus.NotInGame, await game.Session.SubmitAnswer(Guid.NewGuid(), 0, 0));

        Clock.Advance(TimeSpan.FromSeconds(11));
        await Eventually(() => Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Count == 1);
        Assert.False(ResultOf(game.Id, p, 0).Correct, "the first answer (wrong) must stand");
    }

    [Fact]
    public async Task A_full_game_ranks_players_releases_the_pin_and_is_archived_once()
    {
        var game = await NewGame(players: 3);
        var (a, b, c) = (game.Players[0], game.Players[1], game.Players[2]);

        // Question 1: a and b correct at the same instant (tie), c wrong.
        await game.Session.SubmitAnswer(a.PlayerId, 0, 0);
        await game.Session.SubmitAnswer(b.PlayerId, 0, 0);
        await game.Session.SubmitAnswer(c.PlayerId, 0, 2);
        Assert.Equal(GameCommandResult.Ok, await game.Session.Next());

        // Question 2: only a is correct, a answers a little later.
        Clock.Advance(TimeSpan.FromSeconds(2));
        await game.Session.SubmitAnswer(a.PlayerId, 1, 1);
        await game.Session.SubmitAnswer(b.PlayerId, 1, 0);
        await game.Session.SubmitAnswer(c.PlayerId, 1, 0);
        var last = Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Last();
        Assert.True(last.IsLast);
        Assert.Equal(GameCommandResult.Ok, await game.Session.Next());

        var final = Assert.Single(Events<FinalResults>(game.Id, nameof(ISessionNotifier.SessionFinished)));
        Assert.Equal(["Player 0", "Player 1", "Player 2"], final.Top.Select(s => s.Nickname));
        Assert.Equal([1, 2, 3], final.Top.Select(s => s.Rank));
        Assert.Equal([1900, 1000, 0], final.Top.Select(s => s.Points));

        Assert.Equal(SessionStatus.Finished, (await game.Session.GetInfo()).Status);
        Assert.Null(await fixture.Grains.GetGrain<IPinRegistryGrain>(game.Pin).Resolve());
        var summary = Assert.Single(RecordingArchive.Instance.For(game.Id));
        Assert.Equal(3, summary.Players.Count);
        Assert.Equal(2, summary.Questions.Count);
        Assert.Equal([2, 0, 1, 0], summary.Questions[0].OptionCounts);
        Assert.Equal(GameCommandResult.InvalidState, await game.Session.Next());
    }

    [Fact]
    public async Task Equal_points_share_a_rank_and_the_next_rank_is_skipped()
    {
        var game = await NewGame(players: 3);
        await game.Session.SubmitAnswer(game.Players[0].PlayerId, 0, 0);
        await game.Session.SubmitAnswer(game.Players[1].PlayerId, 0, 0);
        await game.Session.SubmitAnswer(game.Players[2].PlayerId, 0, 1);

        var results = Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)).Single();

        Assert.Equal([1, 1, 3], results.Top.Select(s => s.Rank));
    }

    [Fact]
    public async Task Cancelling_mid_game_stops_the_clock_and_frees_the_pin()
    {
        var game = await NewGame();

        await game.Session.Cancel();
        Clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(SessionStatus.Cancelled, (await game.Session.GetInfo()).Status);
        Assert.Empty(Events<QuestionResults>(game.Id, nameof(ISessionNotifier.QuestionEnded)));
        Assert.Null(await fixture.Grains.GetGrain<IPinRegistryGrain>(game.Pin).Resolve());
        Assert.NotEqual(AnswerStatus.Accepted, await game.Session.SubmitAnswer(game.Players[0].PlayerId, 0, 0));
    }
}
