# ADR 0017: E2E tests start the real processes; Compose is a second way to run

Status: accepted

## Decision
- `tests/LiveQuiz.E2E` uses Playwright for .NET (Chromium). Its fixture starts a PostgreSQL container (Testcontainers) and the
  **Api and Web processes from the build output** (`dotnet LiveQuiz.Api.dll`), on free ports, with a silo of its own
  (`Cluster:Provider=AdoNet` on free ports, so a development AppHost on the same machine does not collide).
  It does not start the Aspire AppHost.
- Every person in a scenario gets their own browser context (separate cookies and storage, like separate devices).
  Scenarios: a complete game from registering a host to the history page; a player reloading mid-question; an unknown PIN;
  the render-mode split (the host panel never downloads the .NET runtime, the player screen does).
- `LIVEQUIZ_BASE_URL` points the same tests at a stack that is already running (Compose, the AppHost).
- `docker compose up` runs two Api instances, Web and Postgres from `docker/Dockerfile.api` and `docker/Dockerfile.web`
  (multi-stage, non-root `app` user). It sits beside the AppHost; it is not an Aspire publish manifest.
- Silos advertise `Cluster:AdvertisedHost` (resolved to IPv4) and listen on all interfaces when it is set; without it they
  use loopback as before.
- Migrations take a PostgreSQL advisory lock, so instances that start together do not race.

## Why
The AppHost needs Aspire's DCP and Docker, and it was the least stable part of the earlier phases, so tests that depend on it would be
flaky for reasons that have nothing to do with the product. Starting the two processes directly tests the same code paths
(real Postgres, real YARP proxy, real hub, real WebAssembly) with fewer moving parts.
One Api instance is enough for browser flows; the two-silo behavior is covered by the load test and the resilience tests.

## Consequences
- The tests need Docker and a Chromium download (about 300 MB on first run, `PLAYWRIGHT_BROWSERS_PATH` moves it).
- Running the Compose stack under the E2E tests found two real defects that no earlier test could: two instances racing to migrate
  the same database, and Web calling the Api instances without resolving their addresses outside the AppHost.
  Both are fixed; the second has a unit test (`ApiInstanceHandlerTests`).
