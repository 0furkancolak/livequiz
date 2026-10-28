using static Microsoft.Playwright.Assertions;

namespace LiveQuiz.E2E;

[Collection(StackCollection.Name)]
public sealed class GameFlowTests(LiveQuizStack stack) : IAsyncLifetime
{
    private readonly List<IBrowserContext> _contexts = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.CloseAsync();
    }

    private async Task<IPage> NewPageAsync()
    {
        // A context per person: separate cookies and storage, like separate devices.
        var context = await stack.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1100, Height = 800 },
            RecordVideoDir = Environment.GetEnvironmentVariable("LIVEQUIZ_E2E_VIDEO") // set it to record the demo (docs/demo.md)
        });
        context.SetDefaultTimeout(20_000);
        _contexts.Add(context);
        return await context.NewPageAsync();
    }

    private static readonly QuestionSpec Capital = new("Capital of France?", ["Paris", "Rome", "Madrid"], 0);
    private static readonly QuestionSpec Sum = new("What is 2 + 2?", ["3", "4"], 1);

    [Fact]
    public async Task A_full_game_is_played_from_quiz_creation_to_history()
    {
        var host = new HostDriver(await NewPageAsync(), stack.BaseUrl);
        await host.RegisterAsync();
        await host.CreateQuizAsync("World quiz", Capital, Sum);
        var pin = await host.StartSessionAsync("World quiz");

        var alice = new PlayerDriver(await NewPageAsync(), stack.BaseUrl);
        var bob = new PlayerDriver(await NewPageAsync(), stack.BaseUrl);
        await alice.OpenAsync();
        await bob.OpenAsync();
        await alice.JoinAsync(pin, "Alice");
        await bob.JoinAsync(pin, "Bob");
        await Expect(alice.Page.Locator("#welcome")).ToContainTextAsync("Alice");
        await Expect(host.Page.Locator("#lobby-count")).ToHaveTextAsync("2 players");

        await host.Page.Locator("#start-game").ClickAsync();

        // Question 1: Alice is right, Bob is wrong.
        await Expect(alice.Page.Locator("#question-text")).ToHaveTextAsync(Capital.Text);
        await Expect(bob.Page.Locator("#question-text")).ToHaveTextAsync(Capital.Text);
        await Expect(host.Page.Locator("#question-text")).ToHaveTextAsync(Capital.Text);
        await alice.Page.Locator("#option-0").ClickAsync();
        await bob.Page.Locator("#option-1").ClickAsync();
        await Expect(alice.Page.Locator("#result-headline")).ToHaveTextAsync("Correct!");
        await Expect(bob.Page.Locator("#result-headline")).ToHaveTextAsync("Wrong");
        await Expect(bob.Page.Locator("#correct-answer")).ToContainTextAsync("Paris");
        await Expect(host.Page.Locator("[data-correct=yes] .count")).ToHaveTextAsync("1");
        await Expect(host.Page.Locator("#leaderboard tr").First).ToContainTextAsync("Alice");

        // Question 2: the other way round.
        await host.Page.Locator("#next-question").ClickAsync();
        await Expect(alice.Page.Locator("#question-text")).ToHaveTextAsync(Sum.Text);
        await alice.Page.Locator("#option-0").ClickAsync();
        await bob.Page.Locator("#option-1").ClickAsync();
        await Expect(alice.Page.Locator("#result-headline")).ToHaveTextAsync("Wrong");
        await Expect(bob.Page.Locator("#result-headline")).ToHaveTextAsync("Correct!");

        await host.Page.GetByRole(AriaRole.Button, new() { Name = "Show final results" }).ClickAsync();
        await Expect(host.Page.Locator("#finished")).ToBeVisibleAsync();
        await Expect(host.Page.Locator("#podium tr")).ToHaveCountAsync(2);
        await Expect(alice.Page.Locator("#finished")).ToBeVisibleAsync();
        await Expect(alice.Page.Locator("#final-rank")).ToContainTextAsync("points");
        await Expect(bob.Page.Locator("#podium li")).ToHaveCountAsync(2);

        // The finished game is archived and listed in the host's history.
        await host.Page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
        var row = host.Page.Locator("tr").Filter(new() { HasText = "World quiz" });
        try
        {
            await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }
        catch (PlaywrightException)
        {
            throw new InvalidOperationException($"History never listed the finished game.\n{stack.ProcessLog}");
        }

        await row.GetByRole(AriaRole.Link, new() { Name = "Details" }).ClickAsync();
        await Expect(host.Page.Locator("#standings")).ToContainTextAsync("Alice");
        await Expect(host.Page.Locator("#standings")).ToContainTextAsync("Bob");
    }

    [Fact]
    public async Task A_player_who_reloads_mid_question_lands_on_the_same_question_and_can_still_answer()
    {
        var host = new HostDriver(await NewPageAsync(), stack.BaseUrl);
        await host.RegisterAsync();
        await host.CreateQuizAsync("Reload quiz", Capital);
        var pin = await host.StartSessionAsync("Reload quiz");

        var carol = new PlayerDriver(await NewPageAsync(), stack.BaseUrl);
        await carol.OpenAsync();
        await carol.JoinAsync(pin, "Carol");
        await Expect(carol.Page.Locator("#welcome")).ToBeVisibleAsync();
        await host.Page.Locator("#start-game").ClickAsync();
        await Expect(carol.Page.Locator("#question-text")).ToHaveTextAsync(Capital.Text);

        await carol.Page.ReloadAsync();
        await carol.WaitForWebAssemblyAsync();

        await Expect(carol.Page.Locator("#question-text")).ToHaveTextAsync(Capital.Text);
        await carol.Page.Locator("#option-0").ClickAsync();
        await Expect(carol.Page.Locator("#result-headline")).ToHaveTextAsync("Correct!");
    }

    [Fact]
    public async Task An_unknown_pin_is_refused_with_a_message()
    {
        var player = new PlayerDriver(await NewPageAsync(), stack.BaseUrl);
        await player.OpenAsync();
        await player.JoinAsync("000000", "Dave");

        await Expect(player.Page.Locator("p.error").Last).Not.ToBeEmptyAsync();
        await Expect(player.Page.Locator("#welcome")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_host_panel_runs_on_the_server_and_the_player_screen_in_the_browser()
    {
        var hostPage = await NewPageAsync();
        var hostLoadedWasm = false;
        hostPage.Request += (_, request) => hostLoadedWasm |= request.Url.Contains("dotnet.native", StringComparison.Ordinal);
        var host = new HostDriver(hostPage, stack.BaseUrl);
        await host.RegisterAsync();
        await host.CreateQuizAsync("Modes quiz", Capital);
        await host.StartSessionAsync("Modes quiz");

        var playerPage = await NewPageAsync();
        var playerLoadedWasm = false;
        playerPage.Request += (_, request) => playerLoadedWasm |= request.Url.Contains("dotnet.native", StringComparison.Ordinal);
        await new PlayerDriver(playerPage, stack.BaseUrl).OpenAsync();

        Assert.False(hostLoadedWasm, "The host panel must not download the .NET runtime (InteractiveServer).");
        Assert.True(playerLoadedWasm, "The player screen must run on the .NET runtime in the browser (InteractiveWebAssembly).");
    }
}
