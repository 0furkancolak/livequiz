using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LiveQuiz.Web.Services;

/// <summary>Grabs the host's cookie as soon as the circuit opens, while the originating request is still available.</summary>
public sealed class ApiSessionCircuitHandler(ApiSession session) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        session.Capture();
        return Task.CompletedTask;
    }
}
