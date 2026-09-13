# Technical Design Document (RFC): M5 — AWS Deployment

> **Status map**: `Author`, `Status`, `Created`, `Target Release` are **Required**. Planning inputs are **Required** (Minimal depth). The deployment/none-schema scope means the Full-Planning and most architecture sections are **Not applicable** with a reason. Material tech decisions record decision + rationale.

- **Author**: Assistant (`pk:plan`)
- **Status**: Draft (awaiting owner review/merge of the plan PR)
- **Created**: 2026-09-13
- **Target Release**: M5 — AWS Deployment (Level 3, `pk:ship` + human approval)

<a id="PLAN-m5-aws-deployment"></a>

---

## Planning Record (PromptKit Adaptation)

### Planning Record Metadata

- **Planning Record ID [Required]**: `PLAN-m5-aws-deployment`
- **Planning Depth [Required]**: `Minimal`
- **Owner [Required]**: Assistant (agent) — decisions recommended per AC-16; owner ratifies by merging this plan
- **Record Status [Required]**: `draft`
- **Local Task Record Link [Required for Controlled Work]**: `docs/tasks/TASK-m5-aws-deployment.md` (to be created at implement time)
- **Workflow Links [Optional]**: `pk:plan` → `pk:tasks` → implement → `pk:test` → `pk:review` → `pk:ship`

### Planning Inputs

- **Requested Outcome [Required]**: Deploy the Job Tracker (React SPA + ASP.NET Core API + Postgres) to AWS such that the existing auth posture (`__Host-*` same-site cookies, CSRF gate) is preserved and the app is reachable on a single browser origin.
- **Observable Completion Condition [Required]**: `GET /api/health` returns 200 on the deployed origin; a real browser login → read flow succeeds same-origin with no CORS preflight; rolling back the image recovers the app with no DB change.
- **Scope Boundary [Required]**: ECS/Fargate + ALB + RDS + CloudFront/S3 (Option A). App code changes limited to: add `/api/health`, extend `BootGuard` fail-fast, enable forwarded headers. No auth/session/cookie logic changes.
- **TDD Enforcement Proposal (Reference Only) [Optional]**: `disabled` — this is infrastructure; the app behaviour under test is unchanged. Verification is integration/smoke, not unit TDD.

---

## 1. Non-Goals

- No multi-region, no auto-scaling beyond one task (PG `NOTIFY` SSE rework deferred).
- No API behaviour change; auth invariants I-21/22/23 stay untouched.
- No CI/CD pipeline rebuild beyond what the release step needs (the existing `npm run verify` CI stays).

## 2. Risks & Mitigations

| Risk | Severity | Mitigation |
| :--- | :--- | :--- |
| `ConnectionStrings__Default` missing at boot | High | Extend `BootGuard` to assert presence; crash named at startup |
| Cookies not `Secure` in browser | High | ALB HTTPS listener only; `Secure` flag already set by `AuthCatalog` |
| Scheme wrong behind ALB (absolute URLs/SSE) | Medium | Enable `UseForwardedHeaders` for ALB CIDR |
| Migrations drift from model | High | Single migration path; `dotnet ef` or migrate-on-boot, verified in CI |
| Key ring lost on new task | High | Already RDS-backed (`data_protection_keys`, `-074`) |

## 3. Environment Contract (summary)

See `docs/aws-deployment.md` §3. Injected at runtime from Secrets Manager / task def; nothing secret committed.

## 4. Acceptance Criteria (Gherkin)

### Scenario 1: Health & boot
- **Given** a fresh RDS + ECS deploy with valid `ConnectionStrings__Default`
- **When** the task starts and `GET /api/health` is called
- **Then** it returns `200` with a DB ping latency, and the schema matches the EF model (`data_protection_keys` present)

### Scenario 2: Same-origin auth (the whole point)
- **Given** the deployed single origin `https://app.example.com`
- **When** a browser loads the SPA and calls `GET /api/applications`
- **Then** the request is `200`, the `__Host-JTSession` cookie is sent same-site, and **no CORS preflight** occurs

### Scenario 3: Fail-fast
- **Given** `ConnectionStrings__Default` is absent
- **When** the task boots
- **Then** it crashes at startup with a named missing-variable error, not after a user request

### Scenario 4: Rollback
- **Given** a bad deploy
- **When** the previous image/task-def revision is redeployed
- **Then** the app recovers with no database change

### Scenario 5: No cross-origin regression
- **Given** the single-origin topology is used
- **When** the build's cookies are inspected
- **Then** no `SameSite=None` cookie is emitted and `-073`/`D-3` remain not implemented (verified by `grep`)

### Scenario 6: Post-deploy smoke
- **Given** the deployed origin
- **When** a synthetic user logs in and reads
- **Then** the flow passes over the real HTTPS origin

## 5. Verification

- `npm run verify` stays green (frontend unchanged).
- API: `dotnet test` green; migration applied against a real Postgres (Testcontainers in CI) and asserted to match model.
- Deploy to a staging origin with synthetic seed; run Scenarios 1–6 manually/synthetically.

## 6. Sign-off & Grilling Checklist

- [ ] Architecture challenged via `pk:grill` (deploy posture is low-risk; grill focused on M4 — re-grill only if topology changes)
- [ ] Owner ratified `docs/aws-deployment.md` decisions (D-M5-1…4, Option A)
- [ ] Zero-downtime migration ordering verified
- [ ] Non-goals agreed
- [ ] Ready for `pk:ship` + human deploy approval
