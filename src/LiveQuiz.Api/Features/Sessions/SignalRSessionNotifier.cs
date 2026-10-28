using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace LiveQuiz.Api.Features.Sessions;

/// <summary>Forwards grain events to SignalR groups. Host-only events never reach the players group.</summary>
internal sealed class SignalRSessionNotifier(IHubContext<QuizHub, IQuizClient> hub) : ISessionNotifier
{
    private IQuizClient Host(Guid sessionId) => hub.Clients.Group(QuizHubNames.HostGroup(sessionId));

    private IQuizClient Players(Guid sessionId) => hub.Clients.Group(QuizHubNames.PlayersGroup(sessionId));

    private IQuizClient Everyone(Guid sessionId) =>
        hub.Clients.Groups(QuizHubNames.HostGroup(sessionId), QuizHubNames.PlayersGroup(sessionId));

    public Task PlayerJoined(Guid sessionId, PlayerInfo player) => Host(sessionId).PlayerJoined(player);

    public Task PlayerConnectionChanged(Guid sessionId, Guid playerId, bool connected) =>
        Host(sessionId).PlayerConnectionChanged(playerId, connected);

    public Task LobbyCount(Guid sessionId, int count) => Everyone(sessionId).LobbyCount(count);

    public Task SessionCancelled(Guid sessionId) => Everyone(sessionId).SessionCancelled();

    public Task QuestionStarted(Guid sessionId, QuestionForPlayer question) => Everyone(sessionId).QuestionStarted(question);

    public Task AnswerCount(Guid sessionId, int answered, int total) => Host(sessionId).AnswerCount(answered, total);

    public Task QuestionEnded(Guid sessionId, QuestionResults results) => Everyone(sessionId).QuestionEnded(results);

    public Task PlayerResult(Guid sessionId, Guid playerId, PlayerQuestionResult result) =>
        hub.Clients.Group(QuizHubNames.PlayerGroup(sessionId, playerId)).PlayerResult(result);

    public Task SessionFinished(Guid sessionId, FinalResults results) => Everyone(sessionId).SessionFinished(results);
}
