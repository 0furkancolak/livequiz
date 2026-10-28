# ADR 0006: xunit v3 on Microsoft.Testing.Platform; CI on GitHub Actions

Status: accepted

## Context
On .NET 10 SDK, `dotnet test` with Microsoft.Testing.Platform (MTP) projects requires opting in.

## Decision
- `global.json` sets `test.runner` to `Microsoft.Testing.Platform`; tests use xunit.v3.
- Run tests with `dotnet test --solution LiveQuiz.sln`.
- CI (`.github/workflows/ci.yml`) restores, builds in Release with warnings as errors, and runs the tests.
  Docker is available on GitHub runners for Testcontainers from Phase 1.
