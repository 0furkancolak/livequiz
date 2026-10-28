using LiveQuiz.Grains.Pins;

namespace LiveQuiz.Grains.Tests;

[Collection(ClusterCollection.Name)]
public class PinRegistryGrainTests(ClusterFixture fixture)
{
    private IPinRegistryGrain Registry(string pin) => fixture.Grains.GetGrain<IPinRegistryGrain>(pin);

    [Fact]
    public async Task A_taken_pin_cannot_be_reserved_by_another_session()
    {
        var pin = Guid.NewGuid().ToString("N");
        var first = Guid.NewGuid();

        Assert.True(await Registry(pin).TryReserve(first));
        Assert.False(await Registry(pin).TryReserve(Guid.NewGuid()));
        Assert.Equal(first, await Registry(pin).Resolve());
    }

    [Fact]
    public async Task Reserving_again_for_the_same_session_is_idempotent()
    {
        var pin = Guid.NewGuid().ToString("N");
        var session = Guid.NewGuid();

        Assert.True(await Registry(pin).TryReserve(session));
        Assert.True(await Registry(pin).TryReserve(session));
    }

    [Fact]
    public async Task A_released_pin_can_be_reused()
    {
        var pin = Guid.NewGuid().ToString("N");
        await Registry(pin).TryReserve(Guid.NewGuid());

        await Registry(pin).Release();

        Assert.Null(await Registry(pin).Resolve());
        var next = Guid.NewGuid();
        Assert.True(await Registry(pin).TryReserve(next));
        Assert.Equal(next, await Registry(pin).Resolve());
    }
}
