using System.Diagnostics;
using System.Diagnostics.Metrics;
using Orleans.Runtime;

namespace LiveQuiz.Grains;

/// <summary>
/// Counts and times grain state writes, so the cost of persisting a game is a number and not a guess (ADR 0012, ADR 0015).
/// The meter feeds OpenTelemetry; <see cref="Snapshot"/> serves the same totals to the load test.
/// </summary>
public static class GrainMetrics
{
    public const string MeterName = "LiveQuiz";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Writes = Meter.CreateCounter<long>("livequiz.grain.state_writes", description: "Grain state writes");
    private static readonly Histogram<double> WriteMs = Meter.CreateHistogram<double>("livequiz.grain.state_write.duration", "ms", "Grain state write time");

    private static readonly Dictionary<string, Totals> ByGrain = new()
    {
        ["session"] = new(), ["player"] = new(), ["pin"] = new()
    };

    private sealed class Totals
    {
        public long Count;
        public long Ticks;
        public long MaxTicks;
    }

    public static async Task WriteTimedAsync<T>(this IPersistentState<T> state, string grain)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await state.WriteStateAsync();
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Writes.Add(1, new KeyValuePair<string, object?>("grain", grain));
            WriteMs.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("grain", grain));

            var totals = ByGrain[grain];
            Interlocked.Increment(ref totals.Count);
            Interlocked.Add(ref totals.Ticks, elapsed.Ticks);
            long max;
            while (elapsed.Ticks > (max = Interlocked.Read(ref totals.MaxTicks))
                   && Interlocked.CompareExchange(ref totals.MaxTicks, elapsed.Ticks, max) != max)
            {
            }
        }
    }

    public static IReadOnlyDictionary<string, WriteStats> Snapshot() =>
        ByGrain.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var count = Interlocked.Read(ref kv.Value.Count);
                var total = TimeSpan.FromTicks(Interlocked.Read(ref kv.Value.Ticks));
                return new WriteStats(count, count == 0 ? 0 : total.TotalMilliseconds / count, TimeSpan.FromTicks(Interlocked.Read(ref kv.Value.MaxTicks)).TotalMilliseconds);
            });
}

public sealed record WriteStats(long Writes, double AverageMs, double MaxMs);
