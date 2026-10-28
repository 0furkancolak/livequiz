using System.Security.Claims;
using LiveQuiz.Api.Persistence;
using LiveQuiz.Contracts.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Features.Quizzes;

public static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizzes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quizzes").RequireAuthorization();

        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);

        return app;
    }

    // Every query is scoped to the caller. A quiz owned by someone else is indistinguishable from a missing one (404).

    private static async Task<IResult> List(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = OwnerId(user);
        var items = await db.Quizzes
            .Where(q => q.OwnerId == ownerId)
            .OrderByDescending(q => q.UpdatedAtUtc)
            .Select(q => new QuizSummary(q.Id, q.Title, q.Description, q.Questions.Count, q.UpdatedAtUtc))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = OwnerId(user);
        var quiz = await db.Quizzes.AsNoTracking().AsSplitQuery()
            .Include(q => q.Questions.OrderBy(x => x.Order)).ThenInclude(x => x.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId, ct);
        return quiz is null ? Results.NotFound() : Results.Ok(ToDetail(quiz));
    }

    private static async Task<IResult> Create(SaveQuizRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = QuizValidator.Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors.ToDictionary());

        var now = time.GetUtcNow();
        var quiz = new Quiz { OwnerId = OwnerId(user), Title = request.Title.Trim(), CreatedAtUtc = now, UpdatedAtUtc = now };
        Apply(quiz, request);

        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/quizzes/{quiz.Id}", ToDetail(quiz));
    }

    private static async Task<IResult> Update(Guid id, SaveQuizRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = QuizValidator.Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors.ToDictionary());

        var ownerId = OwnerId(user);

        // Aspire enables a retrying execution strategy, so the whole unit of work must run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId, ct);
            if (quiz is null)
                return Results.NotFound();

            // The quiz is replaced as a whole. Old questions (and their options, via cascade) are deleted first so the
            // (QuizId, Order) unique index never sees old and new rows at the same time.
            await db.Questions.Where(q => q.QuizId == id).ExecuteDeleteAsync(ct);

            quiz.Title = request.Title.Trim();
            quiz.UpdatedAtUtc = time.GetUtcNow();
            Apply(quiz, request);
            // Ids are client-generated, so EF would treat graph-discovered entities as existing (Modified); mark them Added.
            db.Questions.AddRange(quiz.Questions);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDetail(quiz));
        });
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = OwnerId(user);
        var deleted = await db.Quizzes.Where(q => q.Id == id && q.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static string OwnerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user has no id claim.");

    private static void Apply(Quiz quiz, SaveQuizRequest request)
    {
        quiz.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        quiz.Questions = request.Questions
            .Select((q, i) => new Question
            {
                QuizId = quiz.Id,
                Order = i,
                Text = q.Text.Trim(),
                TimeLimitSeconds = q.TimeLimitSeconds,
                Options = q.Options.Select((o, j) => new AnswerOption { Order = j, Text = o.Text.Trim(), IsCorrect = o.IsCorrect }).ToList()
            })
            .ToList();
    }

    private static QuizDetail ToDetail(Quiz quiz) => new(
        quiz.Id,
        quiz.Title,
        quiz.Description,
        quiz.Questions.OrderBy(q => q.Order)
            .Select(q => new QuestionDto(
                q.Id,
                q.Text,
                q.TimeLimitSeconds,
                q.Options.OrderBy(o => o.Order).Select(o => new OptionDto(o.Id, o.Text, o.IsCorrect)).ToList()))
            .ToList(),
        quiz.CreatedAtUtc,
        quiz.UpdatedAtUtc);
}
