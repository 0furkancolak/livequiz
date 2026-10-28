# Load test

One game with **2000 players and 10 questions**, driven through the Web origin (YARP) over the real SignalR WebSocket
protocol, against a cluster of **two Api instances (two Orleans silos)**. The script is `tests/load/quiz.js` (k6).

## Run it

```bash
dotnet run --project src/LiveQuiz.AppHost --launch-profile http     # starts Postgres, 2 Api instances, Web
k6 run -e PLAYERS=2000 -e QUESTIONS=10 -e JOIN_WINDOW=60 -e PERIOD=10 tests/load/quiz.js
```

`-e API_STATS=http://localhost:5197` limits the grain-write report to one instance (use it with `Api__Instances=1`).
Other knobs: `BASE_URL`, `PERIOD` (seconds between questions), `JOIN_WINDOW` (players arrive spread over this time).

## What the script does

- `setup()` registers a host, creates a 10-question quiz and a session, and fixes the moment the game starts.
- One **host** VU connects to the hub with the Identity cookie, calls `StartGame`, then `NextQuestion` on a fixed schedule.
- 2000 **player** VUs join over 60 s (`POST /api/sessions/join`), open a WebSocket, call `JoinAsPlayer`, and for each question
  answer after a random 0.5 to 3.5 s (`SubmitAnswer`). Each expects 10 `QuestionStarted`, 10 `PlayerResult` and a `SessionFinished`.
- Custom metrics (thresholds in the script fail the run when crossed):

| Metric | Meaning |
| --- | --- |
| `quiz_join_duration` | `POST /api/sessions/join` |
| `quiz_hub_join_duration` | WebSocket open until `JoinAsPlayer` completes |
| `quiz_question_latency` | host's scheduled send until the player receives `QuestionStarted` |
| `quiz_answer_roundtrip` | `SubmitAnswer` sent until its completion arrives |
| `quiz_result_latency` | answer sent until the player's own result arrives (includes waiting for the slowest player: a question closes when everyone has answered) |
| `quiz_questions_missed`, `quiz_results_missed`, `quiz_finals_missed` | events a player never received (must be 0) |
| `quiz_join_failures`, `quiz_hub_failures`, `quiz_answers_rejected` | errors (must be 0) |

At the end `teardown()` prints each Api instance's grain write totals from `/api/diagnostics/grain-writes` (Development only).

## Results

Machine: Apple M4, 10 cores, 16 GB, macOS 27, k6 2.1.0, OrbStack (Docker with 6 CPUs for Postgres 17). **k6, both Api
instances, Web and Postgres all share this one machine**, so these numbers show relative behavior and where time goes, not
the capacity of a deployment.

| | 2 silos, first run | 2 silos, after fixes | 1 silo, after fixes |
| --- | --- | --- | --- |
| Events lost (questions, results, finals) | 0 | 0 | 0 |
| `quiz_join_duration` avg / p95 | 4.5 s / 8.2 s | **98 ms / 408 ms** | 75 ms / 236 ms |
| `quiz_hub_join_duration` avg / p95 | 11.4 s / 20.7 s | **151 ms / 729 ms** | 105 ms / 421 ms |
| `quiz_question_latency` avg / p95 | 128 ms / 208 ms | 292 ms / 378 ms | 148 ms / 686 ms |
| `quiz_answer_roundtrip` p95 | 11 ms | 29 ms | 9 ms |
| `quiz_result_latency` avg / p95 | 6.1 s / 8.4 s | **1.8 s / 3.2 s** | 1.7 s / 3.0 s |
| PlayerGrain writes (avg) | 22,160 (1.3 s) | 2,000 (7 ms) | 2,175 (8 ms) |
| SessionGrain writes (avg) | 2,070 (15 ms) | 2,022 (11 ms) | 2,203 (12 ms) |

`quiz_result_latency` is dominated by the slowest of 2000 players answering (up to 3.5 s in the script), so about 1.8 s is
the floor here; the first run added about 4 s on top.

### What the first run showed, and what changed

1. **Session state write cost** (the measurement the phase asked for): one `SessionGrain` write per join (2000) and one per
   question close or phase change (about 20) at 11 to 15 ms each. That is fine. The state grows with the players
   (the whole dictionary is written each time), so it is quadratic in bytes over the lobby, but at 2000 players it stayed
   under 200 ms at worst.
2. **`PlayerGrain.RecordAnswer` was the real cost.** Closing a question awaited 2000 concurrent Postgres writes (about 1.3 s each
   under contention, up to 6 s) before any result was pushed, and nothing read that data: the authoritative score lives in the
   session state and the archive. It was removed (ADR 0012 updated); `PlayerGrain` now holds only the reconnect token.
3. **`JoinAsPlayer` called `GetInfo`**, which ships the whole player list from the one session grain on every hub join, so
   2000 joins competed with each other and with the HTTP joins for that grain (11 s hub join, 4.5 s HTTP join). The hub now asks
   for the player's own view only.
4. Cross-silo delivery initially lost half of all events: the backplane could not serialize the Contracts DTOs
   (`CodecNotFoundException`). Fixed with the Orleans JSON serializer for that namespace (ADR 0015).

### Reading the table honestly

- Two silos are not faster than one on a single machine (the second silo adds a backplane hop: about 150 ms on
  `QuestionStarted` to 2000 recipients). What they buy is availability, shown next. Throughput scaling needs separate machines.
- One game is one session grain, so it always runs on one silo; the second silo mostly carries connections and fan-out.
  Many concurrent games would spread over both.
- 2000 players is under the session limit (2500) but is one small game. The run does not model players on slow networks.

## Killing a silo mid-game

Checked by hand with the AppHost (two instances), one host and one player in the browser:

1. Start a game; find the instance holding the session grain (`/api/diagnostics/grain-writes` shows its writes).
2. `kill -9` that Api process.
3. Result: the cluster marked the silo dead in about 20 s, the player's WebSocket reconnected through YARP to the other
   instance about 18 s after the kill and rejoined (the question restarted with its full time, ADR 0013), the next answer
   was scored, and the host saw the results.

This took several rounds to get right and is why ADR 0016 has these rules: the host's hub connection goes through YARP
instead of straight to one instance; YARP uses round robin with passive health checks (its default balancer always preferred
the dead instance, which had no requests in flight); the Api instances are not behind Aspire's TCP proxy (it keeps accepting
connections to a dead instance and never answers, so reconnect attempts hang); and the reconnect delay is capped at 5 to 7 s.

## Not covered

- Players on the instance that is killed are not automatically spread again when it comes back (new connections balance).
- The k6 script does not reconnect dropped players; reconnection is covered by the Api tests and the manual check above.
- A load test across several machines, and TLS termination.
