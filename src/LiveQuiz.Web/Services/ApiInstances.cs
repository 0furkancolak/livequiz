namespace LiveQuiz.Web.Services;

/// <summary>
/// The Api instances this Web app can reach (<c>Api:Instances</c>, a comma-separated list of service names; just
/// <c>api</c> when there is one). The browser's traffic is spread by YARP; the server-side calls Web makes itself
/// take the instances in turn through <see cref="Next"/>.
/// </summary>
public sealed class ApiInstances
{
    private readonly string[] _names;
    private int _next = -1;

    public ApiInstances(IConfiguration configuration)
    {
        _names = (configuration["Api:Instances"] ?? "api")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_names.Length == 0)
            _names = ["api"];
    }

    /// <summary>The name code uses; the handler swaps it for a real instance.</summary>
    public const string Logical = "api";

    public IReadOnlyList<string> Names => _names;

    public string Next() => _names[(uint)Interlocked.Increment(ref _next) % (uint)_names.Length];
}

/// <summary>
/// Points each outgoing Api call at the next instance. If that instance cannot be reached it tries the others, so one
/// dead instance does not break the host panel. Only connection failures are retried: a response, whatever its status,
/// means the request arrived.
/// </summary>
public sealed class ApiInstanceHandler(ApiInstances instances) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { } uri || uri.Host != ApiInstances.Logical)
            return await base.SendAsync(request, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            request.RequestUri = new UriBuilder(uri) { Host = instances.Next() }.Uri;
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < instances.Names.Count && !cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
