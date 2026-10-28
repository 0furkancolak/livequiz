using Bunit;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Client.Lobby;
using LiveQuiz.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace LiveQuiz.Web.Tests;

public sealed class PlayScreenTests : BunitContext
{
    private readonly FakePlayerLobbyClient _client = new();

    public PlayScreenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IPlayerLobbyClient>(_client);
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
        _client.Result = new JoinAttempt(new PlayerLobbySnapshot(Guid.NewGuid(), "Sam", 1));
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
        _client.Restored = new PlayerLobbySnapshot(Guid.NewGuid(), "Sam", 4);

        var cut = RenderPlay();

        cut.WaitForAssertion(() => Assert.Equal("4 players in the lobby", cut.Find("#player-count").TextContent.Trim()));
    }

    [Fact]
    public void When_the_host_cancels_the_player_is_told()
    {
        _client.Result = new JoinAttempt(new PlayerLobbySnapshot(Guid.NewGuid(), "Sam", 1));
        var cut = RenderPlay();
        Submit(cut, "123456", "Sam");
        cut.WaitForElement("#welcome");

        cut.InvokeAsync(_client.RaiseCancelled);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#cancelled")));
    }

    private sealed class FakePlayerLobbyClient : IPlayerLobbyClient
    {
        public JoinAttempt Result { get; set; } = new(null, "unset");
        public PlayerLobbySnapshot? Restored { get; set; }
        public int JoinCalls { get; private set; }
        public string? LastNickname { get; private set; }

        public event Action<int>? CountChanged;
        public event Action? Cancelled;

        public Task<JoinAttempt> JoinAsync(string pin, string nickname, CancellationToken ct = default)
        {
            JoinCalls++;
            LastNickname = nickname;
            return Task.FromResult(Result);
        }

        public Task<PlayerLobbySnapshot?> RestoreAsync(CancellationToken ct = default) => Task.FromResult(Restored);

        public void RaiseCount(int count) => CountChanged?.Invoke(count);
        public void RaiseCancelled() => Cancelled?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
