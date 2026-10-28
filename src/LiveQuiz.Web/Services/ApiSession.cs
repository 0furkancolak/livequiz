namespace LiveQuiz.Web.Services;

/// <summary>
/// Holds the host's Identity cookie for the lifetime of a DI scope (a Blazor circuit) so server-side API calls
/// can act as the signed-in host. The cookie is read from the request that created the scope and never sent to the browser.
/// </summary>
public sealed class ApiSession(IHttpContextAccessor accessor)
{
    internal const string IdentityCookiePrefix = ".AspNetCore.Identity.Application";

    private string? _cookieHeader;
    private bool _captured;

    public string? CookieHeader
    {
        get
        {
            Capture();
            return _cookieHeader;
        }
    }

    public void Capture()
    {
        if (_captured || accessor.HttpContext is not { } context)
            return;

        // The cookie can be chunked (…Application, …ApplicationC1, …); forward all chunks, nothing else.
        var pairs = context.Request.Cookies
            .Where(c => c.Key.StartsWith(IdentityCookiePrefix, StringComparison.Ordinal))
            .Select(c => $"{c.Key}={c.Value}");
        var header = string.Join("; ", pairs);
        _cookieHeader = header.Length == 0 ? null : header;
        _captured = true;
    }
}
