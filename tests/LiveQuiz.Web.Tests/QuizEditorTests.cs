using AngleSharp.Dom;
using Bunit;
using LiveQuiz.Contracts.Quizzes;
using LiveQuiz.Web.Components.Host;
using LiveQuiz.Web.Host;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace LiveQuiz.Web.Tests;

public sealed class QuizEditorTests : BunitContext
{
    public QuizEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private IRenderedComponent<QuizEditor> RenderEditor(QuizDraft draft, Action<SaveQuizRequest>? onSave = null) =>
        Render<QuizEditor>(p => p
            .Add(c => c.Draft, draft)
            .Add(c => c.OnSave, (SaveQuizRequest request) => onSave?.Invoke(request)));

    private static QuizDraft ValidDraft()
    {
        var draft = QuizDraft.NewQuiz();
        draft.Title = "Capitals";
        draft.Questions[0].Text = "Capital of France?";
        draft.Questions[0].Options[0].Text = "Paris";
        draft.Questions[0].Options[1].Text = "Lyon";
        return draft;
    }

    private static IElement Button(IRenderedComponent<QuizEditor> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().Equals(text, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Renders_one_question_for_a_new_quiz()
    {
        var cut = RenderEditor(QuizDraft.NewQuiz());

        Assert.Single(cut.FindAll("[data-testid=question]"));
    }

    [Fact]
    public void Add_question_appends_a_question_panel()
    {
        var cut = RenderEditor(QuizDraft.NewQuiz());

        Button(cut, "Add question").Click();

        Assert.Equal(2, cut.FindAll("[data-testid=question]").Count);
    }

    [Fact]
    public void The_last_question_cannot_be_removed()
    {
        var cut = RenderEditor(QuizDraft.NewQuiz());

        Assert.True(cut.Find("button[aria-label='Remove question']").HasAttribute("disabled"));
    }

    [Fact]
    public void Removing_a_question_removes_its_panel()
    {
        var draft = QuizDraft.NewQuiz();
        draft.AddQuestion();
        var cut = RenderEditor(draft);

        cut.FindAll("button[aria-label='Remove question']")[0].Click();

        Assert.Single(cut.FindAll("[data-testid=question]"));
    }

    [Fact]
    public void Add_option_stops_at_the_maximum()
    {
        var cut = RenderEditor(QuizDraft.NewQuiz());

        Button(cut, "Add option").Click();
        Button(cut, "Add option").Click();

        Assert.True(Button(cut, "Add option").HasAttribute("disabled"));
        Assert.Equal(QuizLimits.MaxOptions, cut.FindAll("button[aria-label='Remove option']").Count);
    }

    [Fact]
    public void Saving_an_empty_quiz_shows_validation_messages_and_does_not_submit()
    {
        SaveQuizRequest? saved = null;
        var cut = RenderEditor(QuizDraft.NewQuiz(), r => saved = r);

        Button(cut, "Save quiz").Click();

        Assert.Null(saved);
        Assert.Contains("Title is required.", cut.Markup);
        Assert.Contains("Question text is required.", cut.Markup);
        Assert.Contains("Option text is required.", cut.Markup);
    }

    [Fact]
    public void Saving_a_valid_quiz_submits_a_request_with_one_correct_option()
    {
        SaveQuizRequest? saved = null;
        var cut = RenderEditor(ValidDraft(), r => saved = r);

        Button(cut, "Save quiz").Click();

        Assert.NotNull(saved);
        Assert.Equal("Capitals", saved.Title);
        var options = Assert.Single(saved.Questions).Options;
        Assert.Single(options, o => o.IsCorrect);
        Assert.DoesNotContain("is required", cut.Markup);
    }

    [Fact]
    public void Server_side_errors_are_displayed_next_to_the_field()
    {
        var errors = new Dictionary<string, string[]> { ["Title"] = ["Title already taken."] };

        var cut = Render<QuizEditor>(p => p
            .Add(c => c.Draft, ValidDraft())
            .Add(c => c.ServerErrors, errors));

        Assert.Contains("Title already taken.", cut.Markup);
    }
}
