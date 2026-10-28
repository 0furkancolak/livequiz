using System.Security.Claims;
using LiveQuiz.Api.Features.RateLimiting;
using LiveQuiz.Api.Persistence;
using LiveQuiz.Contracts.Game;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Grains.Pins;
using LiveQuiz.Grains.Sessions;
using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Features.Sessions;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions");

        group.MapPost("/", Create).RequireAuthorization();
        group.MapDelete("/{id:guid}", Cancel).RequireAuthorization();
        group.MapPost("/join", Join).AllowAnonymous();
        group.MapGet("/history", History).RequireAuthorization();
        group.MapGet("/history/{id:guid}", HistoryDetail).RequireAuthorization();

        app.MapHub<QuizHub>(QuizHubNames.Path);
        return app;
    }

    private static async Task<IResult> Create(
        CreateSessionRequest request, ClaimsPrincipal user, AppDbContext db, IGrainFactory grains, CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var quiz = await db.Quizzes.AsNoTracking().AsSplitQuery()
            .Include(q => q.Questions.OrderBy(x => x.Order)).ThenInclude(x => x.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(q => q.Id == request.QuizId && q.OwnerId == ownerId, ct);

        if (quiz is null)
            return Results.NotFound();
        if (quiz.Questions.Count == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["QuizId"] = ["The quiz has no questions."] });

        // The session gets its own copy of the quiz (ADR 0008): editing the quiz mid-game changes nothing.
        var snapshot = quiz.Questions
            .Select(q => new QuestionSnapshot(
                q.Text, q.TimeLimitSeconds, q.Options.Select(o => o.Text).ToArray(), q.Options.FindIndex(o => o.IsCorrect)))
            .ToArray();

        var sessionId = Guid.CreateVersion7();
        var pin = await grains.GetGrain<ISessionGrain>(sessionId).Open(ownerId, request.QuizId, quiz.Title, snapshot);
        return Results.Created($"/api/sessions/{sessionId}", new CreateSessionResponse(sessionId, pin));
    }

    private static async Task<IResult> Cancel(Guid id, ClaimsPrincipal user, IGrainFactory grains)
    {
        var session = grains.GetGrain<ISessionGrain>(id);
        var info = await session.GetInfo();
        if (!info.Status.IsLive() || info.OwnerId != user.FindFirstValue(ClaimTypes.NameIdentifier))
            return Results.NotFound();

        await session.Cancel();
        return Results.NoContent();
    }

    private static async Task<IResult> Join(
        JoinSessionRequest request, HttpContext http, IGrainFactory grains, JoinAttemptLimiter limiter)
    {
        var client = RateLimitSetup.ClientKey(http);
        if (limiter.IsBlocked(client, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = RateLimitSetup.RetryAfterSeconds(retryAfter);
            return Results.Problem("Too many attempts. Please wait a moment.", statusCode: StatusCodes.Status429TooManyRequests);
        }

        var pin = request.Pin?.Trim() ?? string.Empty;
        if (pin.Length != SessionLimits.PinLength || !pin.All(char.IsAsciiDigit)
            || await grains.GetGrain<IPinRegistryGrain>(pin).Resolve() is not { } sessionId)
        {
            limiter.RecordFailure(client);
            return Results.NotFound();
        }

        var result = await grains.GetGrain<ISessionGrain>(sessionId).Join(request.Nickname ?? string.Empty);
        if (result.Status == JoinStatus.NotJoinable)
            limiter.RecordFailure(client);

        // Nickname clashes are ordinary user mistakes and do not count against the PIN-guessing budget.
        return result.Status switch
        {
            JoinStatus.Joined => Results.Ok(new JoinSessionResponse(sessionId, result.PlayerId, result.Nickname, result.ReconnectToken!)),
            JoinStatus.InvalidNickname => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["Nickname"] = [NicknamePolicy.Message(result.NicknameError)] }),
            JoinStatus.NicknameTaken => Results.Problem("That nickname is already taken.", statusCode: StatusCodes.Status409Conflict),
            JoinStatus.SessionFull => Results.Problem("The session is full.", statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> History(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var items = await db.SessionRecords.AsNoTracking()
            .Where(r => r.OwnerId == ownerId)
            .OrderByDescending(r => r.FinishedAtUtc)
            .Take(100)
            .Select(r => new SessionHistoryItem(
                r.Id, r.QuizTitle, r.FinishedAtUtc, r.PlayerCount,
                r.Players.Where(p => p.Rank == 1).OrderBy(p => p.Nickname).Select(p => p.Nickname).FirstOrDefault()))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> HistoryDetail(Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var record = await db.SessionRecords.AsNoTracking().AsSplitQuery()
            .Include(r => r.Players).Include(r => r.Questions)
            .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, ct);
        if (record is null)
            return Results.NotFound();

        return Results.Ok(new SessionHistoryDetail(
            record.Id, record.QuizTitle, record.StartedAtUtc, record.FinishedAtUtc,
            record.Players.OrderBy(p => p.Rank).ThenBy(p => p.Nickname).Select(p => new Standing(p.Rank, p.Nickname, p.Points)).ToList(),
            record.Questions.OrderBy(q => q.Index).Select(q => new QuestionStatDto(q.Index, q.Text, q.CorrectIndex, q.OptionCounts)).ToList()));
    }
}
