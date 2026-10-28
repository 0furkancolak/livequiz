using System.Net.Http.Json;

namespace LiveQuiz.Api.Tests;

internal static class TestClient
{
    public const string Password = "Test-Password-1";

    /// <summary>Creates a client with its own cookie jar, registers a fresh host and signs in with the Identity cookie.</summary>
    public static async Task<HttpClient> SignedInAsync(ApiFactory factory) => (await SignedInWithCookieAsync(factory)).Client;

    /// <summary>Same, but also returns the raw cookie header so a SignalR connection can authenticate as that host.</summary>
    public static async Task<(HttpClient Client, string Cookie)> SignedInWithCookieAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        return (client, cookie);
    }
}
