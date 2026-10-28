using LiveQuiz.Contracts.Quizzes;
using LiveQuiz.Contracts.Sessions;
using LiveQuiz.Web.Client.Localization;

namespace LiveQuiz.Web.Tests;

public sealed class LocTests
{
    [Fact]
    public void English_returns_the_text_unchanged()
    {
        var loc = new Loc("en");
        Assert.Equal("Join a game", loc["Join a game"]);
        Assert.Equal("Rank 2 of 5", loc.Format("Rank {0} of {1}", 2, 5));
    }

    [Fact]
    public void Unknown_language_falls_back_to_English()
    {
        Assert.Equal("en", new Loc("de").Language);
        Assert.Equal("tr", new Loc("TR").Language);
    }

    [Fact]
    public void Turkish_translates_keys_and_formats_templates()
    {
        var loc = new Loc("tr");
        Assert.Equal("Oyuna katıl", loc["Join a game"]);
        Assert.Equal("Sıra 2 / 5", loc.Format("Rank {0} of {1}", 2, 5));
        Assert.Equal("Not in the dictionary", loc["Not in the dictionary"]);
    }

    [Fact]
    public void Turkish_translates_messages_built_by_shared_code()
    {
        var loc = new Loc("tr");
        Assert.Equal("Bir takma ad gir.", loc.Translate(NicknamePolicy.Message(NicknameError.Empty)));
        Assert.Equal(
            loc.Translate(NicknamePolicy.Message(NicknameError.TooShort)),
            $"Takma ad en az {SessionLimits.MinNickname} karakter olmalı.");
        Assert.Equal(
            $"Başlık en fazla {QuizLimits.MaxTitleLength} karakter olmalı.",
            loc.Translate($"Title must be at most {QuizLimits.MaxTitleLength} characters."));
    }

    [Fact]
    public void Every_Turkish_entry_keeps_the_placeholders_of_its_key()
    {
        foreach (var (key, value) in Turkish.Entries)
            Assert.Equal(
                System.Text.RegularExpressions.Regex.Matches(key, @"\{\d\}").Select(m => m.Value).Order(),
                System.Text.RegularExpressions.Regex.Matches(value, @"\{\d\}").Select(m => m.Value).Order());
    }
}
