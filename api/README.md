# `api/` — the M3 backend

ASP.NET Core 10 minimal API over PostgreSQL 18.6, satisfying the same six-method
`ApplicationRepository` seam the browser has been using since M2 — see
[`docs/specs/2026-09-11-spec-m3-backend-api.md`](../docs/specs/2026-09-11-spec-m3-backend-api.md).

## One-time setup (host install, `DECISION-m3-backend-api-002`)

The SDK is installed **on the host**, pinned, and the repository carries the pin:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 10.0.401 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet" && export PATH="$DOTNET_ROOT:$PATH"
dotnet --version   # must print 10.0.401
```

**`global.json` in the repository root says `"rollForward": "disable"`.** If `dotnet` refuses to run with a
message about the requested SDK version, that is the pin working, not a broken install: it means the machine
has a *different* SDK than the one this milestone was verified against, and silently rolling forward to it is
how "works on my machine" gets written into history. Either install the pinned version or change the pin in a
commit that says why.

## Day-to-day

```bash
docker compose -f api/docker-compose.yml up -d      # postgres:18.6 on 127.0.0.1:5432
export ConnectionStrings__Default='Host=127.0.0.1;Port=5432;Database=jobtracker;Username=jobtracker;Password=jobtracker-dev-only'

dotnet build api/JobTracker.slnx
dotnet test  api/tests/JobTracker.Api.Tests          # integration tests start their OWN container; see below
dotnet ef migrations add <Name> --project api/src/JobTracker.Api --startup-project api/src/JobTracker.Api
dotnet ef   database update  --project api/src/JobTracker.Api --startup-project api/src/JobTracker.Api
dotnet run   --project api/src/JobTracker.Api        # http://localhost:5000
```

Two things worth understanding rather than copying:

- **Tests do not use `docker-compose.yml`.** They start PostgreSQL through **Testcontainers** on a dynamic
  port, so a test run can't collide with your dev database or with a second run. The compose file is for
  humans running the app; the fixture is for tests asserting against a real server.
- **Migrations need `ConnectionStrings__Default` and refuse to guess.** `JobTrackerDbFactory` throws instead
  of falling back to a hard-coded connection string, because the failure mode of a silent default is applying
  a migration to a database nobody meant to touch.

## If a shell cannot write to `$HOME`

In restricted environments (CI, sandboxed agents) the first `dotnet` invocation dies with
`UnauthorizedAccessException: ~/.dotnet/<version>.toolpath.sentinel`. Redirect the writable state:

```bash
export DOTNET_CLI_HOME=/tmp/dotnet-home NUGET_PACKAGES=/tmp/nuget-packages
```

Neither belongs in this repository — they describe a machine, not the project.

## Current state (Slice 0)

Toolchain only: the projects build, the migration pipeline works, and the harness proves a `dotnet` process
can reach the pinned image. **There are no endpoints and no `DbSet` yet** — the table and the first route are
created by `BEHAVIOR-m3-backend-api-026`'s failing test, not by a commit that nothing drives.
