using Microsoft.JSInterop;

namespace LiveQuiz.Web.Host;

public sealed record BrowserResult(bool Ok, int Status, IReadOnlyList<string> Messages);

/// <summary>
/// Calls the same-origin /api proxy from the user's browser, so Set-Cookie responses (sign-in/out)
/// reach the browser's cookie jar. A server-side HttpClient cannot do that.
/// </summary>
public sealed class BrowserApi(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<BrowserResult> PostJsonAsync(string url, object? body)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "/js/host.js");
        var raw = await _module.InvokeAsync<RawResult>("postJson", url, body);
        return new BrowserResult(raw.Ok, raw.Status, ExtractMessages(raw.Body));
    }

    // Identity API failures are either ProblemDetails (title/detail) or a validation problem (errors: {code: [msg]}).
    private static List<string> ExtractMessages(System.Text.Json.JsonElement? body)
    {
        var messages = new List<string>();
        if (body is not { ValueKind: System.Text.Json.JsonValueKind.Object } json)
            return messages;

        if (json.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var field in errors.EnumerateObject())
                foreach (var message in field.Value.EnumerateArray())
                    messages.Add(message.GetString() ?? field.Name);
        }
        else if (json.TryGetProperty("detail", out var detail) && detail.GetString() is { } text)
        {
            messages.Add(text);
        }

        return messages;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { /* circuit already gone */ }
        }
    }

    private sealed record RawResult(bool Ok, int Status, System.Text.Json.JsonElement? Body);
}
