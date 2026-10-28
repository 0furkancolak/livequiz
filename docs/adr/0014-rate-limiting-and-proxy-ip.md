# ADR 0014: Rate limiting and the client address behind the proxy

Status: accepted

## Decision
All limits are configurable under `RateLimits` and answer with `429` and `Retry-After`.

| Where | Default | Counts |
| --- | --- | --- |
| `POST /api/sessions/join` | 10 per client per minute | only **failed** attempts (unknown PIN, game not joinable) |
| `/api/auth/*` | 10 per client per minute | every request (ASP.NET Core fixed-window rate limiter) |
| Hub `SubmitAnswer` | 5 per second per connection | every call; over-limit calls return `AnswerStatus.RateLimited` and never reach the grain |

- The join limiter asks "is this client blocked?" first, without consuming, and records a failure only when the lookup fails.
  A classroom of 200 students behind one router can all join with the right PIN; someone guessing PINs is stopped after 10 tries.
  Nickname errors are not counted, because they say nothing about guessing a PIN.
- The client address is the last `X-Forwarded-For` entry that YARP appended (`ForwardLimit` = 1), and it is believed only
  when the direct peer is in `Network:TrustedProxies` (defaults: loopback and private ranges). From any other peer the header is
  ignored, so rotating it buys an attacker nothing.

## Why
There are only 10^6 PINs and a few hundred open at a time, so guessing needs to be slow. Counting only failures keeps the
shared-IP case (schools, offices, conferences) working. Browsers talk only to the Web app (ADR 0007), so without
forwarded headers every client would look like the Web host and share one bucket.

## Consequences
- Counters are in memory per Api process. With several Api instances (Phase 5) each has its own, so the effective limit
  is multiplied by the instance count; a shared store (Redis) is the upgrade if that matters.
- An attacker who controls a trusted proxy range can lie about the address. The default ranges assume the Api is not
  reachable directly from outside; production sets `Network:TrustedProxies` to the real proxy.
- IPv6 clients are limited by full address, not by /64.
