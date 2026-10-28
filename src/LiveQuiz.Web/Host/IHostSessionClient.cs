using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Web.Host;

/// <summary>The host panel's live view and remote control of a session; faked in component tests.</summary>
public interface IHostSessionClient : IAsyncDisposable
{
    event Action<PlayerInfo>? PlayerJoined;
    event Action<Guid, bool>? PlayerConnectionChanged;
    event Action? Cancelled;
    event Action<QuestionForPlayer>? QuestionStarted;
    event Action<int, int>? AnswerCount;
    event Action<QuestionResults>? QuestionEnded;
    event Action<FinalResults>? Finished;

    Task<HostLobbySnapshot> ConnectAsync(Guid sessionId, CancellationToken ct = default);

    Task StartGameAsync(Guid sessionId, CancellationToken ct = default);

    Task NextQuestionAsync(Guid sessionId, CancellationToken ct = default);
}
