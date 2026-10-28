using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace LiveQuiz.Api.Features.Sessions;

/// <summary>Forwards grain events to SignalR groups. Host-only events never reach the players group.</summary>
internal sealed class SignalRSessionNotifier(IHubContext<QuizHub, IQuizClient> hub) : ISessionNotifier
{
    public Task PlayerJoined(Guid sessionId, PlayerInfo player) =>
        hub.Clients.Group(QuizHubNames.HostGroup(sessionId)).PlayerJoined(player);

    public Task PlayerConnectionChanged(Guid sessionId, Guid playerId, bool connected) =>
        hub.Clients.Group(QuizHubNames.HostGroup(sessionId)).PlayerConnectionChanged(playerId, connected);

    public Task LobbyCount(Guid sessionId, int count) =>
        Task.WhenAll(
            hub.Clients.Group(QuizHubNames.PlayersGroup(sessionId)).LobbyCount(count),
            hub.Clients.Group(QuizHubNames.HostGroup(sessionId)).LobbyCount(count));

    public Task SessionCancelled(Guid sessionId) =>
        Task.WhenAll(
            hub.Clients.Group(QuizHubNames.PlayersGroup(sessionId)).SessionCancelled(),
            hub.Clients.Group(QuizHubNames.HostGroup(sessionId)).SessionCancelled());
}
