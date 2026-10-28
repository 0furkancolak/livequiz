namespace LiveQuiz.Grains.Pins;

/// <summary>Key: the PIN string. Maps one PIN to at most one live session and keeps two sessions from sharing it.</summary>
public interface IPinRegistryGrain : IGrainWithStringKey
{
    /// <summary>True when the PIN was free (or already held by this same session).</summary>
    Task<bool> TryReserve(Guid sessionId);

    Task<Guid?> Resolve();

    Task Release();
}
