namespace LiveQuiz.Grains.Tests;

[Collection(ClusterCollection.Name)]
public class GrainMetricsTests(ClusterFixture fixture)
{
    [Fact]
    public async Task Writing_state_is_counted_and_timed_per_grain_kind()
    {
        var before = GrainMetrics.Snapshot();
        var session = fixture.Grains.GetGrain<LiveQuiz.Grains.Sessions.ISessionGrain>(Guid.CreateVersion7());

        await session.Open("owner", Guid.NewGuid(), "Quiz", GameFixtures.Questions());
        await session.Join("Player 1");

        var after = GrainMetrics.Snapshot();
        Assert.True(after["session"].Writes >= before["session"].Writes + 2, "open and join each write the session");
        Assert.True(after["session"].MaxMs >= 0);
        Assert.True(after["player"].Writes >= before["player"].Writes);
    }
}
