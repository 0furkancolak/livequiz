using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Pins;
using LiveQuiz.Grains.Players;
using LiveQuiz.Grains.Sessions;

namespace LiveQuiz.Grains.Tests;

[Collection(ClusterCollection.Name)]
public class SessionGrainTests(ClusterFixture fixture)
{
    private async Task<(Guid Id, ISessionGrain Grain, string Pin)> OpenSession(string owner = "owner-1")
    {
        var id = Guid.CreateVersion7();
        var grain = fixture.Grains.GetGrain<ISessionGrain>(id);
        var pin = await grain.Open(owner, Guid.NewGuid(), "Capitals");
        return (id, grain, pin);
    }

    [Fact]
    public async Task Open_allocates_a_numeric_pin_that_resolves_to_the_session()
    {
        var (id, _, pin) = await OpenSession();

        Assert.Equal(SessionLimits.PinLength, pin.Length);
        Assert.All(pin, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.Equal(id, await fixture.Grains.GetGrain<IPinRegistryGrain>(pin).Resolve());
    }

    [Fact]
    public async Task Concurrent_sessions_get_distinct_pins()
    {
        var sessions = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => OpenSession()));

        Assert.Equal(sessions.Length, sessions.Select(s => s.Pin).Distinct().Count());
    }

    [Fact]
    public async Task Open_twice_fails()
    {
        var (_, grain, _) = await OpenSession();

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.Open("owner-1", Guid.NewGuid(), "Again"));
    }

    [Fact]
    public async Task Join_returns_player_identity_and_lists_the_player()
    {
        var (_, grain, _) = await OpenSession();

        var result = await grain.Join("  Ayşe   Yılmaz ");

        Assert.Equal(JoinStatus.Joined, result.Status);
        Assert.Equal("Ayşe Yılmaz", result.Nickname);
        Assert.NotEqual(Guid.Empty, result.PlayerId);
        Assert.False(string.IsNullOrEmpty(result.ReconnectToken));
        var info = await grain.GetInfo();
        Assert.Equal(result.PlayerId, Assert.Single(info.Players).PlayerId);
    }

    [Theory]
    [InlineData("Ayşe", "ayse")]
    [InlineData("Player One", "player one")]
    [InlineData("Ali", "ALİ")]
    public async Task Nicknames_are_unique_ignoring_case_and_accents(string first, string second)
    {
        var (_, grain, _) = await OpenSession();

        Assert.Equal(JoinStatus.Joined, (await grain.Join(first)).Status);
        Assert.Equal(JoinStatus.NicknameTaken, (await grain.Join(second)).Status);
    }

    [Fact]
    public async Task The_same_nickname_is_fine_in_a_different_session()
    {
        var (_, a, _) = await OpenSession();
        var (_, b, _) = await OpenSession();

        Assert.Equal(JoinStatus.Joined, (await a.Join("Sam")).Status);
        Assert.Equal(JoinStatus.Joined, (await b.Join("Sam")).Status);
    }

    [Theory]
    [InlineData("", NicknameError.Empty)]
    [InlineData("a", NicknameError.TooShort)]
    [InlineData("abcdefghijklmnopqrstuvwxyz", NicknameError.TooLong)]
    [InlineData("bad<script>", NicknameError.InvalidCharacters)]
    [InlineData("Big_Shit_99", NicknameError.Profanity)]
    public async Task Invalid_nicknames_are_rejected_with_a_reason(string nickname, NicknameError expected)
    {
        var (_, grain, _) = await OpenSession();

        var result = await grain.Join(nickname);

        Assert.Equal(JoinStatus.InvalidNickname, result.Status);
        Assert.Equal(expected, result.NicknameError);
        Assert.Empty((await grain.GetInfo()).Players);
    }

    [Fact]
    public async Task Join_is_rejected_before_open_and_after_cancel()
    {
        var unopened = fixture.Grains.GetGrain<ISessionGrain>(Guid.CreateVersion7());
        Assert.Equal(JoinStatus.NotJoinable, (await unopened.Join("Sam")).Status);

        var (_, grain, _) = await OpenSession();
        await grain.Cancel();
        Assert.Equal(JoinStatus.NotJoinable, (await grain.Join("Sam")).Status);
    }

    [Fact]
    public async Task Cancel_releases_the_pin_and_notifies_clients()
    {
        var (id, grain, pin) = await OpenSession();

        await grain.Cancel();

        Assert.Null(await fixture.Grains.GetGrain<IPinRegistryGrain>(pin).Resolve());
        Assert.Equal(SessionStatus.Cancelled, (await grain.GetInfo()).Status);
        Assert.Contains(RecordingNotifier.Instance.For(id), e => e.Kind == nameof(ISessionNotifier.SessionCancelled));
    }

    [Fact]
    public async Task Join_notifies_the_host_with_the_new_player()
    {
        var (id, grain, _) = await OpenSession();

        var joined = await grain.Join("Sam");

        var evt = Assert.Single(RecordingNotifier.Instance.For(id), e => e.Kind == nameof(ISessionNotifier.PlayerJoined));
        var player = Assert.IsType<PlayerInfo>(evt.Data);
        Assert.Equal(new PlayerInfo(joined.PlayerId, "Sam", false), player);
    }

    [Fact]
    public async Task Connection_changes_are_tracked_and_pushed()
    {
        var (id, grain, _) = await OpenSession();
        var joined = await grain.Join("Sam");

        await grain.SetPlayerConnected(joined.PlayerId, true);

        Assert.True((await grain.GetInfo()).Players.Single().Connected);
        Assert.Contains(RecordingNotifier.Instance.For(id),
            e => e.Kind == nameof(ISessionNotifier.PlayerConnectionChanged) && e.Data is ValueTuple<Guid, bool> (var p, true) && p == joined.PlayerId);
    }

    [Fact]
    public async Task Burst_of_joins_is_coalesced_into_few_count_pushes_ending_with_the_total()
    {
        var (id, grain, _) = await OpenSession();

        for (var i = 0; i < 10; i++)
            await grain.Join($"Player {i}");

        await WaitUntil(() => LastCount(id) == 10);
        var counts = RecordingNotifier.Instance.For(id).Where(e => e.Kind == nameof(ISessionNotifier.LobbyCount)).ToList();
        Assert.True(counts.Count < 10, $"expected coalescing, got {counts.Count} pushes");
        Assert.Equal(10, LastCount(id));
    }

    [Fact]
    public async Task The_reconnect_token_verifies_only_for_its_own_player()
    {
        var (id, grain, _) = await OpenSession();
        var a = await grain.Join("Alice");
        var b = await grain.Join("Bob");

        var playerA = fixture.Grains.GetGrain<IPlayerGrain>(a.PlayerId, PlayerKeys.Extension(id));
        Assert.True(await playerA.Verify(a.ReconnectToken!));
        Assert.False(await playerA.Verify(b.ReconnectToken!));
        Assert.False(await playerA.Verify(""));
    }

    private static int? LastCount(Guid id) =>
        RecordingNotifier.Instance.For(id).LastOrDefault(e => e.Kind == nameof(ISessionNotifier.LobbyCount)).Data as int?;

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }
}
