# ADR 0012: Open-question answers stay in memory and are persisted when the question closes

Status: accepted (restart, retry and reconnect handled in ADR 0013)

## Decision
While a question is open, answers live only in the session grain's memory. When it closes, the grain updates scores and
persists the session state once. (Phase 5 removed the per-answer record in `PlayerGrain`: see below.) A finished game is written
to Postgres by the Api through `ISessionArchive` (`SessionRecord`, player results, per-question statistics).

## Why
Persisting every answer would mean up to 2000 database writes in a burst for each question. One write per question
keeps the hot path in memory.

## Consequences
- If the silo dies mid-question, that question's answers are lost. On reactivation the grain re-arms the close timer
  from the persisted start time, so the game continues and the question ends with no answers. Phase 4 (resilience)
  revisits this.
- The archive call is best effort and idempotent on the session id; a failure is logged, not retried yet.
- `SessionRecord` copies title, nicknames and question text and has no foreign key to quizzes, so editing or deleting a
  quiz never rewrites history.
- Host and player reconnection mid-game is not implemented yet (Phase 4): a refreshed host page cannot rejoin a running
  game, and a restored player lands in the lobby view until the next event arrives.

## Update (Phase 5)
The load test (docs/load-test.md) showed that recording each player's answer in `PlayerGrain` at close cost 20,000 Postgres
writes per 2000-player game, delayed the results by about 4 s, and that nothing read the data: scores live in the session state
and the archive. `RecordAnswer`, `GetScore` and the per-answer state were removed; `PlayerGrain` keeps only the nickname and the
reconnect token hash. One session state write per question remains.
