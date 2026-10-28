# ADR 0015: Orleans-based SignalR backplane

Status: accepted

## Context
With more than one Api instance, a grain event (`QuestionStarted`, results, ...) is raised on the silo that holds the
session grain, but the players' WebSockets are spread over all instances. `IHubContext` only reaches connections on its
own instance, so a backplane is needed.

## Options
| Option | For | Against |
| --- | --- | --- |
| `ManagedCode.Orleans.SignalR.Server` 10.4.2 | Same version line as Orleans 10.4 and .NET 10, released recently; no extra infrastructure (it uses grains); groups and connections are partitioned grains | Smaller community than Redis; adds grains and per-message hops through the cluster |
| `SignalR.Orleans` 10.0.0 | Most downloads of the Orleans options | Sparse release history |
| `UFX.Orleans.SignalRBackplane` 10.1.0 | Orleans based | Few users |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.14 | Microsoft's own, proven | A new service to run and monitor, and a second cluster-wide system next to Orleans |

## Decision
`ManagedCode.Orleans.SignalR.Server` (`AddSignalR().AddOrleans()`, `ConfigureOrleansSignalR()`, in-memory storage for its
grains). The Redis backplane is the documented fallback if this package stops being maintained: only the registration in
`Program.cs` and `OrleansSetup.cs` changes, because the rest of the code talks to `IHubContext`.

- Its grain state (who is connected, who is in which group) is rebuilt by reconnecting clients, so it is kept in memory
  instead of Postgres.
- The backplane sends hub messages between silos with the Orleans serializer. The DTOs in `LiveQuiz.Contracts` have no
  Orleans attributes (the browser uses them too), so Orleans serializes types in that namespace as JSON
  (`Microsoft.Orleans.Serialization.SystemTextJson`). Without it every cross-silo delivery failed with
  `CodecNotFoundException`; the first load test run showed half the players receiving nothing.
- Clients connect with `SkipNegotiation` and WebSockets only. There are no sticky sessions behind YARP, so a negotiate
  request and the socket must not need to meet the same instance.

## Consequences
- Every group send now crosses the Orleans cluster. The load test (docs/load-test.md) shows what that costs.
- Hub messages are best effort (ADR 0009); a client that misses one recovers from the snapshot when it reconnects.
- Tests run the same backplane with a single silo.
