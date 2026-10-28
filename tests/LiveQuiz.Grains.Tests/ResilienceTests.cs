using LiveQuiz.Contracts.Game;
using LiveQuiz.Grains.Pins;
using LiveQuiz.Grains.Sessions;
using Orleans.Core.Internal;

namespace LiveQuiz.Grains.Tests;

/// <summary>Reconnecting clients, restarts, abandoned sessions and archive failures (ADR 0013).</summary>
[Collection(ClusterCollection.Name)]
public class ResilienceTests(ClusterFixture fixture)
{
    private sealed record Game(Guid Id, ISessionGrain Session, JoinResult[] Players);

    private async Task<Game> NewGame(int players = 2, bool start = true)
    {
        var id = Guid.CreateVersion7();
        var session = fixture.Grains.GetGrain<ISessionGrain>(id);
        await session.Open("owner", Guid.NewGuid(), "Quiz", GameFixtures.Questions());
        var joined = new List<JoinResult>();
        for (var i = 0; i < players; i++)
            joined.Add(await session.Join($"Player {i}"));
        if (start)
            Assert.Equal(GameCommandResult.Ok, await session.Start());
        return new Game(id, session, [.. joined]);
    }

    /// <summary>Drops the activation from memory, like a silo restart does; the next call loads the stored state.</summary>
    private async Task Restart(Game game) =>
        await fixture.Grains.GetGrain<ISessionGrain>(game.Id).AsReference<IGrainManagementExtension>().DeactivateOnIdle();

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "condition not met in time");
    }

    private static Task AdvanceUntil(Func<bool> condition, TimeSpan step) => AdvanceUntil(() => Task.FromResult(condition()), step);

    private static async Task AdvanceUntil(Func<Task<bool>> condition, TimeSpan step)
    {
        for (var i = 0; i < 30 && !await condition(); i++)
        {
            ClusterFixture.Time.Advance(step);
            await Task.Delay(30, TestContext.Current.CancellationToken);
        }
        Assert.True(await condition(), "condition not met while advancing the clock");
    }

    [Fact]
    public async Task A_player_view_follows_the_game_through_every_phase()
    {
        var game = await NewGame(players: 2, start: false);
        var ann = game.Players[0].PlayerId;
        var bob = game.Players[1].PlayerId;

        var lobby = (await game.Session.GetPlayerView(ann))!;
        Assert.Equal(SessionStatus.Lobby, lobby.Status);
        Assert.Equal("Player 0", lobby.Nickname);
        Assert.Equal(2, lobby.PlayerCount);
        Assert.Null(lobby.Question);

        await game.Session.Start();
        ClusterFixture.Time.Advance(TimeSpan.FromSeconds(4));
        var question = (await game.Session.GetPlayerView(ann))!;
        Assert.Equal(SessionStatus.QuestionActive, question.Status);
        Assert.Equal(0, question.Question!.Index);
        Assert.Equal(6000, question.Question.RemainingMs);
        Assert.Null(question.ChosenIndex);

        await game.Session.SubmitAnswer(ann, 0, 0);
        Assert.Equal(0, (await game.Session.GetPlayerView(ann))!.ChosenIndex);
        Assert.Null((await game.Session.GetPlayerView(bob))!.ChosenIndex);
        Assert.Equal(1, (await game.Session.GetHostView()).Answered);

        await game.Session.SubmitAnswer(bob, 0, 1);
        var results = (await game.Session.GetPlayerView(ann))!;
        Assert.Equal(SessionStatus.QuestionResults, results.Status);
        Assert.Equal(0, results.Results!.CorrectIndex);
        Assert.True(results.MyResult!.Correct);
        Assert.Equal(1, results.MyResult.Rank);
        Assert.False((await game.Session.GetPlayerView(bob))!.MyResult!.Correct);

        await game.Session.Next();
        await game.Session.SubmitAnswer(ann, 1, 1);
        await game.Session.SubmitAnswer(bob, 1, 1);
        await game.Session.Next();
        var finished = (await game.Session.GetPlayerView(ann))!;
        Assert.Equal(SessionStatus.Finished, finished.Status);
        Assert.Equal(2, finished.Final!.Length);
        Assert.Equal(1, finished.MyResult!.Rank);
    }

    [Fact]
    public async Task A_stranger_has_no_player_view()
    {
        var game = await NewGame();

        Assert.Null(await game.Session.GetPlayerView(Guid.NewGuid()));
    }

    [Fact]
    public async Task Results_survive_a_restart()
    {
        var game = await NewGame();
        var ann = game.Players[0].PlayerId;
        await game.Session.SubmitAnswer(ann, 0, 0);
        await game.Session.SubmitAnswer(game.Players[1].PlayerId, 0, 2);
        var before = (await game.Session.GetPlayerView(ann))!;

        await Restart(game);
        var after = (await game.Session.GetPlayerView(ann))!;

        Assert.Equal(SessionStatus.QuestionResults, after.Status);
        Assert.Equal(before.MyResult, after.MyResult);
        Assert.Equal(before.Results!.Top, after.Results!.Top);
        Assert.Equal(before.Results.OptionCounts, after.Results.OptionCounts);
    }

    [Fact]
    public async Task A_restart_mid_question_starts_the_question_over_with_its_full_time()
    {
        var game = await NewGame();
        var ann = game.Players[0].PlayerId;
        Assert.Equal(AnswerStatus.Accepted, await game.Session.SubmitAnswer(ann, 0, 0));
        ClusterFixture.Time.Advance(TimeSpan.FromSeconds(6));

        await Restart(game);
        var view = await game.Session.GetHostView();

        Assert.Equal(SessionStatus.QuestionActive, view.Status);
        Assert.Equal(10_000, view.Question!.RemainingMs);
        Assert.Equal(0, view.Answered);
        Assert.Equal(2, RecordingNotifier.Instance.Of<QuestionForPlayer>(game.Id, nameof(ISessionNotifier.QuestionStarted)).Count);
        // The lost answer can be given again, and the restarted question still ends by its own timer.
        Assert.Equal(AnswerStatus.Accepted, await game.Session.SubmitAnswer(ann, 0, 0));
        await AdvanceUntil(() => RecordingNotifier.Instance.For(game.Id).Any(e => e.Kind == nameof(ISessionNotifier.QuestionEnded)), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_restart_in_the_lobby_keeps_players_but_marks_them_disconnected()
    {
        var game = await NewGame(start: false);
        await game.Session.SetPlayerConnected(game.Players[0].PlayerId, "c1", true);
        Assert.True((await game.Session.GetInfo()).Players.Single(p => p.PlayerId == game.Players[0].PlayerId).Connected);

        await Restart(game);
        var info = await game.Session.GetInfo();

        Assert.Equal(SessionStatus.Lobby, info.Status);
        Assert.Equal(2, info.Players.Length);
        Assert.All(info.Players, p => Assert.False(p.Connected));
    }

    [Fact]
    public async Task A_player_stays_connected_until_their_last_connection_closes()
    {
        var game = await NewGame(start: false);
        var id = game.Players[0].PlayerId;
        bool Connected(SessionInfo i) => i.Players.Single(p => p.PlayerId == id).Connected;

        await game.Session.SetPlayerConnected(id, "tab-1", true);
        await game.Session.SetPlayerConnected(id, "tab-2", true);
        await game.Session.SetPlayerConnected(id, "tab-1", false);
        Assert.True(Connected(await game.Session.GetInfo()));

        await game.Session.SetPlayerConnected(id, "tab-2", false);
        Assert.False(Connected(await game.Session.GetInfo()));

        var changes = RecordingNotifier.Instance.Of<(Guid Player, bool Connected)>(game.Id, nameof(ISessionNotifier.PlayerConnectionChanged));
        Assert.Equal([true, false], changes.Select(c => c.Connected));
    }

    [Fact]
    public async Task A_session_whose_host_never_comes_back_is_cancelled_and_frees_its_pin()
    {
        var game = await NewGame(start: false);
        var pin = (await game.Session.GetInfo()).Pin;
        await game.Session.SetHostConnected("host-1", true);
        await game.Session.SetHostConnected("host-1", false);

        ClusterFixture.Time.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(SessionStatus.Lobby, (await game.Session.GetInfo()).Status);

        await AdvanceUntil(async () => (await game.Session.GetInfo()).Status == SessionStatus.Cancelled, TimeSpan.FromMinutes(1));
        Assert.Null(await fixture.Grains.GetGrain<IPinRegistryGrain>(pin).Resolve());
        Assert.Contains(RecordingNotifier.Instance.For(game.Id), e => e.Kind == nameof(ISessionNotifier.SessionCancelled));
    }

    [Fact]
    public async Task A_host_who_returns_in_time_keeps_the_session()
    {
        var game = await NewGame(start: false);
        await game.Session.SetHostConnected("host-1", true);
        await game.Session.SetHostConnected("host-1", false);
        ClusterFixture.Time.Advance(TimeSpan.FromMinutes(9));

        await game.Session.SetHostConnected("host-2", true);
        ClusterFixture.Time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(SessionStatus.Lobby, (await game.Session.GetInfo()).Status);
    }

    [Fact]
    public async Task A_host_with_a_second_connection_is_not_absent_when_one_closes()
    {
        var game = await NewGame(start: false);
        await game.Session.SetHostConnected("tab-1", true);
        await game.Session.SetHostConnected("tab-2", true);
        await game.Session.SetHostConnected("tab-1", false);

        ClusterFixture.Time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(SessionStatus.Lobby, (await game.Session.GetInfo()).Status);
    }

    private async Task<Game> Finished(int failures)
    {
        var game = await NewGame(players: 1);
        RecordingArchive.Instance.FailNext(game.Id, failures);
        var p = game.Players[0].PlayerId;
        await game.Session.SubmitAnswer(p, 0, 0);
        await game.Session.Next();
        await game.Session.SubmitAnswer(p, 1, 1);
        await game.Session.Next();
        Assert.Equal(SessionStatus.Finished, (await game.Session.GetInfo()).Status);
        return game;
    }

    [Fact]
    public async Task A_failed_archive_save_is_retried_until_it_succeeds()
    {
        var game = await Finished(failures: 1);
        Assert.Empty(RecordingArchive.Instance.For(game.Id));

        await AdvanceUntil(() => RecordingArchive.Instance.For(game.Id).Count == 1, TimeSpan.FromSeconds(1));

        Assert.Equal(2, RecordingArchive.Instance.Attempts(game.Id));
    }

    [Fact]
    public async Task Archiving_gives_up_after_the_configured_attempts()
    {
        var game = await Finished(failures: 99);

        await AdvanceUntil(() => RecordingArchive.Instance.Attempts(game.Id) == 3, TimeSpan.FromSeconds(1));
        ClusterFixture.Time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(3, RecordingArchive.Instance.Attempts(game.Id));
        Assert.Empty(RecordingArchive.Instance.For(game.Id));
    }
}
