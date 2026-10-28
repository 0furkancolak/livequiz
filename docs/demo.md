# Demo scenario

A 60 to 90 second screen recording (GIF or short video) that shows what the project is. Three windows side by side:
the **host** (laptop), **Alice** and **Bob** (phone-sized).

## Script

| # | Who | What happens | What it shows |
| --- | --- | --- | --- |
| 1 | Host | Registers, creates a two-question quiz ("World quiz"), clicks start | Host panel (InteractiveServer), quiz editor |
| 2 | Host | The lobby shows a six-digit PIN | PIN registry grain |
| 3 | Alice, Bob | Open `/play`, type the PIN and a nickname | Player screen running as WebAssembly |
| 4 | Host | The lobby counter goes to "2 players" as they join, then Start | Live push through the SignalR backplane |
| 5 | Everyone | Question 1: the countdown runs on all three screens | Server-authoritative time |
| 6 | Alice, Bob | Alice picks the right answer, Bob a wrong one | The host's "1 of 2 answered" counter moves |
| 7 | Alice, Bob | "Correct!" with points and rank / "Wrong" with the right answer | Server-side scoring, per-player push |
| 8 | Host | Per-option counts and the leaderboard | |
| 9 | Alice | Reloads the page in the middle of question 2 and lands on the same question | Reconnect and state restore (ADR 0013) |
| 10 | Host | Next question, then final results: podium on every screen | |
| 11 | Host | History page, open the game | Archived result in Postgres |

Optional closing shot (Aspire dashboard, http://localhost:15888): the traces of one answer going Web, Api, grain, and the
`livequiz` metrics. Run the stack with the AppHost for this one.

## Recording it

Steps 1 to 8, 10 and 11 are the end-to-end test `A_full_game_is_played_from_quiz_creation_to_history`, so the recording can be generated
instead of performed by hand. Playwright writes one `.webm` per browser context (host, Alice, Bob):

```bash
LIVEQUIZ_E2E_VIDEO="$PWD/demo" LIVEQUIZ_E2E_SLOWMO=400 \
  dotnet test --project tests/LiveQuiz.E2E --filter-method '*full_game*'
```

`LIVEQUIZ_E2E_SLOWMO` is the delay in milliseconds before each action. To put the three videos side by side and turn them into a GIF
(needs ffmpeg):

```bash
ffmpeg -i demo/host.webm -i demo/alice.webm -i demo/bob.webm \
  -filter_complex "[0:v]scale=640:-1[h];[1:v]scale=320:-1[a];[2:v]scale=320:-1[b];[a][b]vstack[p];[h][p]hstack" \
  -an demo/live-quiz.webm
ffmpeg -i demo/live-quiz.webm -vf "fps=10,scale=900:-1:flags=lanczos" demo/live-quiz.gif
```

(Rename the three files first: Playwright names them by a random id, in the order the contexts were created: host, Alice, Bob.)
The test does not include the reload (step 9); do that one by hand in a recording against `docker compose up` if you want it in the GIF.
