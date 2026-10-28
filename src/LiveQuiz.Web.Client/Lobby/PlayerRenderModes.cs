using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LiveQuiz.Web.Client.Lobby;

public static class PlayerRenderModes
{
    /// <summary>
    /// Player pages are WebAssembly only and not prerendered: the screen is entirely per-user and live, and
    /// prerendering would force the server to carry the player's browser-only services too.
    /// </summary>
    public static readonly IComponentRenderMode Wasm = new InteractiveWebAssemblyRenderMode(prerender: false);
}
