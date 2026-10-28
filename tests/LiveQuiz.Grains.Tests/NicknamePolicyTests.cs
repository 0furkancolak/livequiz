using LiveQuiz.Contracts.Sessions;

namespace LiveQuiz.Grains.Tests;

public class NicknamePolicyTests
{
    [Theory]
    [InlineData("Sam", NicknameError.None)]
    [InlineData("Ayşe_99", NicknameError.None)]
    [InlineData("  Sam  ", NicknameError.None)]
    [InlineData(null, NicknameError.Empty)]
    [InlineData("   ", NicknameError.Empty)]
    [InlineData("x", NicknameError.TooShort)]
    [InlineData("12345678901234567890x", NicknameError.TooLong)]
    [InlineData("a\tb\u0007", NicknameError.InvalidCharacters)]
    [InlineData("😀😀", NicknameError.InvalidCharacters)]
    [InlineData("F.u.c.k", NicknameError.Profanity)]
    [InlineData("SİKTİR", NicknameError.Profanity)]
    public void Validate(string? input, NicknameError expected) => Assert.Equal(expected, NicknamePolicy.Validate(input));

    [Fact]
    public void Normalize_trims_and_collapses_whitespace() =>
        Assert.Equal("a b c", NicknamePolicy.Normalize("  a   b \t c "));

    [Fact]
    public void Uniqueness_key_ignores_case_accents_and_spacing() =>
        Assert.Equal(NicknamePolicy.UniquenessKey("Ayşe Yılmaz"), NicknamePolicy.UniquenessKey("ayse  yilmaz"));

    [Fact]
    public void Every_error_has_a_message()
    {
        foreach (var error in Enum.GetValues<NicknameError>().Where(e => e != NicknameError.None))
            Assert.False(string.IsNullOrEmpty(NicknamePolicy.Message(error)));
    }
}
