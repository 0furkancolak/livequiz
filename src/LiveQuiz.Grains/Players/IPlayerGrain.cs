namespace LiveQuiz.Grains.Players;

/// <summary>Key: player id, key extension: session id ("N" format).</summary>
public interface IPlayerGrain : IGrainWithGuidCompoundKey
{
    Task Initialize(string nickname, byte[] reconnectTokenHash);

    /// <summary>Constant-time check of a reconnect token against the stored hash.</summary>
    Task<bool> Verify(string reconnectToken);

    /// <summary>Stores the outcome of one question (called by the session when the question closes).</summary>
    Task RecordAnswer(int questionIndex, int optionIndex, int points, int elapsedMs);

    Task<PlayerScore> GetScore();
}

public static class PlayerKeys
{
    public static string Extension(Guid sessionId) => sessionId.ToString("N");
}

[GenerateSerializer, Immutable]
public sealed record PlayerScore([property: Id(0)] int TotalPoints, [property: Id(1)] int AnswersRecorded);
