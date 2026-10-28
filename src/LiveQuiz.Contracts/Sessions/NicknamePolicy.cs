using System.Globalization;
using System.Text;

namespace LiveQuiz.Contracts.Sessions;

public enum NicknameError
{
    None,
    Empty,
    TooShort,
    TooLong,
    InvalidCharacters,
    Profanity
}

/// <summary>Pure nickname rules shared by the server (authoritative) and the player UI (early feedback).</summary>
public static class NicknamePolicy
{
    // Deliberately small and substring-based: it catches the obvious cases, it is not a moderation system.
    private static readonly string[] Blocked =
    [
        "fuck", "shit", "bitch", "asshole", "cunt", "nigger", "nigga",
        "orospu", "siktir", "yarrak", "amcik", "ibne", "gotveren"
    ];

    /// <summary>Trims and collapses inner whitespace. The result is what is stored and shown.</summary>
    public static string Normalize(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return string.Empty;
        return string.Join(' ', nickname.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static NicknameError Validate(string? nickname)
    {
        var name = Normalize(nickname);
        if (name.Length == 0)
            return NicknameError.Empty;
        if (name.Length < SessionLimits.MinNickname)
            return NicknameError.TooShort;
        if (name.Length > SessionLimits.MaxNickname)
            return NicknameError.TooLong;
        if (!name.All(IsAllowed))
            return NicknameError.InvalidCharacters;

        var folded = Fold(name);
        return Blocked.Any(folded.Contains) ? NicknameError.Profanity : NicknameError.None;
    }

    /// <summary>Case- and accent-insensitive key used for per-session uniqueness ("Ayşe" and "ayse" collide).</summary>
    public static string UniquenessKey(string nickname) => Fold(Normalize(nickname));

    public static string Message(NicknameError error) => error switch
    {
        NicknameError.Empty => "Enter a nickname.",
        NicknameError.TooShort => $"Nickname must be at least {SessionLimits.MinNickname} characters.",
        NicknameError.TooLong => $"Nickname can be at most {SessionLimits.MaxNickname} characters.",
        NicknameError.InvalidCharacters => "Use letters, digits, spaces, '_', '-' or '.' only.",
        NicknameError.Profanity => "Please choose a different nickname.",
        _ => string.Empty
    };

    private static bool IsAllowed(char c) => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.';

    // Explicit Turkish/German folds first: string normalization is a no-op under invariant globalization (e.g. WASM, trimmed hosts).
    private static string Fold(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            switch (c)
            {
                case 'ı' or 'İ': sb.Append('i'); break;
                case 'ş' or 'Ş': sb.Append('s'); break;
                case 'ğ' or 'Ğ': sb.Append('g'); break;
                case 'ç' or 'Ç': sb.Append('c'); break;
                case 'ö' or 'Ö': sb.Append('o'); break;
                case 'ü' or 'Ü': sb.Append('u'); break;
                case 'ß': sb.Append("ss"); break;
                default:
                    if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                        sb.Append(char.ToLowerInvariant(c));
                    break;
            }
        }
        return sb.ToString();
    }
}
