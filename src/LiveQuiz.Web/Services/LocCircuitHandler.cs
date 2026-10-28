using LiveQuiz.Web.Client.Localization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LiveQuiz.Web.Services;

/// <summary>Creates the circuit's <see cref="Loc"/> while the originating request (and its language cookie) is still available.</summary>
public sealed class LocCircuitHandler(Loc loc) : CircuitHandler
{
    public Loc Loc { get; } = loc;

    // Resolving this handler is what creates the circuit's Loc; nothing else to do.
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) => Task.CompletedTask;
}
