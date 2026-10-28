using System.Net;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Quizzes;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class GameFlowTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly LobbyHubTests _hub = new(factory);

    private async Task<LobbyHubTests.Probe> PlayerAsync(CreateSessionResponse session, string nickname)
    {
        var joined = await _hub.JoinAsync(session.Pin, nickname);
        var probe = await _hub.ConnectAsync();
        await probe.Connection.InvokeAsync<PlayerGameState>(
            nameof(IQuizHub.JoinAsPlayer), session.SessionId, joined.PlayerId, joined.ReconnectToken, Ct);
        return probe;
    }

    private static Task<bool> Wait(Func<bool> condition) =>
        LobbyHubTests.Eventually(() => condition() ? true : null as bool?);

    [Fact]
    public async Task A_complete_game_runs_over_the_hub_and_lands_in_history()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost); // 3 questions, correct option = n % 4
        await using var host = await _hub.ConnectAsync(cookie);
        await host.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        await using var ann = await PlayerAsync(session, "Ann");
        await using var bob = await PlayerAsync(session, "Bob");

        await host.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct);

        for (var q = 0; q < 3; q++)
        {
            await Wait(() => ann.Questions.Count == q + 1 && bob.Questions.Count == q + 1 && host.Questions.Count == q + 1);
            var shown = ann.Questions.Last();
            Assert.Equal(q, shown.Index);
            Assert.Equal(4, shown.Options.Count);

            var correct = (q + 1) % 4; // QuizFactory: question n (1-based) has its correct option at n % 4
            Assert.Equal(AnswerStatus.Accepted, await ann.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), q, correct, Ct));
            // Only the first question probes duplicates: a person taps slower than the per-connection rate limit allows more of.
            if (q == 0)
                Assert.Equal(AnswerStatus.Duplicate, await ann.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), q, correct, Ct));
            Assert.Equal(AnswerStatus.Accepted, await bob.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), q, (correct + 1) % 4, Ct));

            // Everyone answered, so the question closes without waiting for the timer.
            await Wait(() => host.Results.Count == q + 1 && ann.MyResults.Count == q + 1);
            Assert.Equal(correct, host.Results.Last().CorrectIndex);
            Assert.True(ann.MyResults.Last().Correct);
            Assert.False(bob.MyResults.Last().Correct);
            Assert.Equal(q == 2, host.Results.Last().IsLast);
            Assert.Equal(1, ann.MyResults.Last().Rank);

            await host.Connection.InvokeAsync(nameof(IQuizHub.NextQuestion), session.SessionId, Ct);
        }

        await Wait(() => ann.Finals.Count == 1 && host.Finals.Count == 1);
        Assert.Equal(["Ann", "Bob"], host.Finals.Single().Top.Select(s => s.Nickname));
        Assert.True(host.Finals.Single().Top[0].Points >= 1500);

        // The record is written when the game finishes.
        var history = await Eventually(async () =>
            await httpHost.GetFromJsonAsync<List<SessionHistoryItem>>("/api/sessions/history", Ct) is { Count: > 0 } h ? h : null);
        var item = Assert.Single(history);
        Assert.Equal("Ann", item.Winner);
        Assert.Equal(2, item.PlayerCount);

        var detail = await httpHost.GetFromJsonAsync<SessionHistoryDetail>($"/api/sessions/history/{item.Id}", Ct);
        Assert.Equal(["Ann", "Bob"], detail!.Standings.Select(s => s.Nickname));
        Assert.Equal(3, detail.Questions.Count);
        Assert.All(detail.Questions, q => Assert.Equal(2, q.OptionCounts.Sum()));
    }

    [Fact]
    public async Task Players_never_receive_the_correct_answer_while_the_question_is_open()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await using var host = await _hub.ConnectAsync(cookie);
        await host.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        await using var ann = await PlayerAsync(session, "Ann");
        await using var bob = await PlayerAsync(session, "Bob");
        await host.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct);
        await Wait(() => ann.Questions.Count == 1);

        await ann.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), 0, 1, Ct);

        await Wait(() => host.Answered.Count > 0);
        Assert.Empty(ann.Results);
        Assert.Empty(bob.Results);
        Assert.Empty(ann.MyResults);
        Assert.Empty(host.Results);
    }

    [Fact]
    public async Task Only_the_owning_host_can_drive_the_game()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _1 = httpHost;
        var (httpOther, otherCookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _2 = httpOther;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await _hub.JoinAsync(session.Pin, "Ann");
        await using var other = await _hub.ConnectAsync(otherCookie);
        await using var anonymous = await _hub.ConnectAsync();
        await using var host = await _hub.ConnectAsync(cookie);

        await Assert.ThrowsAsync<HubException>(() => other.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct));
        await Assert.ThrowsAsync<HubException>(() => anonymous.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct));
        await Assert.ThrowsAsync<HubException>(() => host.Connection.InvokeAsync(nameof(IQuizHub.NextQuestion), session.SessionId, Ct));
        Assert.Equal(AnswerStatus.NotInGame, await anonymous.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), 0, 0, Ct));
    }

    [Fact]
    public async Task A_host_cannot_start_without_players_and_history_is_private()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await using var host = await _hub.ConnectAsync(cookie);
        await Assert.ThrowsAsync<HubException>(() => host.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct));

        using var other = await TestClient.SignedInAsync(factory);
        Assert.Empty((await other.GetFromJsonAsync<List<SessionHistoryItem>>("/api/sessions/history", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/sessions/history/{session.SessionId}", Ct)).StatusCode);
    }

    private static async Task<T> Eventually<T>(Func<Task<T?>> read) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await read() is { } value)
                return value;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("Expected value did not appear.");
    }
}
