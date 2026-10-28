# ADR 0004: Host panel on InteractiveServer, player screen on InteractiveWebAssembly

Status: accepted

## Context
Server interactivity keeps one SignalR circuit and server-side state per connected user (memory cost per circuit).
A session can have thousands of players but only one host.

## Decision
- Host panel (`LiveQuiz.Web`): `@rendermode InteractiveServer`. Few users, authenticated, richer admin UI,
  direct server access, no WASM download.
- Player screen (`LiveQuiz.Web.Client`): `@rendermode InteractiveWebAssembly`. Players run in the browser and talk to
  the quiz hub directly, so the server holds no per-player circuit.
- Render modes are set per page, not globally (`--all-interactive false`).

## Consequences
Player capacity is bounded by the hub/Orleans, not by circuit memory. Player code must live in the Client
project and only reference `LiveQuiz.Contracts`.
