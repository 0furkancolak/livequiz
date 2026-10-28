using LiveQuiz.Contracts.Quizzes;

namespace LiveQuiz.Web.Host;

/// <summary>Mutable editing model for the quiz editor; converts to/from the immutable API contracts.</summary>
public sealed class QuizDraft
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public List<QuestionDraft> Questions { get; } = [];

    public static QuizDraft NewQuiz()
    {
        var draft = new QuizDraft();
        draft.AddQuestion();
        return draft;
    }

    public static QuizDraft From(QuizDetail quiz)
    {
        var draft = new QuizDraft { Title = quiz.Title, Description = quiz.Description };
        foreach (var q in quiz.Questions)
        {
            var question = new QuestionDraft { Text = q.Text, TimeLimitSeconds = q.TimeLimitSeconds };
            foreach (var o in q.Options)
                question.Options.Add(new OptionDraft { Text = o.Text });
            question.CorrectIndex = Math.Max(0, q.Options.ToList().FindIndex(o => o.IsCorrect));
            draft.Questions.Add(question);
        }
        return draft;
    }

    public QuestionDraft AddQuestion()
    {
        var question = new QuestionDraft();
        for (var i = 0; i < QuizLimits.MinOptions; i++)
            question.Options.Add(new OptionDraft());
        Questions.Add(question);
        return question;
    }

    public SaveQuizRequest ToRequest() => new(
        Title,
        Description,
        Questions.Select(q => new SaveQuestion(
            q.Text,
            q.TimeLimitSeconds,
            q.Options.Select((o, i) => new SaveOption(o.Text, i == q.CorrectIndex)).ToList())).ToList());
}

public sealed class QuestionDraft
{
    public string Text { get; set; } = "";
    public int TimeLimitSeconds { get; set; } = QuizLimits.DefaultTimeLimitSeconds;
    public int CorrectIndex { get; set; }
    public List<OptionDraft> Options { get; } = [];

    public bool CanAddOption => Options.Count < QuizLimits.MaxOptions;
    public bool CanRemoveOption => Options.Count > QuizLimits.MinOptions;

    public void AddOption()
    {
        if (CanAddOption)
            Options.Add(new OptionDraft());
    }

    public void RemoveOption(int index)
    {
        if (!CanRemoveOption || index < 0 || index >= Options.Count)
            return;

        Options.RemoveAt(index);
        // Keep the marked answer pointing at the same option (or the first one if it was the removed one).
        if (CorrectIndex == index)
            CorrectIndex = 0;
        else if (CorrectIndex > index)
            CorrectIndex--;
    }
}

public sealed class OptionDraft
{
    public string Text { get; set; } = "";
}
