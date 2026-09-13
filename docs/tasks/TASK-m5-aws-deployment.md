# Task Record: M5 — AWS Deployment (deploy-readiness)

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment`
- **Milestone**: M5 — AWS Deployment (Level 3, `pk:ship` + human approval)
- **State**: `in_progress` (implementation of deploy-readiness changes)
- **Owner / Actor**: Assistant (agent)
- **Branch**: `feat/m5-deploy-readiness` @ (this commit)
- **Plan / Spec**: `docs/specs/2026-09-13-spec-m5-aws-deployment.md`
- **Decision record**: `docs/aws-deployment.md`

## Objective
Make the application deployable to AWS (ECS/Fargate + ALB + RDS Postgres + CloudFront) while preserving the
existing auth cookie posture (`__Host-JT*` same-site, CSRF gate), with fail-fast boot and an unauthenticated
health endpoint for the ALB.

## Decisions (ratified, from `docs/aws-deployment.md`)
- **D-M5-1**: ECS/Fargate + ALB (TLS) + RDS Postgres
- **D-M5-2**: Single origin via CloudFront (S3 SPA + ALB API); API code unchanged for SPA serving
- **D-M5-3**: Single instance (preserves in-process SSE bus)
- **D-M5-4**: Keep `-074` RDS-backed `data_protection_keys`
- **Migration strategy** (owner-selected): **migrate on boot** — already implemented at `Program.cs:139-152`
  (`db.Database.Migrate()` inside the startup scope, before `app.Run()`). No change required.

## Work breakdown
1. **`GET /api/health`** (new, unauthenticated) — ALB health probe; DB reachability check. Placed so it is not
   caught by `UseSessionGate`/`UseAntiforgeryGate` (those protect `/api/applications*` and `/api/auth/*` minus
   login; `/api/health` is outside that scope, verified in `SessionGate.cs`).
2. **`BootGuard` fail-fast for DB** — extend `EnsureSafeToStart` to assert `ConnectionStrings:Default` is present
   in Production (and non-empty). Crash at startup with a named key, not after a user request.
3. **Forwarded headers** — `app.UseForwardedHeaders()` trusting the ALB/CloudFront via config-driven
   `ForwardedHeaders:KnownNetworks` (no hardcoded CIDR). So `Request.Scheme` reflects HTTPS behind the ALB.

## Changed files (planned)
- `api/src/JobTracker.Api/Program.cs` — health endpoint + forwarded headers
- `api/src/JobTracker.Api/Auth/BootGuard.cs` — DB-connection fail-fast assertion
- `docs/aws-deployment.md` — note `ForwardedHeaders:KnownNetworks` must list the VPC/ALB CIDR

## Acceptance (from spec AC1–AC6)
- AC-1: `GET /api/health` → 200 with DB ping on a valid deploy
- AC-2: single-origin browser auth works, no CORS preflight
- AC-3: missing `ConnectionStrings__Default` → boot crash with named error
- AC-4: migrations applied; `data_protection_keys` present
- AC-5: redeploy previous image recovers, no DB change
- AC-6: no `SameSite=None` cookie; `-073`/`D-3` remain unimplemented

## Verification
- API: `dotnet build` + `dotnet test` (Docker `dotnet/sdk:10.0`; Testcontainers for PG)
- Frontend: `npm run verify` (unchanged, must stay green)
- (M5 infra deploy itself is `pk:ship` + human approval, out of scope for this code PR)

## Blockers / Open
- None blocking. Domain + ACM certificate for the single origin is an owner action (see `aws-deployment.md` §8).

## Next action
Open a PR with the three changes; on merge, the remaining M5 work is infra (CloudFront/S3/ECS task def) and
the `pk:ship` deploy decision.
