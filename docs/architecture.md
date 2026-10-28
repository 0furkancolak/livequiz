# Architecture

Status: Phase 5 (two silos, SignalR backplane, load test; ADR 0015, 0016, docs/load-test.md). Updated as phases land.

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
