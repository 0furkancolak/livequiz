# Architecture

Status: Phase 6 (E2E tests, Docker Compose; ADR 0017). The README has the current diagram. Updated as phases land.

```mermaid
flowchart LR
    Browser["Browser"] -->|InteractiveServer circuit| Web["LiveQuiz.Web (host panel)"]
    Browser -->|WASM player| Web
    Browser -->|"/api/** (YARP proxy)"| Web
    Web -->|"HTTP + Identity cookie"| Api["LiveQuiz.Api (Identity, quizzes)"]
    Api --> PG[("PostgreSQL")]
    Api -. OTLP .-> Dash["Aspire dashboard"]
    Web -. OTLP .-> Dash
```

Phase 5 added a second Api instance (its own Orleans silo, membership in Postgres) and an Orleans-based SignalR backplane.
Decisions are recorded in [adr/](adr/).
