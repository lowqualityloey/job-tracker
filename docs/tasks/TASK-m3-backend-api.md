# Task Record: M3 — ASP.NET Core Web API over PostgreSQL (swap the persistence medium, not the interface)

<a id="TASK-m3-backend-api"></a>

> **Fill-in note for this project:** M2's records used the legacy compact form. This one uses
> `sdlc-overlay-v1` because a canonical Planning Record (`PLAN-m3-backend-api`) and Assumption Records exist
> and must be linked rather than summarised. **No field is left blank to imply a value**: `Pending` means
> "not executed yet", `N/A - <reason>` means "does not apply", and `None` is used only where there is truly
> nothing to record.

## 1. Identity and Authority

- **Record Type**: `Task Record`
- **Task ID**: `TASK-m3-backend-api`
- **PromptKit Adaptation Profile**: `sdlc-overlay-v1`
- **Work Type**: `Code Work` (Slice 0 contains `Configuration Work` steps — recorded per-behaviour in §7, since one work type cannot describe both honestly)
- **Planning Record Link**: [`PLAN-m3-backend-api`](../specs/2026-09-11-spec-m3-backend-api.md#PLAN-m3-backend-api)
- **Planning Depth Reference**: `Full`
- **Assumption Record Links**: [`ASSUMPTION-m3-backend-api-001`](../specs/2026-09-11-spec-m3-backend-api.md#ASSUMPTION-m3-backend-api-001) (RDS offers 18.6), [`-002`](../specs/2026-09-11-spec-m3-backend-api.md#ASSUMPTION-m3-backend-api-002) (single instance), [`-003`](../specs/2026-09-11-spec-m3-backend-api.md#ASSUMPTION-m3-backend-api-003) (local records are dev data)
- **Specification**: [`docs/specs/2026-09-11-spec-m3-backend-api.md`](../specs/2026-09-11-spec-m3-backend-api.md)
- **External Reference (Optional)**: `N/A` — the Local Task Record is authoritative; no GitHub issues created (deviation from `pk:tasks` Phase 4.2 recorded in §8)
- **Owner / Actor**: Lead Engineer (accountable; approves and merges) · Assistant (executing)
- **Execution Scope**: repo `job-tracker`. New: `api/**`. Changed: `src/data/**` only, `.github/workflows/ci.yml`, `src/domain/types.ts` (one additive field), `docs/**`. **Never**: `src/pages/**`, `src/components/**`, `src/state/**`
- **Approval Boundary**: installing the .NET SDK (**exact version, on the host**); creating/pulling the PostgreSQL container; adding any **runtime** dependency to the frontend (`package.json` dependencies — currently 3, unchanged since M0); pushing to `main`; merging; tags/releases/deploy; deleting the localStorage adapter (M4); **and any second widening of `RepositoryError` after this one**
- **Created**: 2026-09-11 03:32 UTC

> Approval status, stated plainly: the owner approved this design by **merging PR #7 on 2026-09-11**, which included §7's checklist items — the eighth `RepositoryError` variant, the PostgreSQL 18.6 and .NET 10/10.0.401 pins, and the three assumptions. If any of those was **not** your intent when you merged, say so before Slice 1; after Slice 1 the reversal cost stops being one commit.

## 2. Objective and Boundaries

- **Objective**: With `VITE_API_BASE_URL` set, the six-method `ApplicationRepository` seam is satisfied by an HTTP adapter over a real ASP.NET Core API and PostgreSQL, and **zero files in `src/pages/`, `src/components/`, or `src/state/` change** — with cross-tab sync, optimistic concurrency, and server-authoritative validation all working.
- **In Scope**:
  - `api/` — ASP.NET Core 10 minimal-API project + test project, `ApplicationCatalog`, EF Core 10.0.12 + Npgsql 10.0.3, one migration
  - PostgreSQL 18.6 via Docker, loopback-published, dev-only
  - `src/data/httpApplicationRepository.ts` (fetch + `EventSource` + problem+json → `RepositoryError`), adapter selection in `src/data/applicationStore.ts`
  - `RepositoryError` gains `{ code: 'conflict'; id: string }`; `JobApplication` gains `revision?: number`
  - CI: a second job gated on `api/**`
  - The CDP real-browser harness, re-used as Slice 4's acceptance test
- **Explicit Non-Goals**:
  - Authentication, authorisation, `owner_id`, tenants (M4) — and therefore **no public exposure of this API before M4**
  - Any AWS/deployment work (M5); rate limiting; pagination/search/sorting server-side; bulk import/export; OpenAPI **codegen**; soft delete/audit history
  - **Deleting `LocalStorageApplicationRepository`** or migrating existing local records (Assumption 003)
  - Server-side filtering (filters stay view state — M2b's decision, locked as invariant I-5)
  - Any frontend runtime dependency (the API client is `fetch`)
- **Dependencies**: owner approval (**met** by PR #7 merge) · .NET SDK install in Slice 0 (**not yet installed**, deliberately) · Docker **29.7.2, present and measured 2026-09-11** · `UNCERTAINTY-DECISION-m3-backend-api-003-001` (Npgsql⇄EF compatibility) resolved or falsified by Slice 0's first migration command · `UNCERTAINTY-DECISION-m3-backend-api-008-001` (Docker on CI runners) resolved by Slice 0's first CI run, fallback = GitHub service container
- **Risk**: `Medium` — **High** upside on learning value, **Low** blast radius. Nothing is deployed; the swap reverses by unsetting one variable; the database holds dev data and is droppable. The two genuinely risky items are (1) the widened union touching M2a's locked vocabulary — mitigated because the change is additive and every existing pattern-match is table-driven and tested, and (2) a new runtime/CI path with no precedent in this repo — mitigated by Slice 0 being configuration-only, so the first failure is in scaffolding, not in a half-written feature.
- **Verification Condition**: `dotnet test` green in `api/` **and** `npm run verify` green with the flag unset **and** `BEHAVIOR-m3-backend-api-042` passing — the CDP harness run against the HTTP-backed build in real Chromium, whose output is quoted into §6 rather than summarised.

## 3. Acceptance Criteria

Every AC is objectively checkable and names its command. `Result: Pending` until executed. Gherkin scenarios for the behaviours a user can observe; AC ids are stable and **do not renumber** if the ladder below changes.

- [ ] **AC-1** — **The seam holds.** *Given* `VITE_API_BASE_URL` is set, *When* the app runs, *Then* CRUD through the HTTP adapter works and `git diff --name-only` lists **no** file under `src/pages|components|state`. **TypeScript's structural check is the first line of this test, not the last.**
  - **Result**: Pending · **Evidence**: `git diff main --name-only -- src/pages src/components src/state` → empty · `npx tsc -p tsconfig.app.json --noEmit` → 0
- [ ] **AC-2** — **Flag off changes nothing.** `npm run verify` green with `VITE_API_BASE_URL` unset, exactly as on `main` today. **Result**: Pending · **Evidence**: `npm run verify` exit 0
- [ ] **AC-3** — **Durability outside the client process.** *Given* a record created in the browser, *When* the browser is closed and reopened, *Then* the record is present **and** `psql -c 'select id from applications'` shows it. **Result**: Pending · **Evidence**: `BEHAVIOR-…-043` + quoted `psql` output
- [ ] **AC-4** — **Constraints are real, not advisory.** A status outside the five is rejected **by the database** even when the API validator is bypassed (direct SQL). **Result**: Pending · **Evidence**: `BEHAVIOR-…-032`; the Red runs `INSERT … status='Escalated'` and asserts violation
- [ ] **AC-5** — **No timezone drift on a date-only field.** `applied_at = 2026-08-10` round-trips as `"2026-08-10"` for a client whose machine is UTC+13. **Result**: Pending · **Evidence**: `BEHAVIOR-…-027` with `TZ=Pacific/Auckland`
- [ ] **AC-6** — **Conflict detection, with the row untouched.** *Given* two tabs hold the same revision, *When* the second saves, *Then* the API answers `409` with `code:"conflict"` **and the stored row is still the first save's content**. **The "row unchanged" half is the assertion; a 409 that also wrote is the worst bug available in this milestone.** **Result**: Pending · **Evidence**: `BEHAVIOR-…-033`
- [ ] **AC-7** — **Create is idempotent under retry.** A retried `POST` with the same client-minted id yields `409`, the adapter re-reads and treats it as success, and the table holds **exactly one** row. **Result**: Pending · **Evidence**: `BEHAVIOR-…-034`
- [ ] **AC-8** — **The error table is total.** Every documented `HTTP × code` pair maps to its stated `RepositoryError` variant, and an **unrecognised** code maps to `corrupt-data` — no silent default, no throw. **Result**: Pending · **Evidence**: `BEHAVIOR-…-037` table-driven, one case per row of spec §4.3
- [ ] **AC-9** — **Server unreachable degrades like storage blocked.** A `fetch` rejection maps to `unavailable`, and the provider's existing refusal gate behaves exactly as M2a's tests describe. **Result**: Pending · **Evidence**: `BEHAVIOR-…-038`
- [ ] **AC-10** — **Out-of-order re-reads cannot repaint stale data** (closes M2b's handed-over **P2-2**). *Given* request 1 is delayed and request 2 resolves first, *When* request 1 lands, *Then* the list still shows request 2's data. **Result**: Pending · **Evidence**: `BEHAVIOR-…-039`
- [ ] **AC-11** — **`subscribe()` over SSE keeps its contract.** A write from another client triggers the callback and one re-list; after a simulated disconnect+reconnect the adapter re-reads **exactly once**; the returned unsubscriber closes the stream and no further callbacks occur. **Result**: Pending · **Evidence**: `BEHAVIOR-…-040`
- [ ] **AC-12** — **Two validators, one truth — by one fixture, not by coincidence** (grill F-1). A single checked-in `api/tests/fixtures/validation-cases.json` is consumed by **both** suites: the xUnit theory and a vitest case read the same `{ input, expect }` rows, so the C# validator cannot pass its own opinion. Boundary values: empty, max, max+1, whitespace-only, unicode, control chars, 10 kB notes, all five statuses + a sixth. **The grill caught a real divergence, and reading `validation.ts` narrowed it further (grill §4):** the client caps `companyName`/`jobTitle`/`location` at `MAX_TEXT_LENGTH = 120`, so **`notes` alone has no client ceiling** — a server limit there is a server-only rule and needs its own case. And **`status` has no runtime client validation at all** (a TS union), so feeding "a sixth status" to both validators was **impossible as written**; the fixture carries a per-side expectation instead. **Result**: Pending · **Evidence**: `BEHAVIOR-…-031` + the contract test named in §8
- [ ] **AC-13** — **CI jobs are independent.** The API job is gated by an **internal `if:` on changed paths, not by `on.pull_request.paths`** (grill F-10: an excluded job reports *no status*, which is indistinguishable from a check that never ran the day branch protection requires all checks). So the job always reports and only its steps skip. **Result**: Pending · **Evidence**: two PR pushes — a docs-only push showing job *success / steps skipped* and an `api/**` push showing steps executed, both run lists quoted
- [ ] **AC-14** — **Locked invariants I-1…I-6 still hold** (spec §3), checked by command rather than by re-reading code: no `fetch`/`EventSource` outside `src/data/`, no `Number(id)` anywhere, **runtime dependency count still 3**, no `.only`/`.skip`, no `vite.config.js` in the tree, **and `revision` referenced nowhere outside `src/data/` + its declaration** (grill F-5: the field's isolation was a comment; it is now a grep). **Result**: Pending · **Evidence**: §8's invariant scan block, output quoted verbatim

## 4. Execution Policy

- **Mode**: `Gated Mode`
- **TDD Enforcement Mode**: `enabled` — canonical here. The Planning Record's identical proposal (`§Planning Inputs`) **agrees**, so readiness is not blocked by a mode disagreement; `pk:test`'s test plan must match this value or readiness blocks until reconciled.
- **Batch Authorization**: `N/A` — Gated Mode
- **Soft Checkpoint**: ~60 minutes of working time, or one behaviour completed, whichever first
- **Hard Checkpoint**: 90 minutes, or **any** scope expansion, or the first CI failure on the new API job
- **Event-Driven Checkpoints**: milestone/slice boundary, task switch, scope expansion, handoff, compaction, context drift
- **Stop Conditions**: missing approval or context · failed verification or CI · invariant violation (I-1…I-6) · hard checkpoint · a `dotnet ef` incompatibility falsifying `UNCERTAINTY-…-003-001` · developer stop
- **Host Timer Capability**: `None mechanical` — no timer enforcement was observed in this harness; checkpoint discipline is upheld by measuring with `date -u` at slice boundaries, and that limitation is stated rather than presented as enforcement.

## 5. State and Active Ownership

- **Execution State**: `in_progress` — claimed by the owner's merge of PR #10, whose reviewer-focus item was
  explicitly "promote to `in_progress`, which authorises Slice 0"
- **Mapped `pk:tasks` Status**: `In Progress`
- **Active Task Pointer**: `TASK-m3-backend-api` (this record holds it; Slice 0 is executing, not complete)
- **Start Time**: 2026-09-11 06:00 UTC — the first measured timestamp *inside* the slice. It began after the 05:03 reconciliation
  of PR #10 and no earlier value was captured, so this is a bound, not false precision.
- **Current Actor**: Lead Engineer (review/approval) · Assistant holds no execution authority from this record until Slice 0 begins
- **Next Action (Slice 0 complete)**: **`BEHAVIOR-m3-backend-api-026`** — write the failing test that
  `GET /api/applications` returns `200 []`, then the `DbSet`, the entity, the endpoint and the **first real
  migration** its Green requires. (Preceding text read "**start Slice 0** — install SDK **10.0.401** and scaffold `api/` + `docker-compose.yml` + the path-gated CI job, under §1.3's exception-verification path (no Red test: there is no behaviour yet). Slices 0–1 are unblocked; **Slice 2 is not** (F-1's fixture must exist first). Gates now closed: `pk:grill` ran at 2026-09-11 03:50 UTC → [`GRILL-m3-backend-api`](../reviews/2026-09-11-m3-plan-grill.md): 12 findings, 2 conditions (F-1, F-7), 4 plan amendments (F-2…F-5). Exactly one action; Slice 0 does not start until the intent register exists.

### Transition History

| Previous State | New State | Timestamp | Actor | Reason | Supporting Evidence |
|---|---|---|---|---|---|
| — (none) | `planned` | 2026-09-11 03:32 UTC | Assistant (`pk:tasks`) | Decomposed from an approved Full Planning Record; readiness deliberately **not** claimed because `pk:test`'s plan and `pk:grill` have not run | [PR #7 merge (`84bf560`)](https://github.com/lowqualityloey/job-tracker/commit/84bf560) |
| `planned` | `in_progress` | 2026-09-11 06:00 UTC | Assistant (Slice 0) | Owner merged PR #10, whose focus item asked for exactly this promotion. Slice 0 ran the exception-verification path: SDK 10.0.401 installed, projects scaffolded, container harness proven, migration applied |
| `planned` | `planned` | 2026-09-11 05:03 UTC | Assistant (`pk:test`) | Test strategy and the **TDD intent register** produced: 20 intents, all `ready`, mode **reconciled** with §4 (`Matches Task Record`). State still not promoted by the workflow that prepared it — **readiness is the owner's call on this PR**, and it is now unblocked in substance | [`docs/tests/2026-09-11-test-m3-backend-api.md`](../tests/2026-09-11-test-m3-backend-api.md) |
| `planned` | `planned` | 2026-09-11 03:50 UTC | Assistant (`pk:grill`) | Design gate executed, **state deliberately unchanged** — a grill that automatically promotes readiness is a grill with no teeth. Slice 2 now carries a blocking condition (F-1: AC-12 had no cross-language mechanism) and two contract amendments await the owner (F-3 `If-Match` on `DELETE`, F-4 JSON-only bodies) | [`GRILL-m3-backend-api`](../reviews/2026-09-11-m3-plan-grill.md) · PR #8 merged (`e58da50`) |

## 6. Evidence and Completion Gate

*All fields below are `Pending` / `None` by construction — this record is created before execution, and pre-filled evidence would be fabrication.*

- **Changed Files (Slice 0)**: new `global.json`; `api/{JobTracker.slnx,README.md,docker-compose.yml}`;
  `api/src/JobTracker.Api/{Program.cs,*.csproj,appsettings*.json,Properties/launchSettings.json,Data/JobTrackerDb.cs,
  Data/JobTrackerDbFactory.cs,Migrations/*}`; `api/tests/JobTracker.Api.Tests/{*.csproj,PostgresHarness.cs}`;
  `.gitignore`; `.github/workflows/ci.yml`. **`src/**`: untouched** (verified by `git diff main --name-only -- src/` → empty)
- **Scope Change Records**: `None`
- **Checkpoint Records**: `None` (a `TASK-m3-backend-api.checkpoint-00N.md` is written at any hard checkpoint)
- **Handoff Records**: `None`
- **Verification Evidence — Slice 0 (§1.3 exception path)**, output quoted rather than summarised:

| §1.3 acceptance | Command | Observed |
| :--- | :--- | :--- |
| SDK pinned | `dotnet --version`, `dotnet --list-sdks` | `10.0.401` — `DECISION-m3-backend-api-002`'s pin exactly, held in `global.json` with `rollForward: disable` |
| Projects build | `dotnet build api/JobTracker.slnx` | **0 Warning(s), 0 Error(s)** |
| Tests run | `dotnet test api/tests/JobTracker.Api.Tests` | `Passed: 4, Failed: 0` |
| Container real *and fresh* | `select pg_postmaster_start_time()` asserted inside the fixture's own window; `docker events` | daemon logged `testcontainers-ryuk-*` plus a cold `postgres:18.6` at each run |
| Server is the pinned version | `show server_version`; `psql -tAc 'select version()'` | `PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)` |
| `CHECK` is enforced | `insert … values ('Escalated')` | `PostgresException`, `SqlState 23514` — the premise `BEHAVIOR-…-032` rests on |
| Migration applies | `dotnet ef database update` | `Applying migration '20260911055250_ToolchainProvesMigrationPipeline'. Done.` |
| Recorded server-side | `select "MigrationId","ProductVersion" from "__EFMigrationsHistory"` | `20260911055250_ToolchainProvesMigrationPipeline \| 10.0.12` |
| Schema empty on purpose | `information_schema.tables` count | `0 user tables so far` — the table is Slice 1's Red test's job |
| Design-time guard refuses to guess | `env -u ConnectionStrings__Default dotnet ef dbcontext info` | `The exception 'ConnectionStrings__Default is not set…' was thrown` |
| Frontend gate untouched | `npm run verify` | exit **0** — 100 tests, build ✓ |
| Nothing build-shaped staged | `git add -An --dry-run api global.json \| grep -cE 'bin/\|obj/'` | `0` of 16 files |

**Three findings Slice 0 exists to produce, and did:**

1. **`UNCERTAINTY-m3-backend-api-003-001` resolved, with a nuance the spec did not anticipate.** EF Core 10.0.12 and
   Npgsql EF 10.0.3 *are* compatible — the provider declares `[10.0.4, 11.0.0)` — but the first build emitted
   `MSB3277`: the app compiled against **10.0.12** while the test project resolved **10.0.4**, because NuGet
   resolves a range **independently per project** and a *project reference does not carry your chosen version*.
   Fixed by pinning `Microsoft.EntityFrameworkCore` explicitly in **both** projects. The pin was usable; what the
   spec missed is that "pin the provider" ≠ "pin the graph".
2. **`DECISION-m3-backend-api-001` needs a companion fact: the volume path is part of the version pin.**
   `postgres:18`+ images **refuse** a mount at `/var/lib/postgresql/data` and exit 1 ("there appears to be
   PostgreSQL data in … unused mount/volume"); it belongs at `/var/lib/postgresql`. Found by the container failing,
   not by reading release notes — and my earlier probe had succeeded *only because it had no volume*, which is why
   nothing contradicted the compose file until it actually ran.
3. **`UNCERTAINTY-m3-backend-api-008-001` (Docker from a .NET process) resolved locally**: Testcontainers started,
   served and cleaned up four containers from inside `dotnet test`. The CI half is this PR's first `api` job.

- **Verification Evidence — ACs**: `Pending`. Slice 0 asserts no AC; AC-2's `npm run verify` was re-run at the
  slice boundary and is green.
- **Behavior IDs [Required when enabled]**: `BEHAVIOR-m3-backend-api-026` … `-043` — see the ladder in §7
- **TDD Intent Register [Required when enabled]**: [`docs/tests/2026-09-11-test-m3-backend-api.md`](../tests/2026-09-11-test-m3-backend-api.md) — 20 `TDD-INTENT-m3-backend-api-<nnn>` rows, one per behaviour, each `ready` with a concrete Red command and expected failing assertion. **Mode reconciliation: `Matches Task Record`** (both say `enabled`), so readiness is no longer blocked on this field. *Filename follows `pk:test`'s template (`YYYY-MM-DD-test-<feature>.md`); the earlier `TEST-m3-backend-api.md` name in this record was mine and is superseded.*
- **TDD Execution Evidence [Required when enabled]**: `None yet` — one `TDD-EXEC-m3-backend-api-<seq>` block per behaviour, recorded in §7's evidence column as execution proceeds, each retaining its behaviour identity and re-running the same Red command
- **TDD Exception Verification**: `N/A - Code Work`, **except** Slice 0's configuration steps (`SDK install`, CI wiring), which use the exception path with reason `Configuration Work: no observable behaviour to assert before the stack exists; evidence is command output`
- **CI Evidence — the first `api` job run FAILED, and why it was right**: provider GitHub Actions, workflow
  `ci.yml`, job `verify-api`, commit `47501b6`, 2026-09-11 06:07 UTC:
  - `error CS8605: Unboxing a possibly null value` — `PostgresHarness.cs:86` cast `ExecuteScalarAsync()`
    (which returns `object?`) straight to `DateTime`. Locally invisible because my build was incremental **and**
    my grep pattern was `Passed!|Failed!|error CS` — `warning` was not in it, so the line that would have told
    me was discarded by the reading. Fixed with `var started = (DateTime?)…` + `Assert.NotNull(started)`, which
    also converts a null answer into a *test failure* instead of a `NullReferenceException` dressed as one.
  - `warning MSB3277` survived for `Microsoft.EntityFrameworkCore.Relational` (10.0.4 vs 10.0.12): my earlier
    "fix" pinned only the assembly the first warning happened to name. Replaced structurally by
    `api/Directory.Packages.props` with `CentralPackageTransitivePinningEnabled` — one version per package
    across the graph, so correctness stops depending on my remembering both projects *and* both siblings.
  - Both then rebuilt from scratch (`bin/ obj/` deleted) with `-p:TreatWarningsAsErrors=true`: **0 warnings,
    0 errors**, and `dotnet test` → `Passed: 4, Failed: 0`.
  - **`TreatWarningsAsErrors` earned its place on its first run**: it failed a build my own machine passed.
    Slice 0's whole purpose was to surface exactly this kind of gap while nothing depends on it.
  - Second run of this job (post-fix) quoted in the PR comments. AC-13's other half (a docs-only push → job
    reports success with steps skipped) is asserted on the next docs PR. Disclosed honestly: my first local test
    of the detection pattern was **vacuous** — `git diff main...HEAD` on an uncommitted tree is empty, and an
    empty diff and an untriggered gate look identical. Near-miss paths (`api-notes.md`, `global.json.bak`,
    `src/api/client.ts`) were then verified to yield `false`.
- **CI Evidence — superseded text**: Slice 0 **adds the second job** (`api`), so this PR's run resolves the
  Docker-on-runners question on the provider that will actually execute it. AC-13's other half (a docs-only push →
  job reports success, steps skip) is asserted on the next docs PR. Disclosed honestly: my first local test of the
  detection pattern was **vacuous** — `git diff main...HEAD` on an uncommitted tree is empty, and an empty diff and
  an untriggered gate look identical. Near-miss paths (`api-notes.md`, `global.json.bak`, `src/api/client.ts`) were
  then verified to yield `false`.
- **Review Evidence**: **design gate**: [`GRILL-m3-backend-api`](../reviews/2026-09-11-m3-plan-grill.md) (12 findings; F-7 = `pk:grill` has **no workflow definition** in `.promptkit/workflows/`, so the gate ran from its advertised two-line contract and its self-assessment weakness is stated in the record's head). **Code review**: `Pending` — `REVIEW-m3-backend-api` at `docs/reviews/`, two-axis, at PR time
- **Commit Evidence**: `Pending`
- **Pull Request Evidence**: this PR (#8), then one PR per slice
- **Release Evidence**: `N/A` — no release before M5
- **Blocker and Resume Condition**: **Slice 2 is blocked** on F-1 (shared `api/tests/fixtures/validation-cases.json` consumed by both xUnit and vitest — without it AC-12 is two test files encoding two opinions). **Slice 0 is complete; Slice 1 is unblocked.** Owner attention awaited on F-3 and F-4, which change the §4.3 contract, and on F-7's remedy. **To proceed**: `pk:test` → Slice 0.

## 7. Behaviour Ladder (decomposition, sizing, priorities)

Phase-2 sizing rule applied: each row is one unit inside the **1–4 hour** band. `Red command` names the test file that behaviour creates, so the first commit of each behaviour is a failing test, never an implementation. Labels: `area:` / `type:` / `priority:` follow `pk:tasks` Phase 2 conventions.

| Behaviour | Slice | Unit of work | Est. | Pri | Area / Type | Red → Green command |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| — (config) | 0 | SDK install + scaffold + compose + CI job + `.gitignore` | 3–4h | p0 | `area:backend` `type:feature` | exception path — output of `dotnet --version`, `dotnet test` (must run 0 tests red-styled), `docker compose up` readiness |
| `BEHAVIOR-…-026` | 1 | empty list returns `[]` not 404 | 1h | p0 | `area:backend` `type:test` | `dotnet test --filter ~ApplicationsQueryTests.Empty_list_returns_200_empty_array` |
| `…-027` | 1 | full field round-trip incl. `date` without drift | 2h | p0 | `area:backend` `type:test` | `…~RoundTripsEveryField` (run with `TZ=Pacific/Auckland`) |
| `…-028` | 1 | unknown id → 404 problem `not-found` | 1h | p1 | `area:backend` `type:test` | `…~UnknownId_returns_404_problem` |
| `…-029` | 1 | list carries a revision that changes on write | 1h | p1 | `area:backend` `type:test` | `…~Revision_changes_after_update` |
| `…-030` | 2 | create persists and is visible to a second client | 1.5h | p0 | `area:backend` `type:test` | `…~Create_visible_to_second_client` |
| `…-031` | 2 | server rejects what the client rejects, with pointers | 3h | p0 | `area:backend` `type:test` | `…~Validation_matches_client_boundaries` (the AC-12 contract test) |
| `…-032` | 2 | DB `CHECK` rejects a sixth status | 1h | p1 | `area:data` `type:test` | `…~Status_check_constraint_rejects_sixth_value` |
| `…-033` | 2 | stale `If-Match` → 409 **and row unchanged** | 2.5h | p0 | `area:backend` `type:test` | `…~Stale_if_match_conflicts_without_writing` |
| `…-034` | 2 | retried create → 409, one row only | 1.5h | p1 | `area:backend` `type:test` | `…~Retry_of_create_is_idempotent` |
| `…-035` | 2 | delete → 204, then 404 | 1h | p1 | `area:backend` `type:test` | `…~Delete_then_missing` |
| `…-036` | 3 | env var selects the adapter; both satisfy the type | 1.5h | p0 | `area:data` `type:feature` | `npx vitest run src/data/applicationStore.test.ts` |
| `…-037` | 3 | total HTTP → `RepositoryError` mapping table | 3h | p0 | `area:data` `type:test` | `npx vitest run src/data/httpApplicationRepository.test.ts -t "error mapping"` |
| `…-038` | 3 | transport failure → `unavailable` + refusal gate | 1.5h | p0 | `area:data` `type:test` | `…  -t "server unreachable"` |
| `…-039` | 3 | out-of-order re-read guard (**P2-2 closed**) | 2.5h | p1 | `area:data` `type:test` | `…  -t "ignores an older response that lands late"` |
| `…-040` | 3 | SSE callback + unsubscribe + **at most one re-list per `open`, incl. three rapid reconnects** *(grill F-8: bounded was reasoned, now it is asserted)* | 3h | p1 | `area:data` `type:test` | `…  -t "subscribe"` |
| `…-041` | 3 | unicode/long-string fidelity through JSON | 1h | p2 | `area:data` `type:test` | `…  -t "survives unicode"` |
| `…-042` | 4 | real browser, real Chromium, HTTP-backed build | 2h | p0 | `area:frontend` `type:test` | the CDP harness in `docs/spikes/…/crosstab.mjs` against `VITE_API_BASE_URL` |
| `…-043` | 4 | survives browser restart; `psql` confirms | 1h | p1 | `area:data` `type:test` | harness + quoted `psql` output |
| `…-044` | 2 | **delete carrying a stale revision is refused and the row still exists** *(grill F-3 — `If-Match` now required on `DELETE`)* | 1h | p0 | `area:backend` `type:test` | `…~Delete_with_stale_revision_is_refused` |
| `…-045` | 2 | **`text/plain` body → `415`, no row created** *(grill F-4 — makes the preflight the real cross-origin write guard)* | 1h | p1 | `area:backend` `type:test` | `…~Non_json_body_rejected` |

**Refactor milestones** are not separate rows: each behaviour's Refactor step re-runs its own Red command and is recorded in the `TDD-EXEC` block with either a passing result or an explicit `no-refactor reason` (the template's allowance — an empty Refactor cell is a lie waiting to be read as skipped).

**Identity reconciliation, stated so two documents cannot drift:** spec §6 wrote these as bare `BEHAVIOR-026…043`; the canonical form required by `pk:tasks` is `BEHAVIOR-m3-backend-api-<nnn>`. **Same numbers, same meanings, one identity — this record's form wins**, and the approved spec is left unedited rather than quietly amended (its "Slice 0 → PR #8" reference is off by one because this decomposition took PR #8; recorded here instead of edited there, because an approved artefact that can be edited after approval is not an approved artefact).

## 8. Task index and optional external sync

Copy-pasteable, **not executed** — GitHub issues were deliberately not created, matching M2's precedent that the Local Task Record is authoritative and a parallel issue tracker becomes a second source of truth. If the owner wants a public board, this block is ready to run:

```bash
# one command per behaviour, e.g. (labels must exist first; they are not created implicitly)
gh issue create --title "test(m3): BEHAVIOR-m3-backend-api-033 stale If-Match conflicts without writing" \
  --body "AC-6 / spec DECISION-m3-backend-api-006 · slice 2 · p0 · area:backend type:test" \
  --label "priority:p0,area:backend,type:test"
```

**Invariant scan for AC-14** (run at each slice boundary; output quoted into §6, no paraphrasing):

```bash
git diff main --name-only -- src/pages src/components src/state      # must be empty (AC-1)
grep -rn "fetch(\|EventSource" src --include=*.ts --include=*.tsx | grep -v '^src/data/' | grep . && echo "I-1 VIOLATED"
grep -rn "Number(id\|parseInt(id" src && echo "I-2 VIOLATED"
node -p "Object.keys(require('./package.json').dependencies).length"  # must print 3 (I-6 / AGENTS.md)
grep -rn "\.only\|\.skip\|console\.log" src && echo "HYGIENE VIOLATED"
test -e vite.config.js && echo "I-5/DEBT-11 VIOLATED"
```

**Test-stack note** (`DECISION-m3-backend-api-008`, status `proposed`): xUnit + `Testcontainers.PostgreSql` with versions pinned **at creation time** against the NuGet feed and that access date recorded here. If Slice 0's CI run shows no Docker on runners, the fallback is a GitHub **service container** — a fixture swap, not a test rewrite, which is why the choice was allowed to stay `proposed`.
