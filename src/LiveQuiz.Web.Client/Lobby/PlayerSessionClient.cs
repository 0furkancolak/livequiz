using System.Net;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;

namespace LiveQuiz.Web.Client.Lobby;

public sealed class PlayerSessionClient(HttpClient http, NavigationManager navigation, PlayerIdentityStore store) : IPlayerSessionClient
{
    private QuizHubConnection? _hub;

    public event Action<int>? CountChanged;
    public event Action? Cancelled;
    public event Action<QuestionForPlayer>? QuestionStarted;
    public event Action<QuestionResults>? QuestionEnded;
    public event Action<PlayerQuestionResult>? ResultReceived;
    public event Action<FinalResults>? Finished;
    public event Action? Reconnecting;
    public event Action<PlayerGameState>? Resumed;
    public event Action? ConnectionLost;

    public async Task<AnswerStatus> SubmitAnswerAsync(int questionIndex, int optionIndex, CancellationToken ct = default) =>
        _hub is null ? AnswerStatus.NotInGame : await _hub.SubmitAnswerAsync(questionIndex, optionIndex, ct);

    public async Task<JoinAttempt> JoinAsync(string pin, string nickname, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(pin.Trim(), nickname), ct);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var identity = (await response.Content.ReadFromJsonAsync<JoinSessionResponse>(ct))!;
                await store.SaveAsync(identity);
                try
                {
                    return await ConnectAsync(identity, ct)
                        ?? new JoinAttempt(null, "Joined, but the live connection failed. Please try again.");
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    return new JoinAttempt(null, "Joined, but the live connection failed. Please try again.");
                }
            case HttpStatusCode.NotFound:
                return new JoinAttempt(null, "No open game with that PIN.");
            case HttpStatusCode.Conflict:
                return new JoinAttempt(null, "That nickname is taken or the game is full.", NicknameProblem: true);
            case HttpStatusCode.TooManyRequests:
                var wait = response.Headers.RetryAfter?.Delta;
                return new JoinAttempt(null, wait is { } d
                    ? $"Too many attempts. Try again in {Math.Max(1, (int)Math.Ceiling(d.TotalSeconds))} seconds."
                    : "Too many attempts. Please wait a moment and try again.");
            case HttpStatusCode.BadRequest:
                return new JoinAttempt(null, "Please check your nickname.", NicknameProblem: true);
            default:
                return new JoinAttempt(null, "Something went wrong. Please try again.");
        }
    }

    public async Task<PlayerGameState?> RestoreAsync(CancellationToken ct = default)
    {
        if (await store.LoadAsync() is not { } identity)
            return null;

        try
        {
            var attempt = await ConnectAsync(identity, ct);
            if (attempt is null)
                await store.ClearAsync();
            return attempt?.State;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // The network, not the game, is the problem: keep the identity so a reload can try again.
            return null;
        }
    }

    private async Task<JoinAttempt?> ConnectAsync(JoinSessionResponse identity, CancellationToken ct)
    {
        await DisposeHubAsync();
        var hub = new QuizHubConnection(navigation.ToAbsoluteUri(QuizHubNames.Path));
        hub.LobbyCount += c => CountChanged?.Invoke(c);
        hub.QuestionStarted += q => QuestionStarted?.Invoke(q);
        hub.QuestionEnded += r => QuestionEnded?.Invoke(r);
        hub.PlayerResult += r => ResultReceived?.Invoke(r);
        hub.Reconnecting += () => Reconnecting?.Invoke();
        hub.PlayerResumed += state =>
        {
            if (state.Phase == SessionPhase.Finished)
                _ = store.ClearAsync();
            Resumed?.Invoke(state);
        };
        hub.ConnectionLost += () => ConnectionLost?.Invoke();
        hub.SessionFinished += f =>
        {
            _ = store.ClearAsync();
            Finished?.Invoke(f);
        };
        hub.SessionCancelled += () =>
        {
            _ = store.ClearAsync();
            Cancelled?.Invoke();
        };

        try
        {
            var state = await hub.JoinAsPlayerAsync(identity.SessionId, identity.PlayerId, identity.ReconnectToken, ct);
            _hub = hub;
            if (state.Phase == SessionPhase.Finished)
                await store.ClearAsync();
            return new JoinAttempt(state);
        }
        catch (HubException)
        {
            await hub.DisposeAsync();
            return null;
        }
        catch
        {
            await hub.DisposeAsync();
            throw;
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
