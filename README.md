# LiveQuiz

Real-time quiz engine (Orleans + SignalR + Blazor). Work in progress: see [docs/architecture.md](docs/architecture.md) and [docs/adr](docs/adr).

## Run locally

Requires .NET 10 SDK and Docker.

```bash
dotnet run --project src/LiveQuiz.AppHost
```

Dashboard: http://localhost:15888 (login link is printed in the console). Web: http://localhost:5255 (host panel: `/host`, register at `/host/register`).

```bash
dotnet test --solution LiveQuiz.sln
```
