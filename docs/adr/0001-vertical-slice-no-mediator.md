# ADR 0001: Vertical slices, no MediatR / AutoMapper / generic repository

Status: accepted

## Context
The API is small and a single deployable. Layered "Clean Architecture" templates add projects and indirection
that do not pay for themselves here.

## Decision
- Code is organised by feature (`Features/<Feature>/...`): endpoint, request/response types and handler logic live together.
- Endpoints are plain Minimal API delegates; shared logic is plain classes injected via DI.
- No MediatR, AutoMapper or generic repository. `DbContext` already is a unit of work and repository.
- Mapping is hand-written (explicit, compile-time checked, trivially debuggable).

## Consequences
Less ceremony and fewer moving parts; features can be read top to bottom in one folder.
Cross-cutting behaviour (validation, logging) uses endpoint filters and middleware rather than pipeline behaviours.
