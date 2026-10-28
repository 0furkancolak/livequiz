using LiveQuiz.ServiceDefaults;
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
builder.Services.AddScoped<LiveQuiz.Web.Host.IHostLobbyClient, LiveQuiz.Web.Host.HostLobbyClient>();

// "https+http://api" is resolved by Aspire service discovery (prefers https, falls back to http).
builder.Services.AddHttpClient<ApiClient>(client => client.BaseAddress = new Uri("https+http://api"));

// Web is the single browser-facing origin: /api/** is forwarded to the Api, so the Identity cookie
// is first-party and neither CORS nor cross-origin cookies are needed (see ADR 0007). /hubs/** takes the same path.
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
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                ["api"] = new() { Address = "https+http://api" }
            }
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
app.MapReverseProxy();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(LiveQuiz.Web.Client._Imports).Assembly);

app.Run();
