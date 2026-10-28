using System.Collections.Concurrent;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed partial class LobbyHubTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal sealed class Probe : IAsyncDisposable
    {
        public ConcurrentQueue<PlayerInfo> Joined { get; } = new();
        public ConcurrentQueue<(Guid Player, bool Connected)> Connections { get; } = new();
        public ConcurrentQueue<int> Counts { get; } = new();
        public int Cancelled;
        public ConcurrentQueue<QuestionForPlayer> Questions { get; } = new();
        public ConcurrentQueue<QuestionResults> Results { get; } = new();
        public ConcurrentQueue<PlayerQuestionResult> MyResults { get; } = new();
        public ConcurrentQueue<FinalResults> Finals { get; } = new();
        public ConcurrentQueue<int> Answered { get; } = new();
        public required HubConnection Connection { get; init; }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    internal async Task<Probe> ConnectAsync(string? cookie = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, QuizHubNames.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (cookie is not null)
                    options.Headers["Cookie"] = cookie;
            })
            .Build();

        var probe = new Probe { Connection = connection };
        connection.On<PlayerInfo>(nameof(IQuizClient.PlayerJoined), p => probe.Joined.Enqueue(p));
        connection.On<Guid, bool>(nameof(IQuizClient.PlayerConnectionChanged), (id, c) => probe.Connections.Enqueue((id, c)));
        connection.On<int>(nameof(IQuizClient.LobbyCount), c => probe.Counts.Enqueue(c));
        connection.On(nameof(IQuizClient.SessionCancelled), () => Interlocked.Increment(ref probe.Cancelled));
        connection.On<QuestionForPlayer>(nameof(IQuizClient.QuestionStarted), q => probe.Questions.Enqueue(q));
        connection.On<int, int>(nameof(IQuizClient.AnswerCount), (a, _) => probe.Answered.Enqueue(a));
        connection.On<QuestionResults>(nameof(IQuizClient.QuestionEnded), r => probe.Results.Enqueue(r));
        connection.On<PlayerQuestionResult>(nameof(IQuizClient.PlayerResult), r => probe.MyResults.Enqueue(r));
        connection.On<FinalResults>(nameof(IQuizClient.SessionFinished), f => probe.Finals.Enqueue(f));
        await connection.StartAsync(Ct);
        return probe;
    }

    internal static async Task<T> Eventually<T>(Func<T?> read) where T : struct
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value)
                return value;
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException("Expected hub event did not arrive.");
    }

    internal async Task<JoinSessionResponse> JoinAsync(string pin, string nickname)
    {
        using var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(pin, nickname), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JoinSessionResponse>(Ct))!;
    }

    [Fact]
    public async Task Host_sees_players_join_in_real_time()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await using var host = await ConnectAsync(cookie);
        var snapshot = await host.Connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        Assert.Equal(session.Pin, snapshot.Pin);
        Assert.Empty(snapshot.Players);

        var joined = new[] { await JoinAsync(session.Pin, "Ann"), await JoinAsync(session.Pin, "Bob"), await JoinAsync(session.Pin, "Cy") };

        await Eventually(() => host.Joined.Count == 3 ? true : null as bool?);
        Assert.Equal(joined.Select(j => j.PlayerId).Order(), host.Joined.Select(p => p.PlayerId).Order());
        Assert.Equal(3, await Eventually(() => host.Counts.Contains(3) ? 3 : null as int?));
    }

    [Fact]
    public async Task Late_joining_host_gets_existing_players_in_the_snapshot()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        await JoinAsync(session.Pin, "Ann");
        await JoinAsync(session.Pin, "Bob");

        await using var host = await ConnectAsync(cookie);
        var snapshot = await host.Connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);

        Assert.Equal(["Ann", "Bob"], snapshot.Players.Select(p => p.Nickname).Order());
    }

    [Fact]
    public async Task Only_the_owning_host_can_join_the_host_group()
    {
        var (httpHost, _) = await TestClient.SignedInWithCookieAsync(factory);
        using var _1 = httpHost;
        var (httpOther, otherCookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _2 = httpOther;
        var session = await SessionEndpointTests.StartSession(httpHost);

        await using var anonymous = await ConnectAsync();
        await using var other = await ConnectAsync(otherCookie);

        await Assert.ThrowsAsync<HubException>(() => anonymous.Connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct));
        await Assert.ThrowsAsync<HubException>(() => other.Connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct));
    }

    [Fact]
    public async Task Player_needs_a_valid_token_and_never_receives_other_players_names()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        var ann = await JoinAsync(session.Pin, "Ann");
        await using var host = await ConnectAsync(cookie);
        await host.Connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        await using var player = await ConnectAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            player.Connection.InvokeAsync<PlayerLobbySnapshot>(nameof(IQuizHub.JoinAsPlayer), session.SessionId, ann.PlayerId, "wrong-token", Ct));

        var lobby = await player.Connection.InvokeAsync<PlayerLobbySnapshot>(
            nameof(IQuizHub.JoinAsPlayer), session.SessionId, ann.PlayerId, ann.ReconnectToken, Ct);
        Assert.Equal("Ann", lobby.Nickname);
        Assert.Equal(1, lobby.PlayerCount);

        await JoinAsync(session.Pin, "Bob");
        await Eventually(() => host.Joined.Any(p => p.Nickname == "Bob") ? true : null as bool?);
        await Eventually(() => player.Counts.Contains(2) ? true : null as bool?);
        Assert.Empty(player.Joined);
    }

    [Fact]
    public async Task Host_sees_connection_changes_and_cancellation_reaches_everyone()
    {
        var (httpHost, cookie) = await TestClient.SignedInWithCookieAsync(factory);
        using var _ = httpHost;
        var session = await SessionEndpointTests.StartSession(httpHost);
        var ann = await JoinAsync(session.Pin, "Ann");
        await using var host = await ConnectAsync(cookie);
        await host.Connection.InvokeAsync<HostLobbySnapshot>(nameof(IQuizHub.JoinAsHost), session.SessionId, Ct);
        var player = await ConnectAsync();
        await player.Connection.InvokeAsync<PlayerLobbySnapshot>(
            nameof(IQuizHub.JoinAsPlayer), session.SessionId, ann.PlayerId, ann.ReconnectToken, Ct);

        await Eventually(() => host.Connections.Contains((ann.PlayerId, true)) ? true : null as bool?);

        await httpHost.DeleteAsync($"/api/sessions/{session.SessionId}", Ct);
        await Eventually(() => Volatile.Read(ref player.Cancelled) > 0 ? true : null as bool?);
        await Eventually(() => Volatile.Read(ref host.Cancelled) > 0 ? true : null as bool?);

        await player.DisposeAsync();
    }
}
