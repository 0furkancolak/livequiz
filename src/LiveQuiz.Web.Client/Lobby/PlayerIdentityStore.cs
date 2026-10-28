using System.Text.Json;
using LiveQuiz.Contracts.Sessions;
using Microsoft.JSInterop;

namespace LiveQuiz.Web.Client.Lobby;

/// <summary>
/// Keeps the join result in <c>localStorage</c> so a refresh can rejoin. Storage can be unavailable or throw
/// (private windows, blocked site data), so every access is guarded and the screen works without it.
/// </summary>
public sealed class PlayerIdentityStore(IJSRuntime js)
{
    private const string Key = "livequiz.player";

    public async Task SaveAsync(JoinSessionResponse identity)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(identity));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JsonException)
        {
            // Not persisted: the player simply cannot rejoin after a refresh.
        }
    }

    public async Task<JoinSessionResponse?> LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            return json is null ? null : JsonSerializer.Deserialize<JoinSessionResponse>(json);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", Key);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Nothing to clear.
        }
    }
}
