# ADR 0016: Two silos, membership in Postgres, shared Data Protection keys

Status: accepted

## Decision
- `Cluster:Provider` selects the clustering: `Localhost` (default; one silo; used by tests and a plain `dotnet run`) or
  `AdoNet`. The Orleans section name `Orleans:` is reserved by Orleans itself, so these settings live under `Cluster:`.
- With `AdoNet`, silos register in `orleansmembershiptable` in the same Postgres database, using the schema vendored from
  dotnet/orleans v10.4.0 (an EF migration, like the persistence schema in ADR 0010). Silo and gateway ports come from
  `Cluster:SiloPort` and `Cluster:GatewayPort`.
- Aspire starts `Api:Instances` (default 2) projects named `api-1`, `api-2`, ... Replicas of one resource cannot have
  proxy-less endpoints in Aspire, and every silo needs ports of its own, so each instance is a resource with fixed ports
  (HTTP 5197+, silo 11111+, gateway 30001+).
- Web receives the instance names (`Api:Instances`). YARP lists each as a destination and balances browser traffic with
  **round robin** (its default, power of two choices, always picked the dead instance because it has no requests in flight)
  and passive health checks. The Api instances use plain, non-proxied endpoints: Aspire's TCP proxy keeps accepting
  connections to a dead instance without answering, so reconnects hang instead of failing fast.
- The host's hub connection (a server-side client in the Blazor circuit) connects through Web's own origin, like a browser,
  so every reconnect gets a live instance. `ApiClient` takes the instances in turn and retries the next one on a connection
  failure.
- Clients reconnect every 5 to 7 s after the first few attempts, because the cluster needs about 20 s to notice a dead silo.
- Identity cookies and antiforgery keys: Api and Web already share one Data Protection key ring (`DataProtection:KeysPath`,
  a directory shared by all processes). With more instances the same directory is shared by all of them, so a cookie issued
  by one is valid on every other. This is enough on a single machine. Across machines the ring must move to a shared store
  (Postgres or blob storage) with the same application name; it is the documented next step and not needed here.

## Why
Grains must be reachable from every instance for the backplane and failover to mean anything. Membership in Postgres
needs no extra infrastructure, matching the grain persistence choice.

## Consequences
- A silo that dies is detected by the others (membership probes, about 20 s) and its grains reactivate elsewhere from
  Postgres; the open question restarts (ADR 0013). Clients on the dead instance reconnect through YARP to a live one: about
  18 s after a `kill -9` in the manual check (docs/load-test.md). Until the silo is declared dead, calls to grains it held fail.
- Rate limit counters are per Api instance (ADR 0014), so with two instances the effective limit is up to twice as large.
- Web's own HTTP calls try the next instance after a connection failure but do not remember which instance is down; YARP's
  passive health check covers browser traffic only. Active health checks are the upgrade.
- Stale silo processes from a previous run (an unclean stop) keep their ports and confuse a later run; stop the AppHost first.
