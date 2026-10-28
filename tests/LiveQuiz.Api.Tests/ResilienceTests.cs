using System.Net;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Api.Tests;

/// <summary>Resuming at any phase and the rate limits (ADR 0013, ADR 0014).</summary>
[Collection(ApiCollection.Name)]
public sealed class ResilienceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly LobbyHubTests _hub = new(factory);

    private static Task<bool> Wait(Func<bool> condition) =>
        LobbyHubTests.Eventually(() => condition() ? true : null as bool?);

    private async Task<(JoinSessionResponse Joined, LobbyHubTests.Probe Probe)> PlayerAsync(CreateSessionResponse session, string nickname)
    {
        var joined = await _hub.JoinAsync(session.Pin, nickname);
        var probe = await _hub.ConnectAsync();
        await probe.Connection.InvokeAsync<PlayerGameState>(nameof(IQuizHub.JoinAsPlayer), session.SessionId, joined.PlayerId, joined.ReconnectToken, Ct);
        return (joined, probe);
    }

    private Task<PlayerGameState> RejoinPlayerAsync(LobbyHubTests.Probe probe, Guid sessionId, JoinSessionResponse joined) =>
        probe.Connection.InvokeAsync<PlayerGameState>(nameof(IQuizHub.JoinAsPlayer), sessionId, joined.PlayerId, joined.ReconnectToken, Ct);

    [Fact]
    public async Task A_rejoining_client_is_told_where_the_game_is_at_every_phase()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await using var host = await _hub.ConnectAsync(cookie);
        var lobby = await host.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        Assert.Equal(SessionPhase.Lobby, lobby.Phase);
        var (ann, annProbe) = await PlayerAsync(session, "Ann");
        await using var _a = annProbe;

        Assert.Equal(SessionPhase.Lobby, (await RejoinPlayerAsync(annProbe, session.SessionId, ann)).Phase);

        await host.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct);
        await Wait(() => annProbe.Questions.Count == 1);

        await using var freshHost = await _hub.ConnectAsync(cookie);
        var hostDuring = await freshHost.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        Assert.Equal(SessionPhase.Question, hostDuring.Phase);
        Assert.Equal(0, hostDuring.Question!.Index);

        await annProbe.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), 0, 1, Ct);

        // Ann was the only player, so her answer closed the question.
        await Wait(() => annProbe.MyResults.Count == 1);
        var playerAtResults = await RejoinPlayerAsync(annProbe, session.SessionId, ann);
        Assert.Equal(SessionPhase.Results, playerAtResults.Phase);
        Assert.True(playerAtResults.Result!.Answered);
        Assert.NotNull(playerAtResults.Results);
        Assert.NotNull(playerAtResults.Result);
        var hostAtResults = await freshHost.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        Assert.Equal(SessionPhase.Results, hostAtResults.Phase);
        Assert.NotNull(hostAtResults.Results);

        for (var q = 1; q < 3; q++)
        {
            await host.Connection.InvokeAsync(nameof(IQuizHub.NextQuestion), session.SessionId, Ct);
            await Wait(() => annProbe.Questions.Count == q + 1);
            await annProbe.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), q, 0, Ct);
            await Wait(() => annProbe.MyResults.Count == q + 1);
        }
        await host.Connection.InvokeAsync(nameof(IQuizHub.NextQuestion), session.SessionId, Ct);
        await Wait(() => annProbe.Finals.Count == 1);

        var playerFinal = await RejoinPlayerAsync(annProbe, session.SessionId, ann);
        Assert.Equal(SessionPhase.Finished, playerFinal.Phase);
        Assert.Equal("Ann", playerFinal.Final!.Top.Single().Nickname);
        var hostFinal = await freshHost.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        Assert.Equal(SessionPhase.Finished, hostFinal.Phase);
        Assert.NotNull(hostFinal.Final);
    }

    [Fact]
    public async Task A_rejoining_player_sees_which_option_they_chose()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await using var host = await _hub.ConnectAsync(cookie);
        await host.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        var (ann, annProbe) = await PlayerAsync(session, "Ann");
        await using var _a = annProbe;
        var (_, bobProbe) = await PlayerAsync(session, "Bob");
        await using var _b = bobProbe;
        await host.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct);
        await Wait(() => annProbe.Questions.Count == 1);
        await annProbe.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), 0, 2, Ct);

        await using var phone = await _hub.ConnectAsync(); // Ann's page was reloaded
        var state = await RejoinPlayerAsync(phone, session.SessionId, ann);

        Assert.Equal(SessionPhase.Question, state.Phase);
        Assert.Equal(2, state.ChosenIndex);
        Assert.InRange(state.Question!.RemainingMs, 1, state.Question.TimeLimitSeconds * 1000);
    }

    [Fact]
    public async Task Wrong_pins_are_limited_per_client_and_answered_with_retry_after()
    {
        using var client = factory.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest("000000", "Ann"), Ct);
            Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest("000000", "Ann"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.InRange(blocked.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);

        // Another client is unaffected.
        using var other = factory.CreateClient();
        var fine = await other.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest("000000", "Ann"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, fine.StatusCode);
    }

    [Fact]
    public async Task Successful_joins_do_not_count_towards_the_limit()
    {
        var (httpHost, _) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        using var classroom = factory.CreateClient(); // one address, like a classroom behind one router

        for (var i = 0; i < 15; i++)
        {
            var response = await classroom.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, $"Pupil {i}"), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_peer_is_ignored()
    {
        // The peer is a public address, so its X-Forwarded-For must not be believed: rotating it buys nothing.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Peer", "203.0.113.77");
        for (var i = 0; i < 10; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ApiFactory.NextAddress());
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest("000000", "Ann"), Ct)).StatusCode);
        }

        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ApiFactory.NextAddress());
        var blocked = await client.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest("000000", "Ann"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Sign_in_attempts_are_limited_per_client()
    {
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.test", password = "Wrong-Password-1" }, Ct);
            statuses.Add(response.StatusCode);
            if (i == 10)
                Assert.NotNull(response.Headers.RetryAfter);
        }

        Assert.All(statuses.Take(10), s => Assert.NotEqual(HttpStatusCode.TooManyRequests, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    [Fact]
    public async Task A_flood_of_answers_on_one_connection_is_cut_off()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await using var host = await _hub.ConnectAsync(cookie);
        await host.Connection.InvokeAsync<HostSnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        var (_, ann) = await PlayerAsync(session, "Ann");
        await using var _a = ann;
        var (_, bob) = await PlayerAsync(session, "Bob");
        await using var _b = bob;
        await host.Connection.InvokeAsync(nameof(IQuizHub.StartGame), session.SessionId, Ct);
        await Wait(() => ann.Questions.Count == 1);

        var statuses = new List<AnswerStatus>();
        for (var i = 0; i < 12; i++)
            statuses.Add(await ann.Connection.InvokeAsync<AnswerStatus>(nameof(IQuizHub.SubmitAnswer), 0, i % 4, Ct));

        Assert.Equal(AnswerStatus.Accepted, statuses[0]);
        Assert.Contains(AnswerStatus.RateLimited, statuses);
        Assert.DoesNotContain(AnswerStatus.RateLimited, statuses.Take(5));
    }
}
