# Architecture

Status: Phase 1 (content + auth). Updated as phases land.

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

Planned additions: Orleans silo in the API (Phase 2), SignalR hub (Phase 2-3), second silo + backplane (Phase 5).
Decisions are recorded in [adr/](adr/).
