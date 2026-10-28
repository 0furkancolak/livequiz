using Bunit;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Components.Pages.Host;
using LiveQuiz.Web.Host;
using LiveQuiz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace LiveQuiz.Web.Tests;

public sealed class HostLobbyTests : BunitContext
{
    private readonly FakeHostLobbyClient _lobby = new();
    private readonly Guid _sessionId = Guid.NewGuid();

    public HostLobbyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        AddAuthorization().SetAuthorized("host@example.test");
        Services.AddSingleton<IHostLobbyClient>(_lobby);
        Services.AddScoped<BrowserApi>();
        Services.AddSingleton(new ApiClient(new HttpClient { BaseAddress = new Uri("http://localhost") }, new ApiSession(new HttpContextAccessor())));
    }

    private IRenderedComponent<HostLobby> RenderLobby() => Render<HostLobby>(p => p.Add(c => c.SessionId, _sessionId));

    [Fact]
    public void Shows_the_pin_and_players_already_in_the_lobby()
    {
        _lobby.Snapshot = new HostLobbySnapshot(_sessionId, "123456", "Capitals",
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
        _lobby.Snapshot = new HostLobbySnapshot(_sessionId, "123456", "Capitals", [player]);
        var cut = RenderLobby();
        Assert.DoesNotContain("mud-chip-color-success", cut.Find(".player-chip").ClassName);

        await cut.InvokeAsync(() => _lobby.RaiseConnection(player.PlayerId, true));

        cut.WaitForAssertion(() => Assert.Contains("mud-chip-color-success", cut.Find(".player-chip").ClassName));
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

    private sealed class FakeHostLobbyClient : IHostLobbyClient
    {
        public HostLobbySnapshot? Snapshot { get; set; }
        public Exception? ConnectError { get; set; }

        public event Action<PlayerInfo>? PlayerJoined;
        public event Action<Guid, bool>? PlayerConnectionChanged;
        public event Action? Cancelled;

        public Task<HostLobbySnapshot> ConnectAsync(Guid sessionId, CancellationToken ct = default) =>
            ConnectError is not null
                ? Task.FromException<HostLobbySnapshot>(ConnectError)
                : Task.FromResult(Snapshot ?? new HostLobbySnapshot(sessionId, "654321", "Quiz", []));

        public void RaisePlayerJoined(PlayerInfo p) => PlayerJoined?.Invoke(p);
        public void RaiseConnection(Guid id, bool connected) => PlayerConnectionChanged?.Invoke(id, connected);
        public void RaiseCancelled() => Cancelled?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
