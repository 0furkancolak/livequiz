using System.Net;
using System.Net.Http.Json;
using LiveQuiz.Contracts.Quizzes;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace LiveQuiz.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SessionEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<CreateSessionResponse> StartSession(HttpClient host)
    {
        var quiz = await (await host.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(3), Ct)).Content.ReadFromJsonAsync<QuizDetail>(Ct);
        var response = await host.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(quiz!.Id), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateSessionResponse>(Ct))!;
    }

    [Fact]
    public async Task Host_starts_a_session_for_their_own_quiz_and_gets_a_pin()
    {
        using var host = await TestClient.SignedInAsync(factory);

        var session = await StartSession(host);

        Assert.Equal(SessionLimits.PinLength, session.Pin.Length);
        Assert.All(session.Pin, c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Fact]
    public async Task Another_host_cannot_start_a_session_for_someone_elses_quiz()
    {
        using var owner = await TestClient.SignedInAsync(factory);
        using var other = await TestClient.SignedInAsync(factory);
        var quiz = await (await owner.PostAsJsonAsync("/api/quizzes", QuizFactory.Quiz(2), Ct)).Content.ReadFromJsonAsync<QuizDetail>(Ct);

        var response = await other.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(quiz!.Id), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_start_a_session()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Player_joins_with_pin_and_nickname_without_signing_in()
    {
        using var host = await TestClient.SignedInAsync(factory);
        var session = await StartSession(host);
        using var player = factory.CreateClient();

        var response = await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, "Sam"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var joined = await response.Content.ReadFromJsonAsync<JoinSessionResponse>(Ct);
        Assert.Equal(session.SessionId, joined!.SessionId);
        Assert.Equal("Sam", joined.Nickname);
        Assert.NotEqual(Guid.Empty, joined.PlayerId);
        Assert.False(string.IsNullOrEmpty(joined.ReconnectToken));
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("")]
    public async Task Unknown_or_malformed_pins_are_not_found(string pin)
    {
        using var player = factory.CreateClient();

        var response = await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(pin, "Sam"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_nickname_is_a_conflict_and_invalid_nickname_is_a_validation_error()
    {
        using var host = await TestClient.SignedInAsync(factory);
        var session = await StartSession(host);
        using var player = factory.CreateClient();
        await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, "Sam"), Ct);

        var duplicate = await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, "sam"), Ct);
        var invalid = await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, "x"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.Contains("Nickname", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Only_the_owner_can_cancel_and_cancelling_frees_the_pin()
    {
        using var host = await TestClient.SignedInAsync(factory);
        using var other = await TestClient.SignedInAsync(factory);
        var session = await StartSession(host);

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/sessions/{session.SessionId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync($"/api/sessions/{session.SessionId}", Ct)).StatusCode);

        using var player = factory.CreateClient();
        var join = await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, "Sam"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, join.StatusCode);
    }

    [Fact]
    public async Task Session_and_player_state_is_persisted_in_postgres()
    {
        using var host = await TestClient.SignedInAsync(factory);
        var session = await StartSession(host);
        using var player = factory.CreateClient();
        await player.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(session.Pin, "Sam"), Ct);

        await using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand(
            "select distinct graintypestring from orleansstorage",
            connection);
        var types = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            types.Add(reader.GetString(0));

        Assert.Contains(types, t => t.StartsWith("session", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(types, t => t.StartsWith("player", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(types, t => t == "pin");
    }
}
