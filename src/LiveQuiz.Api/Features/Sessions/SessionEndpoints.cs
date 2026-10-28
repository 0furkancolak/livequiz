using System.Security.Claims;
using LiveQuiz.Api.Persistence;
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

        app.MapHub<QuizHub>(QuizHubNames.Path);
        return app;
    }

    private static async Task<IResult> Create(
        CreateSessionRequest request, ClaimsPrincipal user, AppDbContext db, IGrainFactory grains, CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var quiz = await db.Quizzes.AsNoTracking()
            .Where(q => q.Id == request.QuizId && q.OwnerId == ownerId)
            .Select(q => new { q.Title, Questions = q.Questions.Count })
            .FirstOrDefaultAsync(ct);

        if (quiz is null)
            return Results.NotFound();
        if (quiz.Questions == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["QuizId"] = ["The quiz has no questions."] });

        var sessionId = Guid.CreateVersion7();
        var pin = await grains.GetGrain<ISessionGrain>(sessionId).Open(ownerId, request.QuizId, quiz.Title);
        return Results.Created($"/api/sessions/{sessionId}", new CreateSessionResponse(sessionId, pin));
    }

    private static async Task<IResult> Cancel(Guid id, ClaimsPrincipal user, IGrainFactory grains)
    {
        var session = grains.GetGrain<ISessionGrain>(id);
        var info = await session.GetInfo();
        if (info.Status != SessionStatus.Lobby || info.OwnerId != user.FindFirstValue(ClaimTypes.NameIdentifier))
            return Results.NotFound();

        await session.Cancel();
        return Results.NoContent();
    }

    private static async Task<IResult> Join(JoinSessionRequest request, IGrainFactory grains)
    {
        var pin = request.Pin?.Trim() ?? string.Empty;
        if (pin.Length != SessionLimits.PinLength || !pin.All(char.IsAsciiDigit))
            return Results.NotFound();

        if (await grains.GetGrain<IPinRegistryGrain>(pin).Resolve() is not { } sessionId)
            return Results.NotFound();

        var result = await grains.GetGrain<ISessionGrain>(sessionId).Join(request.Nickname ?? string.Empty);
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
}
