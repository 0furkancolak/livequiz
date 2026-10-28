using System.Net;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Quizzes;
using Microsoft.AspNetCore.Mvc;

namespace LiveQuiz.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class QuizEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Host_can_create_a_five_question_quiz_and_read_it_back_in_order()
    {
        using var client = await TestClient.SignedInAsync(factory);

        var created = await client.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(5), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = await created.Content.ReadFromJsonAsync<QuizDetail>(Ct);
        Assert.NotNull(detail);

        var fetched = await client.GetFromJsonAsync<QuizDetail>($"/api/quizzes/{detail.Id}", Ct);
        Assert.NotNull(fetched);
        Assert.Equal(5, fetched.Questions.Count);
        Assert.Equal(["Question 1?", "Question 2?", "Question 3?", "Question 4?", "Question 5?"], fetched.Questions.Select(q => q.Text));
        Assert.All(fetched.Questions, q =>
        {
            Assert.Equal(4, q.Options.Count);
            Assert.Single(q.Options, o => o.IsCorrect);
        });

        var list = await client.GetFromJsonAsync<List<QuizSummary>>("/api/quizzes", Ct);
        var summary = Assert.Single(list!);
        Assert.Equal(5, summary.QuestionCount);
    }

    [Fact]
    public async Task Update_replaces_questions_and_keeps_order()
    {
        using var client = await TestClient.SignedInAsync(factory);
        var created = await (await client.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(5), Ct))
            .Content.ReadFromJsonAsync<QuizDetail>(Ct);

        var updated = await client.PutAsJsonAsync($"/api/quizzes/{created!.Id}", QuizFactory.Quiz(2, "Renamed"), Ct);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var fetched = await client.GetFromJsonAsync<QuizDetail>($"/api/quizzes/{created.Id}", Ct);
        Assert.Equal("Renamed", fetched!.Title);
        Assert.Equal(2, fetched.Questions.Count);
    }

    [Fact]
    public async Task Delete_removes_the_quiz_and_its_children()
    {
        using var client = await TestClient.SignedInAsync(factory);
        var created = await (await client.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(3), Ct))
            .Content.ReadFromJsonAsync<QuizDetail>(Ct);

        var deleted = await client.DeleteAsync($"/api/quizzes/{created!.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/quizzes/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/quizzes/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Another_host_cannot_see_update_or_delete_the_quiz()
    {
        using var owner = await TestClient.SignedInAsync(factory);
        using var intruder = await TestClient.SignedInAsync(factory);
        var created = await (await owner.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(1), Ct))
            .Content.ReadFromJsonAsync<QuizDetail>(Ct);
        var url = $"/api/quizzes/{created!.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PutAsJsonAsync(url, QuizFactory.Quiz(1), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync(url, Ct)).StatusCode);
        Assert.Empty((await intruder.GetFromJsonAsync<List<QuizSummary>>("/api/quizzes", Ct))!);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_get_401_not_a_redirect()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/quizzes", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(), Ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_the_session()
    {
        using var client = await TestClient.SignedInAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/quizzes", Ct)).StatusCode);

        (await client.PostAsync("/api/auth/logout", null, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/quizzes", Ct)).StatusCode);
    }

    public static TheoryData<string, SaveQuizRequest, string> InvalidQuizzes() => new()
    {
        { "no questions", new("T", null, []), "Questions" },
        { "blank title", QuizFactory.Quiz() with { Title = "  " }, "Title" },
        { "no correct option", new("T", null, [new("Q", 20, [new("a", false), new("b", false)])]), "Questions[0].Options" },
        { "two correct options", new("T", null, [new("Q", 20, [new("a", true), new("b", true)])]), "Questions[0].Options" },
        { "one option", new("T", null, [new("Q", 20, [new("a", true)])]), "Questions[0].Options" },
        { "five options", QuizFactory.Quiz(1) with { Questions = [QuizFactory.Question(1, optionCount: 5)] }, "Questions[0].Options" },
        { "time limit too short", new("T", null, [new("Q", 1, [new("a", true), new("b", false)])]), "Questions[0].TimeLimitSeconds" },
        { "blank question text", new("T", null, [new(" ", 20, [new("a", true), new("b", false)])]), "Questions[0].Text" },
    };

    [Theory]
    [MemberData(nameof(InvalidQuizzes))]
    public async Task Invalid_quiz_is_rejected_with_a_validation_problem(string _, SaveQuizRequest request, string expectedKey)
    {
        using var client = await TestClient.SignedInAsync(factory);

        var response = await client.PostAsJsonAsync("/api/quizzes", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.Contains(expectedKey, problem!.Errors.Keys);
        Assert.Empty((await client.GetFromJsonAsync<List<QuizSummary>>("/api/quizzes", Ct))!);
    }
}
