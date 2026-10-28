namespace LiveQuiz.E2E;

public sealed record QuestionSpec(string Text, string[] Options, int Correct);

/// <summary>The host panel as a user sees it.</summary>
public sealed class HostDriver(IPage page, string baseUrl)
{
    public IPage Page { get; } = page;

    public async Task RegisterAsync()
    {
        await Page.GotoAsync($"{baseUrl}/host/register");
        await WaitForCircuitAsync();
        await FillAsync(Page.GetByLabel("Email"), $"host-{Guid.NewGuid():N}@example.test");
        await FillAsync(Page.GetByLabel("Password"), "Str0ng!Passw0rd");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Register" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My quizzes" })).ToBeVisibleAsync();
    }

    public async Task CreateQuizAsync(string title, params QuestionSpec[] questions)
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "New quiz" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "New quiz" })).ToBeVisibleAsync();
        await WaitForCircuitAsync();
        await FillAsync(Page.GetByLabel("Title"), title);

        for (var q = 0; q < questions.Length; q++)
        {
            if (q > 0)
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add question" }).ClickAsync();

            var card = Page.GetByTestId("question").Nth(q);
            await FillAsync(card.GetByLabel("Question text"), questions[q].Text);
            for (var o = 0; o < questions[q].Options.Length; o++)
            {
                if (o >= 2) // a new question starts with two options
                    await card.GetByRole(AriaRole.Button, new() { Name = "Add option" }).ClickAsync();
                await FillAsync(card.GetByLabel($"Option {o + 1}", new() { Exact = true }), questions[q].Options[o]);
            }

            await card.GetByLabel($"Mark option {questions[q].Correct + 1} correct").CheckAsync();
        }

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save quiz" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Cell, new() { Name = title, Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>Starts a session for the quiz and returns its PIN.</summary>
    public async Task<string> StartSessionAsync(string quizTitle)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Start session for {quizTitle}" }).ClickAsync();
        var pin = Page.Locator("#session-pin");
        await Assertions.Expect(pin).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d{6}$"));
        return (await pin.TextContentAsync())!;
    }

    // MudBlazor binds on the change event, which a plain fill only raises when the field loses focus.
    private static async Task FillAsync(ILocator field, string text)
    {
        await field.FillAsync(text);
        await field.PressAsync("Tab");
    }

    // The server renders the page first and the circuit connects a moment later; input typed before that is lost.
    private Task WaitForCircuitAsync() => Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}

/// <summary>The player screen on a phone: it runs as WebAssembly.</summary>
public sealed class PlayerDriver(IPage page, string baseUrl)
{
    public IPage Page { get; } = page;

    public async Task OpenAsync()
    {
        await Page.GotoAsync($"{baseUrl}/play");
        await WaitForWebAssemblyAsync();
    }

    public async Task JoinAsync(string pin, string nickname)
    {
        await Page.Locator("#pin").FillAsync(pin);
        await Page.Locator("#nickname").FillAsync(nickname);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Join" }).ClickAsync();
    }

    // Until the runtime has started, the prerendered form is inert.
    public Task WaitForWebAssemblyAsync() =>
        Assertions.Expect(Page.Locator("#is-wasm")).ToHaveTextAsync("True", new() { Timeout = 60_000 });
}
