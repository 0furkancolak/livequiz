using LiveQuiz.Web.Client.Lobby;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same origin as the host page: the Web app proxies /api and /hubs to the Api.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<PlayerIdentityStore>();
builder.Services.AddScoped<IPlayerSessionClient, PlayerSessionClient>();

await builder.Build().RunAsync();
