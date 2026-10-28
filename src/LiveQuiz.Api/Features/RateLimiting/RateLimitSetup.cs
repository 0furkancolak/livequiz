using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace LiveQuiz.Api.Features.RateLimiting;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Login and registration attempts per client IP and minute.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Failed PIN lookups per client IP and minute; successful joins are not counted (ADR 0014).</summary>
    public int JoinFailuresPerMinute { get; set; } = 10;

    /// <summary>SubmitAnswer invocations per hub connection and second.</summary>
    public int AnswersPerSecond { get; set; } = 5;
}

public static class RateLimitSetup
{
    public const string AuthPolicy = "auth";

    private static readonly string[] DefaultTrustedProxies =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.Section));
        var limits = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddSingleton<JoinAttemptLimiter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(retryAfter);
                return ValueTask.CompletedTask;
            };
        });

        // Browser traffic reaches the Api through the Web proxy (YARP), which appends the real client address to
        // X-Forwarded-For. The header is only believed when the direct peer is a trusted proxy, otherwise anyone could
        // pick their own rate-limit bucket.
        var trusted = configuration.GetSection("Network:TrustedProxies").Get<string[]>() ?? DefaultTrustedProxies;
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var network in trusted)
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        return services;
    }

    /// <summary>The rate-limit partition of a request: the client address after forwarded headers were applied.</summary>
    public static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static string RetryAfterSeconds(TimeSpan retryAfter) =>
        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Throttles PIN guessing. Only failed lookups consume permits, so a classroom of honest players behind one school
/// IP is never locked out by its own successful joins, while a scan of the 10⁶ PIN space is cut to a crawl.
/// </summary>
public sealed class JoinAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public JoinAttemptLimiter(IOptions<RateLimitOptions> options)
    {
        var permits = options.Value.JoinFailuresPerMinute;
        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    /// <summary>Checks the budget without spending any of it.</summary>
    public bool IsBlocked(string key, out TimeSpan retryAfter)
    {
        using var lease = _limiter.AttemptAcquire(key, 0);
        if (lease.IsAcquired)
        {
            retryAfter = TimeSpan.Zero;
            return false;
        }

        if (!lease.TryGetMetadata(MetadataName.RetryAfter, out retryAfter))
            retryAfter = TimeSpan.FromMinutes(1);
        return true;
    }

    public void RecordFailure(string key)
    {
        using var lease = _limiter.AttemptAcquire(key);
    }

    public void Dispose() => _limiter.Dispose();
}

/// <summary>Fixed-window counter for one hub connection; SignalR serialises a connection's invocations, so no lock.</summary>
public sealed class ConnectionRateLimit(int limit, TimeSpan window, TimeProvider time)
{
    private long _windowStart = time.GetTimestamp();
    private int _count;

    public bool TryAcquire()
    {
        var now = time.GetTimestamp();
        if (time.GetElapsedTime(_windowStart, now) >= window)
        {
            _windowStart = now;
            _count = 0;
        }
        return ++_count <= limit;
    }
}
