# ADR 0003: Orleans silo co-hosted in the API; no microservices

Status: accepted

## Context
Live session state (lobby, current question, deadline, scores) is highly concurrent per session but
independent across sessions.

## Decision
- Live state lives in Orleans grains (`IPinRegistryGrain`, `ISessionGrain`, `IPlayerGrain`). A grain processes one
  message at a time, so session state needs no locks.
- The silo is co-hosted inside `LiveQuiz.Api` (one API project + one Web project). Scaling out means running more
  API instances, which join the same Orleans cluster (Phase 5).
- Durable content (users, quizzes, history) stays in EF Core/Postgres; grain state uses Orleans persistence (Phase 2+).

## Consequences
Simple deployment, no network hop between hub and grains in the common case; the API process owns both concerns.
