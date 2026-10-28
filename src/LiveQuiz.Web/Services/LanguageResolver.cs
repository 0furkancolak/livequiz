using LiveQuiz.Web.Client.Localization;
using Microsoft.Net.Http.Headers;

namespace LiveQuiz.Web.Services;

/// <summary>Picks the UI language of a request: the <c>lq-lang</c> cookie wins, then Accept-Language, then English.</summary>
public static class LanguageResolver
{
    public const string CookieName = "lq-lang";

    public static string Resolve(HttpContext? context)
    {
        if (context is null)
            return "en";

        if (context.Request.Cookies.TryGetValue(CookieName, out var chosen) && Loc.Supported.Contains(chosen, StringComparer.OrdinalIgnoreCase))
            return Loc.Normalize(chosen);

        var preferred = context.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.Value?.Split('-')[0])
            .FirstOrDefault(l => Loc.Supported.Contains(l, StringComparer.OrdinalIgnoreCase));
        return Loc.Normalize(preferred);
    }
}
