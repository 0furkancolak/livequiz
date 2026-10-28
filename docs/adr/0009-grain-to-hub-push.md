# ADR 0009: Grains push to clients through an `ISessionNotifier`, and the hub authenticates inside invocations

Status: accepted

## Context
Grains must tell connected hosts and players about lobby changes. The spec suggests Orleans streams or observers,
with the Api forwarding to SignalR.

## Decision
- `LiveQuiz.Grains` defines `ISessionNotifier` (join, connection change, count, cancel). The Api implements it with
  `IHubContext<QuizHub, IQuizClient>` and registers it in the same DI container the silo uses.
- Delivery is best effort: a failing push is logged and never fails or rolls back the grain call.
- Hub groups are `session:{id}:host` and `session:{id}:players` (`QuizHubNames`). Hosts get every join; players
  only get a head count, coalesced to at most one push per `LobbyOptions.CountInterval` (1 s), so a join storm of
  n players costs O(n) messages, not O(n²).
- Credentials are checked inside hub invocations (`JoinAsHost` with the Identity cookie and ownership,
  `JoinAsPlayer` with the reconnect token), never in the URL, so tokens do not end up in access logs.
- The host panel connects from the Blazor circuit with a server-side `HubConnection` that forwards the host's cookie
  from `ApiSession`; the Api address comes from service discovery. Players connect from the browser to `/hubs/quiz`
  on the Web origin, where YARP proxies the WebSocket to the Api.

## Why not streams/observers
They add subscription lifecycle (observer refresh, stream providers, persistent subscriptions) for no benefit while
every silo hosts its own hub. A plain interface needs no subscription state, survives silo restarts, and tests swap
in a recording fake. With two silos (Phase 5) each silo pushes to its own SignalR clients; the backplane (ADR in
Phase 5) carries the message to clients connected to the other silo.

## Consequences
The notifier is a seam: if grains and hubs ever live in different processes, only its implementation changes.
Reconnecting clients must rejoin their group (Phase 4).
