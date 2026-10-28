# ADR 0007: Identity in the Api, one browser-facing origin, shared cookie

Status: accepted

## Context
Hosts sign in with ASP.NET Core Identity (cookie). Identity lives in `LiveQuiz.Api`; the host panel is a Blazor
InteractiveServer app in `LiveQuiz.Web`. The cookie must be valid in both, and the player WASM client (Phase 2+)
must reach the Api and the SignalR hub without CORS.

## Decision
- **Web is the only origin the browser talks to.** YARP forwards `/api/**` to the Api (service discovery),
  so the Identity cookie is first-party and CORS is not needed. `/hubs/**` will use the same mechanism.
- **Sign-in/out are browser `fetch` calls** to `/api/auth/*` (via `host.js`), because only the browser can store
  the `Set-Cookie`. After sign-in the page does a full reload so the new circuit starts with the cookie.
- **Web validates the cookie locally.** Same authentication scheme name (`Identity.Application`) and a shared Data
  Protection application name and key ring (`DataProtection:KeysPath`) make the cookie readable by both apps, so
  `[Authorize]` and the circuit's authentication state work without an Api call per request.
- **Server-side Api calls from a circuit** (`ApiClient`) forward the host's Identity cookie. `ApiSession` captures
  it when the circuit opens (while the originating request exists) and keeps it in circuit scope.
  The Api remains the authority: it re-validates the cookie and enforces ownership on every call.
- The Api answers 401/403 instead of redirecting (it is an API); Web redirects anonymous page requests to `/host/login`.

## Consequences
- Locally the AppHost points both apps at `.data/keys` (git-ignored; keys are secrets).
  Production needs a shared key store (Redis or database) before running more than one instance; tracked for Phase 5.
- The cookie is captured once per circuit: signing out in another tab does not end an open circuit until it reconnects.
- Rejected: forwarding bearer tokens (no browser persistence across reloads) and calling the Api per Web request
  to resolve the user (extra round trip for every static asset).
