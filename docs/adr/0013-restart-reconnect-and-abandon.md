# ADR 0013: Restarts, reconnects and abandoned sessions

Status: accepted (supersedes the open points of ADR 0012)

## Decision
- **Joining is a resume.** `JoinAsHost` and `JoinAsPlayer` return the current phase snapshot (`HostSnapshot`,
  `PlayerGameState`: lobby, open question with remaining time and the player's own choice, results, or final).
  The Blazor screens have one `Apply...` method that every path (first join, page reload, SignalR reconnect) goes through.
- **Silo restart mid-question restarts that question** with its full time, and `QuestionStarted` is pushed again.
  The answers of an open question live only in memory (ADR 0012), so they are gone; players whose screen shows
  "answer locked in" get the question again and answer again. Closed questions are never replayed: scores, each
  player's last answer and the per-question statistics are persisted when a question closes.
- **Connection state is per activation.** The grain tracks the host's and each player's connection ids in memory. A fresh
  activation starts with everyone disconnected and clients rejoin, which sets it again. A player with two tabs stays
  connected until the last one closes. Connection changes are not written to storage.
- **Abandoned sessions cancel themselves.** When a live session has had no host connection for `HostAbsenceLimit`
  (30 minutes, configurable), the grain cancels itself and frees the PIN. A host who comes back in time cancels the timer.
  The grain also delays its own deactivation past that limit, so the timer survives the idle collector.
- **Archiving a finished game retries** with exponential backoff (`ArchiveAttempts` = 3, `ArchiveRetryDelay` = 2 s,
  doubled each time). The first attempt is inline; retries run detached so the game's last call never waits on them.
- **Clients reconnect on their own** (`WithAutomaticReconnect`: 0, 1, 2, 5, 10 s, then every 30 s, for 5 minutes),
  show a "reconnecting" banner and rejoin the groups. If rejoining is refused or the policy gives up, the screen says
  to reload.

## Why
A phone locking its screen or a laptop closing its lid is normal for a quiz; the game must survive it without the host
doing anything. Restarting the question is simpler and fairer than guessing what was answered when. Treating the snapshot
as the one source of truth for "where is the game" removes a whole class of "stuck on the wrong screen" bugs.

## Consequences
- A restart costs the players of the open question their answers and gives them the full time again. This is stated in
  the game's behavior, not hidden.
- Orleans does not write a field that equals the type's default, so state fields are 0-based with 0 meaning "none yet"
  (`CurrentIndex`, `LastQuestionNumber`). An initializer like `= -1` would silently come back after a restart. A test
  restarts a session mid-game to guard this.
- The host connection set is lost if a grain with no activity deactivates anyway; the host's next rejoin restores it, and
  the abandon timer is re-armed on activation.
- A real process kill is checked by hand (AppHost: stop and start the Api). Tests simulate it by deactivating the grain
  and reading the stored state back. Several silos and the SignalR backplane are Phase 5.
