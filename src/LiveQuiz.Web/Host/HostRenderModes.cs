using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LiveQuiz.Web.Host;

public static class HostRenderModes
{
    /// <summary>
    /// Host panel pages are interactive server only. Prerendering is off: the host is a logged-in tool, not
    /// indexed content, and skipping it avoids running every API call twice (prerender + circuit).
    /// </summary>
    public static readonly IComponentRenderMode Server = new InteractiveServerRenderMode(prerender: false);
}
