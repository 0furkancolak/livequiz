using LiveQuiz.Contracts.Quizzes;
using LiveQuiz.Web.Host;

namespace LiveQuiz.Web.Tests;

public sealed class QuizDraftTests
{
    private static QuestionDraft FourOptions()
    {
        var question = new QuestionDraft();
        for (var i = 0; i < 4; i++)
            question.Options.Add(new OptionDraft { Text = $"o{i}" });
        return question;
    }

    [Fact]
    public void New_quiz_starts_with_one_question_with_the_minimum_options()
    {
        var draft = QuizDraft.NewQuiz();

        var question = Assert.Single(draft.Questions);
        Assert.Equal(QuizLimits.MinOptions, question.Options.Count);
        Assert.Equal(QuizLimits.DefaultTimeLimitSeconds, question.TimeLimitSeconds);
    }

    [Theory]
    [InlineData(2, 1, 1)] // removing an option before the correct one shifts the marker
    [InlineData(1, 1, 0)] // removing the correct option falls back to the first
    [InlineData(1, 3, 1)] // removing an option after the correct one keeps the marker
    public void Removing_an_option_keeps_the_correct_marker_consistent(int correct, int remove, int expected)
    {
        var question = FourOptions();
        question.CorrectIndex = correct;

        question.RemoveOption(remove);

        Assert.Equal(3, question.Options.Count);
        Assert.Equal(expected, question.CorrectIndex);
    }

    [Fact]
    public void Options_stay_within_limits()
    {
        var question = FourOptions();
        question.AddOption();
        Assert.Equal(QuizLimits.MaxOptions, question.Options.Count);

        question.RemoveOption(0);
        question.RemoveOption(0);
        question.RemoveOption(0);
        Assert.Equal(QuizLimits.MinOptions, question.Options.Count);
    }

    [Fact]
    public void ToRequest_marks_exactly_the_selected_option_correct()
    {
        var draft = new QuizDraft { Title = "T" };
        var question = FourOptions();
        question.Text = "Q";
        question.CorrectIndex = 2;
        draft.Questions.Add(question);

        var request = draft.ToRequest();

        var options = Assert.Single(request.Questions).Options;
        Assert.Equal([false, false, true, false], options.Select(o => o.IsCorrect));
    }

    [Fact]
    public void Round_trips_through_QuizDetail()
    {
        var detail = new QuizDetail(
            Guid.NewGuid(), "T", "D",
            [new QuestionDto(Guid.NewGuid(), "Q", 30, [new OptionDto(Guid.NewGuid(), "a", false), new OptionDto(Guid.NewGuid(), "b", true)])],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var request = QuizDraft.From(detail).ToRequest();

        Assert.Equal("T", request.Title);
        Assert.Equal(30, request.Questions[0].TimeLimitSeconds);
        Assert.Equal([false, true], request.Questions[0].Options.Select(o => o.IsCorrect));
    }
}
