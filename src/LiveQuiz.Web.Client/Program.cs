using LiveQuiz.Web.Client.Localization;
using LiveQuiz.Web.Client.Lobby;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same origin as the host page: the Web app proxies /api and /hubs to the Api.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<PlayerIdentityStore>();
builder.Services.AddScoped<IPlayerSessionClient, PlayerSessionClient>();

// The server renders <html lang> for the visitor's language; the player screen follows it.
builder.Services.AddSingleton(sp => new Loc(
    ((IJSInProcessRuntime)sp.GetRequiredService<IJSRuntime>()).Invoke<string?>("eval", "document.documentElement.lang") ?? "en"));

await builder.Build().RunAsync();
