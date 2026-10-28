using LiveQuiz.Web.Client.Lobby;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveQuiz.Web.Tests;

public sealed class ReconnectPolicyTests
{
    private static TimeSpan? Next(int previousRetries, TimeSpan elapsed) =>
        new ReconnectPolicy().NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetries, ElapsedTime = elapsed });

    [Fact]
    public void The_first_attempts_come_quickly()
    {
        Assert.Equal(TimeSpan.Zero, Next(0, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(1), Next(1, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(2), Next(2, TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(5), Next(3, TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public void Later_attempts_stay_close_together_with_jitter_so_a_returning_server_is_found_quickly()
    {
        var delays = Enumerable.Range(4, 40).Select(n => Next(n, TimeSpan.FromSeconds(30))!.Value).ToList();

        Assert.All(delays, d => Assert.InRange(d, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7)));
        Assert.True(delays.Distinct().Count() > 1, "delays should not all be equal");
    }

    [Fact]
    public void It_gives_up_after_five_minutes()
    {
        Assert.NotNull(Next(30, ReconnectPolicy.GiveUpAfter - TimeSpan.FromSeconds(1)));
        Assert.Null(Next(30, ReconnectPolicy.GiveUpAfter));
    }
}
