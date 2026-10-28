# ADR 0005: Server-authoritative timing and scoring

Status: accepted

## Decision
- The server decides `QuestionStartedAtUtc`, `DeadlineUtc`, answer acceptance (`receivedAt <= DeadlineUtc + 250ms`)
  and score. Client clocks are never trusted.
- Clients estimate a clock offset with pings and use it only to render the countdown.
- Time is read through `TimeProvider` everywhere so boundaries are unit-testable.
- Score for a correct answer: `round(1000 * (1 - (elapsed / timeLimit) / 2))`; wrong or late = 0; one answer per player per question.
