namespace LiveQuiz.Contracts.Quizzes;

/// <summary>
/// Pure validation of a <see cref="SaveQuizRequest"/>. Keys follow the ProblemDetails convention
/// (<c>Questions[0].Options</c>) so the API and the editor can display errors the same way.
/// </summary>
public static class QuizValidator
{
    public static IReadOnlyDictionary<string, string[]> Validate(SaveQuizRequest? request)
    {
        var errors = new Dictionary<string, List<string>>();

        if (request is null)
        {
            Add(errors, "Request", "Request body is required.");
            return Freeze(errors);
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            Add(errors, "Title", "Title is required.");
        else if (title.Length > QuizLimits.MaxTitleLength)
            Add(errors, "Title", $"Title must be at most {QuizLimits.MaxTitleLength} characters.");

        if (request.Description is { Length: > QuizLimits.MaxDescriptionLength })
            Add(errors, "Description", $"Description must be at most {QuizLimits.MaxDescriptionLength} characters.");

        var questions = request.Questions;
        if (questions is null || questions.Count == 0)
        {
            Add(errors, "Questions", "A quiz needs at least one question.");
            return Freeze(errors);
        }

        if (questions.Count > QuizLimits.MaxQuestions)
            Add(errors, "Questions", $"A quiz can have at most {QuizLimits.MaxQuestions} questions.");

        for (var i = 0; i < questions.Count; i++)
            ValidateQuestion(errors, $"Questions[{i}]", questions[i]);

        return Freeze(errors);
    }

    private static void ValidateQuestion(Dictionary<string, List<string>> errors, string path, SaveQuestion? question)
    {
        if (question is null)
        {
            Add(errors, path, "Question is required.");
            return;
        }

        var text = question.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            Add(errors, $"{path}.Text", "Question text is required.");
        else if (text.Length > QuizLimits.MaxQuestionTextLength)
            Add(errors, $"{path}.Text", $"Question text must be at most {QuizLimits.MaxQuestionTextLength} characters.");

        if (question.TimeLimitSeconds is < QuizLimits.MinTimeLimitSeconds or > QuizLimits.MaxTimeLimitSeconds)
            Add(errors, $"{path}.TimeLimitSeconds",
                $"Time limit must be between {QuizLimits.MinTimeLimitSeconds} and {QuizLimits.MaxTimeLimitSeconds} seconds.");

        var options = question.Options;
        if (options is null || options.Count is < QuizLimits.MinOptions or > QuizLimits.MaxOptions)
        {
            Add(errors, $"{path}.Options",
                $"A question needs between {QuizLimits.MinOptions} and {QuizLimits.MaxOptions} options.");
            return;
        }

        for (var j = 0; j < options.Count; j++)
        {
            var optionText = options[j]?.Text?.Trim();
            if (string.IsNullOrEmpty(optionText))
                Add(errors, $"{path}.Options[{j}].Text", "Option text is required.");
            else if (optionText.Length > QuizLimits.MaxOptionTextLength)
                Add(errors, $"{path}.Options[{j}].Text", $"Option text must be at most {QuizLimits.MaxOptionTextLength} characters.");
        }

        if (options.Count(o => o is { IsCorrect: true }) != 1)
            Add(errors, $"{path}.Options", "Exactly one option must be marked correct.");
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
            errors[key] = list = [];
        list.Add(message);
    }

    private static Dictionary<string, string[]> Freeze(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}
