using System.Net;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;

namespace LiveQuiz.Web.Client.Lobby;

public sealed class PlayerLobbyClient(HttpClient http, NavigationManager navigation, PlayerIdentityStore store) : IPlayerLobbyClient
{
    private QuizHubConnection? _hub;

    public event Action<int>? CountChanged;
    public event Action? Cancelled;

    public async Task<JoinAttempt> JoinAsync(string pin, string nickname, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(pin.Trim(), nickname), ct);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var identity = (await response.Content.ReadFromJsonAsync<JoinSessionResponse>(ct))!;
                await store.SaveAsync(identity);
                return await ConnectAsync(identity, ct)
                    ?? new JoinAttempt(null, "Joined, but the live connection failed. Please try again.");
            case HttpStatusCode.NotFound:
                return new JoinAttempt(null, "No open game with that PIN.");
            case HttpStatusCode.Conflict:
                return new JoinAttempt(null, "That nickname is taken or the game is full.", NicknameProblem: true);
            case HttpStatusCode.BadRequest:
                return new JoinAttempt(null, "Please check your nickname.", NicknameProblem: true);
            default:
                return new JoinAttempt(null, "Something went wrong. Please try again.");
        }
    }

    public async Task<PlayerLobbySnapshot?> RestoreAsync(CancellationToken ct = default)
    {
        if (await store.LoadAsync() is not { } identity)
            return null;

        var attempt = await ConnectAsync(identity, ct);
        if (attempt is null)
            await store.ClearAsync();
        return attempt?.Lobby;
    }

    private async Task<JoinAttempt?> ConnectAsync(JoinSessionResponse identity, CancellationToken ct)
    {
        await DisposeHubAsync();
        var hub = new QuizHubConnection(navigation.ToAbsoluteUri(QuizHubNames.Path));
        hub.LobbyCount += c => CountChanged?.Invoke(c);
        hub.SessionCancelled += () =>
        {
            _ = store.ClearAsync();
            Cancelled?.Invoke();
        };

        try
        {
            var lobby = await hub.JoinAsPlayerAsync(identity.SessionId, identity.PlayerId, identity.ReconnectToken, ct);
            _hub = hub;
            return new JoinAttempt(lobby);
        }
        catch (Exception ex) when (ex is HubException or HttpRequestException or InvalidOperationException)
        {
            await hub.DisposeAsync();
            return null;
        }
    }

    private async ValueTask DisposeHubAsync()
    {
        if (_hub is { } hub)
        {
            _hub = null;
            await hub.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => DisposeHubAsync();
}
