# ADR 0008: Quiz is saved as a whole aggregate

Status: accepted

## Decision
- `Quiz -> Question -> AnswerOption` is one aggregate edited in one form. `PUT /api/quizzes/{id}` replaces
  all questions and options in a single transaction instead of diffing them.
- Question and option order is explicit (`Order`, unique per parent); ids are client-independent `Guid.CreateVersion7()`.
- All quiz queries are filtered by owner; another host's quiz is a 404, never a 403, so existence is not leaked.
- Validation rules live in `LiveQuiz.Contracts` (`QuizValidator`) and run in both the editor (instant feedback)
  and the Api (the real gate).
- The host-facing `QuizDetail` contains correct answers. Player-facing DTOs (Phase 3) are separate types so the
  correct answer cannot leak by accident.

## Consequences
Simple, atomic updates; question ids change on every save, which is fine because live sessions (Phase 2+) will snapshot
the quiz when they start rather than reference rows.
