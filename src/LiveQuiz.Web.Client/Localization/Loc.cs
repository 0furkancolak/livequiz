using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveQuiz.Web.Client.Localization;

/// <summary>
/// UI text lookup. English is the source language: a string is its own key and is returned unchanged when there is no
/// translation, so a missing entry degrades to English instead of showing a key. The language is chosen per request (cookie, then Accept-Language) on the server and read from the page's lang attribute in the browser.
/// Cultures are deliberately not used: the images run with invariant globalization.
/// </summary>
public sealed class Loc(string language)
{
    public string Language { get; } = Normalize(language);

    public static readonly string[] Supported = ["en", "tr"];

    // Messages built on the server or in shared code carry numbers inside the sentence, so they are matched by shape.
    private static readonly (Regex Pattern, string Template)[] Patterns =
        [.. Turkish.Entries.Where(e => e.Key.Contains('{'))
            .Select(e => (new Regex("^" + Regex.Escape(e.Key).Replace(@"\{0}", "(.+?)").Replace(@"\{1}", "(.+?)") + "$"), e.Value))];

    public static string Normalize(string? language) =>
        Supported.FirstOrDefault(s => string.Equals(s, language, StringComparison.OrdinalIgnoreCase)) ?? "en";

    public string this[string text] => Translate(text);

    public string Format(string template, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Translate(template), args);

    /// <summary>Translates a finished sentence, e.g. an error message produced by shared validation code.</summary>
    public string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text) || Language != "tr")
            return text ?? string.Empty;
        if (Turkish.Entries.TryGetValue(text, out var exact))
            return exact;

        foreach (var (pattern, template) in Patterns)
        {
            var match = pattern.Match(text);
            if (match.Success)
                return string.Format(CultureInfo.InvariantCulture, template, match.Groups.Cast<Group>().Skip(1).Select(g => (object)g.Value).ToArray());
        }

        return text;
    }
}
