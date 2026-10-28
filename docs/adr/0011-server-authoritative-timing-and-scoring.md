# ADR 0011: Timing, scoring and answer rules live in the session grain

Status: accepted (extends ADR 0005)

## Decision
- `SessionGrain` stamps every answer with its own clock (`TimeProvider`) the moment the hub call arrives. Clients send
  only `(questionIndex, optionIndex)`; there is no client time field anywhere in the contract.
- A question runs `TimeLimitSeconds`. An answer is accepted while `receivedAt <= startedAt + limit + AnswerGrace`
  (250 ms, `LobbyOptions.AnswerGrace`). The question closes at the end of that window, or earlier when every player
  has answered. Close is driven by a grain timer, so it fires even if nobody does anything.
- Points: correct = `round(1000 * (1 - (elapsed / limit) / 2))` (500–1000, elapsed clamped to the limit); wrong or
  missing = 0 (`Contracts.Game.Scoring`, a pure function with its own tests).
- One answer per player per question: the first one wins and a repeat returns `Duplicate`. Retried closes are harmless
  (`PlayerGrain.RecordAnswer` is idempotent per question).
- Clients get `RemainingMs` computed by the server at send time, not an absolute deadline, so client clock skew
  cannot matter. The on-screen countdown is cosmetic.
- The player-facing `QuestionForPlayer` has no correct-answer field. The answer is only sent in `QuestionResults`
  after the question closed. Tests assert both the DTO shape and that nothing is pushed before the close.
- Ranking is competition ranking (1, 1, 3). Ties are listed alphabetically.

## Consequences
Tests drive time with `FakeTimeProvider` (grain timers honour it), so boundary cases (inside/outside the grace window)
are exact. Network latency up to the grace period is forgiven; anything beyond is simply late.
