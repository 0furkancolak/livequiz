# ADR 0002: .NET Aspire for local orchestration

Status: accepted

## Context
The system needs Postgres (and later Redis, a second silo) plus API and Web running together with
traces and metrics visible in one place.

## Decision
- `LiveQuiz.AppHost` (Aspire 13.x) starts Postgres, Api and Web; one command: `dotnet run --project src/LiveQuiz.AppHost`.
- `LiveQuiz.ServiceDefaults` wires OpenTelemetry (traces, metrics, logs via OTLP), health checks,
  service discovery and the standard resilience handler for every service.
- Postgres is pinned to `17.11-alpine` and stores data in a bind mount at `.data/postgres` (git-ignored),
  so state survives restarts and does not grow the Docker VM disk.
- `/health` and `/alive` are mapped in Development only.

## Consequences
Docker is required locally. Dashboard shows distributed traces across Web -> Api -> Postgres from day one.
