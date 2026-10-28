using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiveQuiz.Contracts.Quizzes;

namespace LiveQuiz.Web.Services;

public sealed record ApiResult<T>(HttpStatusCode Status, T? Value, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;
    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;
    public bool IsNotFound => Status == HttpStatusCode.NotFound;
}

/// <summary>Server-side typed client for the Api, acting as the signed-in host via <see cref="ApiSession"/>.</summary>
public sealed class ApiClient(HttpClient http, ApiSession session)
{
    public Task<ApiResult<List<QuizSummary>>> ListQuizzesAsync(CancellationToken ct = default) =>
        SendAsync<List<QuizSummary>>(HttpMethod.Get, "/api/quizzes", null, ct);

    public Task<ApiResult<QuizDetail>> GetQuizAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<QuizDetail>(HttpMethod.Get, $"/api/quizzes/{id}", null, ct);

    public Task<ApiResult<QuizDetail>> CreateQuizAsync(SaveQuizRequest request, CancellationToken ct = default) =>
        SendAsync<QuizDetail>(HttpMethod.Post, "/api/quizzes", request, ct);

    public Task<ApiResult<QuizDetail>> UpdateQuizAsync(Guid id, SaveQuizRequest request, CancellationToken ct = default) =>
        SendAsync<QuizDetail>(HttpMethod.Put, $"/api/quizzes/{id}", request, ct);

    public Task<ApiResult<object>> DeleteQuizAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"/api/quizzes/{id}", null, ct);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (session.CookieHeader is { } cookie)
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        using var response = await http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            var value = response.Content.Headers.ContentLength is 0 || response.StatusCode == HttpStatusCode.NoContent
                ? default
                : await response.Content.ReadFromJsonAsync<T>(ct);
            return new ApiResult<T>(response.StatusCode, value);
        }

        return new ApiResult<T>(response.StatusCode, default, await ReadErrorsAsync(response, ct));
    }

    private static async Task<IReadOnlyDictionary<string, string[]>?> ReadErrorsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest)
            return null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(ct);
            return problem?.Errors;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
