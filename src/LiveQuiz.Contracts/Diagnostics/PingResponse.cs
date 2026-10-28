namespace LiveQuiz.Contracts.Diagnostics;

/// <summary>Response of the API liveness probe used to prove the Web -> Api -> Postgres trace path.</summary>
public sealed record PingResponse(string Service, bool DatabaseReachable, DateTimeOffset ServerTimeUtc);
