using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Sessions;

namespace LiveQuiz.Api.Features.Sessions;

/// <summary>Maps the grains' serialisable views onto the wire DTOs (Contracts stays free of Orleans).</summary>
internal static class SessionViewMapper
{
    public static HostSnapshot ToHost(Guid sessionId, SessionView v) => new(
        sessionId, v.Pin, v.QuizTitle,
        v.Players.Select(p => new PlayerInfo(p.PlayerId, p.Nickname, p.Connected)).ToList(),
        Phase(v.Status), Question(v.Question), v.Answered, Results(v.Results), Final(v.Final, v.PlayerCount));

    public static PlayerGameState ToPlayer(Guid sessionId, SessionView v) => new(
        sessionId, v.Nickname, v.PlayerCount, Phase(v.Status), Question(v.Question), v.ChosenIndex,
        Results(v.Results), Result(v.MyResult), Final(v.Final, v.PlayerCount));

    public static SessionPhase Phase(SessionStatus status) => status switch
    {
        SessionStatus.QuestionActive => SessionPhase.Question,
        SessionStatus.QuestionResults => SessionPhase.Results,
        SessionStatus.Finished => SessionPhase.Finished,
        _ => SessionPhase.Lobby
    };

    private static QuestionForPlayer? Question(QuestionView? q) =>
        q is null ? null : new QuestionForPlayer(q.Index, q.Total, q.Text, q.Options, q.TimeLimitSeconds, q.RemainingMs);

    private static QuestionResults? Results(ResultsView? r) => r is null
        ? null
        : new QuestionResults(r.Index, r.Total, r.CorrectIndex, r.OptionCounts, r.Top.Select(Standing).ToList(), r.IsLast);

    private static PlayerQuestionResult? Result(PlayerResultView? r) => r is null
        ? null
        : new PlayerQuestionResult(r.QuestionIndex, r.Answered, r.ChosenIndex, r.Correct, r.Points, r.TotalPoints, r.Rank, r.PlayerCount);

    private static FinalResults? Final(StandingView[]? top, int playerCount) =>
        top is null ? null : new FinalResults(top.Select(Standing).ToList(), playerCount);

    private static Standing Standing(StandingView s) => new(s.Rank, s.Nickname, s.Points);
}
