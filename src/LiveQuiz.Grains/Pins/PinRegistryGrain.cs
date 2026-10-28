namespace LiveQuiz.Grains.Pins;

[GenerateSerializer]
public sealed class PinRegistryState
{
    [Id(0)] public Guid? SessionId { get; set; }
}

public sealed class PinRegistryGrain(
    [PersistentState("pin")] IPersistentState<PinRegistryState> state) : Grain, IPinRegistryGrain
{
    public async Task<bool> TryReserve(Guid sessionId)
    {
        if (state.State.SessionId is { } owner)
            return owner == sessionId;

        state.State.SessionId = sessionId;
        await state.WriteTimedAsync("pin");
        return true;
    }

    public Task<Guid?> Resolve() => Task.FromResult(state.State.SessionId);

    public async Task Release()
    {
        if (state.State.SessionId is null)
            return;

        await state.ClearStateAsync();
    }
}
