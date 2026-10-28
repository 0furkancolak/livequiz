using System.Security.Cryptography;
using System.Text;

namespace LiveQuiz.Grains.Players;

[GenerateSerializer]
public sealed class PlayerState
{
    [Id(0)] public string Nickname { get; set; } = string.Empty;
    [Id(1)] public byte[] ReconnectTokenHash { get; set; } = [];
    [Id(2)] public bool Connected { get; set; }
}

public sealed class PlayerGrain(
    [PersistentState("player")] IPersistentState<PlayerState> state) : Grain, IPlayerGrain
{
    public async Task Initialize(string nickname, byte[] reconnectTokenHash)
    {
        state.State.Nickname = nickname;
        state.State.ReconnectTokenHash = reconnectTokenHash;
        state.State.Connected = false;
        await state.WriteStateAsync();
    }

    public Task<bool> Verify(string reconnectToken)
    {
        if (state.State.ReconnectTokenHash.Length == 0 || string.IsNullOrEmpty(reconnectToken))
            return Task.FromResult(false);

        var hash = HashToken(reconnectToken);
        return Task.FromResult(CryptographicOperations.FixedTimeEquals(hash, state.State.ReconnectTokenHash));
    }

    public async Task SetConnected(bool connected)
    {
        if (state.State.Connected == connected)
            return;

        state.State.Connected = connected;
        await state.WriteStateAsync();
    }

    public Task<bool> IsConnected() => Task.FromResult(state.State.Connected);

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
