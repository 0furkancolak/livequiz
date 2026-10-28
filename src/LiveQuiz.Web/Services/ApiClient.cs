using LiveQuiz.Contracts.Diagnostics;

namespace LiveQuiz.Web.Services;

/// <summary>Server-side typed client for the Api; resolved through Aspire service discovery.</summary>
public sealed class ApiClient(HttpClient http)
{
    public Task<PingResponse?> PingAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<PingResponse>("/api/ping", ct);
}
