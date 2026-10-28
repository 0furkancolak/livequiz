# ADR 0010: Grain state in Postgres via ADO.NET persistence; grain types live in `LiveQuiz.Grains`

Status: accepted

## Decision
- `PinRegistryGrain` (key: PIN), `SessionGrain` (key: session id) and `PlayerGrain` (key: player id + session id)
  keep their state in `IPersistentState<T>`, stored by `Microsoft.Orleans.Persistence.AdoNet` in the application
  database (`orleansstorage`). The schema is the unmodified script from dotnet/orleans v10.4.0, vendored under
  `Persistence/Orleans/` and applied by the `OrleansPersistence` EF migration, so one `dotnet ef database update`
  prepares everything. The silo needs these tables at startup, so the Api no longer boots without its database.
- Clustering is `UseLocalhostClustering()` for the single silo; Phase 5 switches to ADO.NET clustering for two silos.
- PINs are 6 random digits from a cryptographic RNG; collisions are resolved by `TryReserve` retries. `Release`
  clears the registry state when a session ends, so a PIN is reusable.
- Reconnect tokens are 32 random bytes; only their SHA-256 is stored and compared in constant time. The raw token is
  returned once, at join.
- Grain interfaces and their serializable result types (`JoinResult`, `SessionInfo`, `PlayerSnapshot`) are defined in
  `LiveQuiz.Grains`, not in `LiveQuiz.Contracts`. This keeps Contracts free of any Orleans package (the player
  WASM bundle references Contracts); the Api maps grain results to Contracts DTOs at the edge.

## Consequences
- `SessionGrain` writes its whole state on every join (O(players) per write). Fine for a lobby; Phase 3/5 will
  measure it under the 2000-player load test and batch writes if it shows up.
- Storage uses the default Orleans binary serializer (`bytea`); rows are not human-readable.
- Tests use `AddMemoryGrainStorageAsDefault` for TestCluster, and the real Postgres path is covered by the Api tests.
