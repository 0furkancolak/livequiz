using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Web.Client.Lobby;

/// <summary>Result of a join attempt: either a lobby, or a message to show (optionally tied to the nickname field).</summary>
public sealed record JoinAttempt(PlayerLobbySnapshot? Lobby, string? Error = null, bool NicknameProblem = false)
{
    public bool Succeeded => Lobby is not null;
}

/// <summary>Everything the player screen needs from the network; faked in component tests.</summary>
public interface IPlayerSessionClient : IAsyncDisposable
{
    event Action<int>? CountChanged;
    event Action? Cancelled;
    event Action<QuestionForPlayer>? QuestionStarted;
    event Action<QuestionResults>? QuestionEnded;
    event Action<PlayerQuestionResult>? ResultReceived;
    event Action<FinalResults>? Finished;

    Task<JoinAttempt> JoinAsync(string pin, string nickname, CancellationToken ct = default);

    /// <summary>Rejoins with the identity saved in this browser, if any and still valid.</summary>
    Task<PlayerLobbySnapshot?> RestoreAsync(CancellationToken ct = default);

    Task<AnswerStatus> SubmitAnswerAsync(int questionIndex, int optionIndex, CancellationToken ct = default);
}
