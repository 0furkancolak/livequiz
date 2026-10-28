using System.Net;
using Microsoft.Extensions.ServiceDiscovery;

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
/// means the request arrived. The service-discovery handler sits outside this one and only knows the logical name, so the
/// instance's address is resolved here.
/// </summary>
public sealed class ApiInstanceHandler(ApiInstances instances, ServiceEndpointResolver resolver) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { } uri || uri.Host != ApiInstances.Logical)
            return await base.SendAsync(request, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                request.RequestUri = await ResolveAsync(uri, instances.Next(), cancellationToken);
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < instances.Names.Count && !cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task<Uri> ResolveAsync(Uri uri, string instance, CancellationToken cancellationToken)
    {
        var source = await resolver.GetEndpointsAsync($"https+http://{instance}", cancellationToken);
        var address = source.Endpoints.Select(e => e.EndPoint).FirstOrDefault()
            ?? throw new HttpRequestException($"No address is configured for Api instance '{instance}'.");

        var builder = new UriBuilder(uri);
        switch (address)
        {
            case UriEndPoint { Uri: var resolved }:
                builder.Scheme = resolved.Scheme;
                builder.Host = resolved.Host;
                builder.Port = resolved.Port;
                break;
            case IPEndPoint ip:
                builder.Scheme = Uri.UriSchemeHttp;
                builder.Host = ip.Address.ToString();
                builder.Port = ip.Port;
                break;
            case DnsEndPoint dns:
                builder.Scheme = Uri.UriSchemeHttp;
                builder.Host = dns.Host;
                builder.Port = dns.Port;
                break;
            default:
                throw new HttpRequestException($"Unsupported address type for Api instance '{instance}'.");
        }

        return builder.Uri;
    }
}
