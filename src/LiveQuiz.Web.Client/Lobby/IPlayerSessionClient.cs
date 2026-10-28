using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Web.Client.Lobby;

/// <summary>Result of a join attempt: either the state of the game to continue from, or a message to show (optionally tied to the nickname field).</summary>
public sealed record JoinAttempt(PlayerGameState? State, string? Error = null, bool NicknameProblem = false)
{
    public bool Succeeded => State is not null;
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

    /// <summary>The connection dropped; the client keeps retrying on its own.</summary>
    event Action? Reconnecting;

    /// <summary>Back online after <see cref="Reconnecting"/>: the state to continue from.</summary>
    event Action<PlayerGameState>? Resumed;

    /// <summary>The connection could not be restored; the player has to reload.</summary>
    event Action? ConnectionLost;

    Task<JoinAttempt> JoinAsync(string pin, string nickname, CancellationToken ct = default);

    /// <summary>Rejoins with the identity saved in this browser, if any and still valid, at whatever phase the game is in.</summary>
    Task<PlayerGameState?> RestoreAsync(CancellationToken ct = default);

    Task<AnswerStatus> SubmitAnswerAsync(int questionIndex, int optionIndex, CancellationToken ct = default);
}
