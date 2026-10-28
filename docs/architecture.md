# Architecture

Status: Phase 0 skeleton. Updated as phases land.

```mermaid
flowchart LR
    Browser["Browser"] -->|InteractiveServer circuit| Web["LiveQuiz.Web (host panel)"]
    Browser -->|WASM player| Web
    Web -->|HTTP, service discovery| Api["LiveQuiz.Api"]
    Api --> PG[("PostgreSQL")]
    Api -. OTLP .-> Dash["Aspire dashboard"]
    Web -. OTLP .-> Dash
```

Planned additions: Orleans silo in the API (Phase 2), SignalR hub (Phase 2-3), second silo + backplane (Phase 5).
Decisions are recorded in [adr/](adr/).
