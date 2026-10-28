namespace LiveQuiz.Grains.Sessions;

/// <summary>
/// At most one push per interval: the first goes out immediately, later requests are folded into a single
/// timer tick. Used inside a grain, so it needs no locking.
/// </summary>
internal sealed class CoalescedPush(IGrainBase grain, TimeProvider time, Func<TimeSpan> interval, Func<Task> push)
{
    private IGrainTimer? _timer;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public void Queue()
    {
        if (_timer is not null)
            return;

        var wait = _last + interval() - time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
        {
            _ = Run();
            return;
        }

        _timer = grain.RegisterGrainTimer(async () =>
        {
            _timer?.Dispose();
            _timer = null;
            await Run();
        }, new GrainTimerCreationOptions(wait, Timeout.InfiniteTimeSpan));
    }

    public void Cancel()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task Run()
    {
        _last = time.GetUtcNow();
        await push();
    }
}
