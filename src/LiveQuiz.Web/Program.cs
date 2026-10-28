using LiveQuiz.ServiceDefaults;
using LiveQuiz.Web.Client.Localization;
using LiveQuiz.Web.Components;
using LiveQuiz.Web.Services;
using Microsoft.AspNetCore.Identity;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedDataProtection();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();
builder.Services.AddScoped(sp => new Loc(LanguageResolver.Resolve(sp.GetRequiredService<IHttpContextAccessor>().HttpContext)));
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, LocCircuitHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// The Api issues the Identity cookie; Web validates it locally (same scheme name => same cookie name, same
// Data Protection keys) so HttpContext.User and the circuit's authentication state work without calling the Api.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options => options.LoginPath = "/host/login");

builder.Services.AddScoped<ApiSession>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, ApiSessionCircuitHandler>();
builder.Services.AddScoped<LiveQuiz.Web.Host.BrowserApi>();
builder.Services.AddScoped<LiveQuiz.Web.Host.IHostSessionClient, LiveQuiz.Web.Host.HostSessionClient>();

// "https+http://api" is resolved by Aspire service discovery (prefers https, falls back to http).
builder.Services.AddSingleton<ApiInstances>();
builder.Services.AddTransient<ApiInstanceHandler>();
builder.Services.AddHttpClient<ApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiInstanceHandler>();

// Web is the single browser-facing origin: /api/** is forwarded to the Api, so the Identity cookie
// is first-party and neither CORS nor cross-origin cookies are needed (see ADR 0007). /hubs/** takes the same path.
var apiInstances = new ApiInstances(builder.Configuration);
builder.Services.AddReverseProxy()
    .LoadFromMemory(
        [
            new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = "api",
                ClusterId = "api",
                Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/{**catch-all}" }
            },
            // SignalR (WebSocket upgrade included) for the player screen; same cluster, same origin.
            new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = "hubs",
                ClusterId = "api",
                Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/hubs/{**catch-all}" }
            }
        ],
        [new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "api",
            // A destination that refuses connections is skipped for a while, so a dead instance costs a few
            // failed attempts and not every other request.
            HealthCheck = new Yarp.ReverseProxy.Configuration.HealthCheckConfig
            {
                Passive = new Yarp.ReverseProxy.Configuration.PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = "TransportFailureRate",
                    ReactivationPeriod = TimeSpan.FromSeconds(30)
                }
            },
            // The default policy (power of two choices) prefers the destination with the fewest requests in flight,
            // which for a dead instance is always zero; round robin does not.
            LoadBalancingPolicy = "RoundRobin",
            Metadata = new Dictionary<string, string>
            {
                ["TransportFailureRateHealthPolicy.RateLimit"] = "0.5",
                ["TransportFailureRateHealthPolicy.MinimalTotalCountThreshold"] = "1"
            },
            // Browser traffic is spread over every Api instance. SignalR connections need no stickiness: the clients skip
            // negotiation and use WebSockets, and the Orleans backplane delivers messages whichever instance holds a socket.
            Destinations = apiInstances.Names.ToDictionary(
                name => name,
                name => new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = $"https+http://{name}" })
        }])
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapGet("/lang/{code}", (string code, string? returnUrl, HttpContext context) =>
{
    var language = Loc.Normalize(code);
    context.Response.Cookies.Append(
        LanguageResolver.CookieName,
        language,
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, Path = "/" });
    // Local paths only, so the endpoint cannot be used as an open redirect.
    var target = returnUrl is { Length: > 0 } && returnUrl[0] == '/' && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\") ? returnUrl : "/";
    return Results.LocalRedirect(target);
}).ExcludeFromDescription();
app.MapReverseProxy();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(LiveQuiz.Web.Client._Imports).Assembly);

app.Run();
