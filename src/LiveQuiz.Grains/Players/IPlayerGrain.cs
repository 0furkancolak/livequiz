namespace LiveQuiz.Grains.Players;

/// <summary>Key: player id, key extension: session id ("N" format).</summary>
public interface IPlayerGrain : IGrainWithGuidCompoundKey
{
    Task Initialize(string nickname, byte[] reconnectTokenHash);

    /// <summary>Constant-time check of a reconnect token against the stored hash.</summary>
    Task<bool> Verify(string reconnectToken);

    Task SetConnected(bool connected);

    Task<bool> IsConnected();
}

public static class PlayerKeys
{
    public static string Extension(Guid sessionId) => sessionId.ToString("N");
}
