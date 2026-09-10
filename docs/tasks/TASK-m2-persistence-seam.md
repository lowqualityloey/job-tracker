# Task Record: Persisted CRUD behind a repository seam (M2a)

<a id="TASK-m2-persistence-seam"></a>

> **Read this first.** This Local Task Source is authoritative for scope, acceptance, execution state, and
> TDD mode. The Planning Record supplies inputs and context but approves nothing. A behaviour may not be
> re-scoped, re-sequenced, or silently skipped here once this record is active — deviations go through a
> Scope Change Record first.

## 1. Identity and Authority

- **Record Type**: `Task Record`
- **Task ID**: `TASK-m2-persistence-seam`
- **PromptKit Adaptation Profile**: `sdlc-overlay-v1`
- **Work Type**: `Code Work`
- **Planning Record Link**: `PLAN-m2-persistence-seam` — `docs/specs/2026-09-10-spec-m2-persistence-seam.md#planning-record-promptkit-adaptation`
- **Planning Depth Reference**: `Full`
- **Assumption Record Links**: `ASSUMPTION-m2-persistence-seam-001, ASSUMPTION-m2-persistence-seam-002`
- **Specification**: `docs/specs/2026-09-10-spec-m2-persistence-seam.md`
- **External Reference (Optional)**: `N/A` (no issue tracker items exist; `pk:tasks` GitHub sync deferred)
- **Owner / Actor**: Lead Engineer (accountable) · Assistant (executing agent)
- **Execution Scope**: Repository `job-tracker`, branch `feat/m2-persistence-seam` off `main@63d769d`; paths
  `src/**`, `docs/**`. No workspace/package boundary (standalone repo)
- **Approval Boundary**: The agent **may** edit in-scope files, run typecheck/tests/build, and make atomic
  commits on this branch. The agent **may not** merge a PR, push to `main`, tag, release, deploy, run
  destructive git commands, delete or migrate a user's stored data, or begin M2b/M3 work
- **Created**: 2026-09-10 21:05 UTC (record authored); approved and started 21:14 UTC

## 2. Objective and Boundaries

- **Objective**: A user can create, edit, and delete a job application and still see those changes after a
  page reload, with all reads and writes passing through one `ApplicationRepository` interface.
- **In Scope**:
  - `src/domain/validation.ts`, `src/domain/pipeline.ts`, `src/domain/applicationRepository.ts`
  - `src/data/localStorageApplicationRepository.ts`, `src/data/inMemoryApplicationRepository.ts`,
    `src/data/seedApplications.ts`
  - `src/state/applicationsProvider.tsx` (+ `useApplications` hook)
  - `src/components/ApplicationForm.tsx`, `src/components/StorageNotice.tsx`
  - `src/pages/DashboardPage.tsx`, `ApplicationsPage.tsx`, `ApplicationDetailsPage.tsx`, `App.tsx`
  - `src/types/application.ts` — the `id: number → string` contract change
  - Deletion of `src/data/mockApplications.ts` (replaced by the seed module)
  - Tests for every behaviour listed in §3
- **Explicit Non-Goals**:
  - Search, status filters, sorting UI (M2b)
  - Any backend, HTTP client, auth, or new runtime dependency (this record adds **zero** packages)
  - Cross-tab reconciliation / `StorageEvent` handling (documented failure F-5, deferred by decision)
  - Adding an `updatedAt` field to imply conflict capability that is not being built
  - CSS token extraction and anything from `DESIGN.md` §8 (separate PR, separate concern)
  - Closing DEBT-01 (this record *closes* it) or touching DEBT-08/09/10
- **Dependencies**: PR #1 merged (done, `63d769d`) — the `noEmit` fix in `tsconfig.node.json` must be in place
  so verification runs do not emit a shadowing `vite.config.js`. Owner: satisfied.
- **Risk**: **Medium** — durable client state on a device plus an externally visible contract change
  (`id: string`). Mitigations: fail-closed envelope rules (spec §4.2), corrupt-payload quarantine rather than
  discard, TDD per behaviour, and no destructive migration path (the storage key is new).
- **Verification Condition**: `npm run test:run` exits 0 with all §3 behaviours covered, and
  `npx tsc -p tsconfig.app.json --noEmit` exits 0; `npm run build` exits 0 and emits **no** `vite.config.js`;
  manual: add an application → F5 → still listed.

## 3. Acceptance Criteria

Gherkin form; each maps to a stable `BEHAVIOR-` identity used by the TDD commits in spec §6.

- [x] **AC-1** `BEHAVIOR-m2-persistence-seam-001` — **Given** a valid new application **When** it is validated
  **Then** validation succeeds and **And** the created record carries a v4 string `id` and a `createdAt` stamp
  - **Result**: **Partially met** — the validation half is Met; the `id`/`createdAt` half belongs to
    `BEHAVIOR-004` (M2a.2) and is untested. Not closed.
  - **Evidence**: Red `f84a970` (`Tests no tests`, module absent) → Green `fdffc2c`
    (`Tests 1 passed (1)`, exit 0). Final M2a.1 state `1f6aba2` @ `npx vitest run`: 13 passed.
- [x] **AC-2** `BEHAVIOR-m2-persistence-seam-002` **and** `-003` — **Given** a blank/over-length company name,
  or `appliedAt: '2026-02-30'` **When** validated **Then** a field-scoped error is returned for that field
  and nothing is written
  - **Result**: **Met** — every rejected case names exactly one field, and validation runs before any
    repository exists, so nothing can be written on failure.
  - **Evidence**: Red `da9d7a6` (`4 failed | 2 passed`) → Green `714c108` (`6 passed`); Red `82ee71a`
    (`3 failed | 8 passed`) → Green `d35fe10` (`11 passed`); Refactor `1f6aba2` (`13 passed`, tsc exit 0).
    Boundary case `'x'.repeat(120)` accepted, `repeat(121)` rejected.
- [ ] **AC-3** `BEHAVIOR-m2-persistence-seam-004` **and** `-005` — **Given** a created application
  **When** a *fresh* repository instance over the same storage lists records **Then** the new record is present
  (this is the reload guarantee, expressed as a test)
  - **Result**: Pending · **Evidence**: round-trip test against real jsdom `localStorage`
- [ ] **AC-4** `BEHAVIOR-m2-persistence-seam-006` **and** `-007` — **Given** an existing record **When** it is
  updated **Then** `id` and `createdAt` are preserved; **And when** it is removed, reading it yields
  `not-found`
  - **Result**: Pending · **Evidence**: update/remove tests
- [ ] **AC-5** `BEHAVIOR-m2-persistence-seam-008` **and** `-009` — **Given** storage is unavailable or a write
  exceeds quota **When** the app starts or saves **Then** a typed `unavailable` / `quota-exceeded` result is
  returned, a visible notice is rendered, and the in-memory list is **not** mutated or falsely reported as saved
  - **Result**: Pending · **Evidence**: injected-throwing-storage tests
- [ ] **AC-6** `BEHAVIOR-m2-persistence-seam-010` **and** `-011` — **Given** an unparseable payload, or a
  `schemaVersion` newer than this build **When** the repository reads or writes **Then** the raw value is
  preserved (quarantine key) or left byte-identical, and no user data is silently discarded or downgraded
  - **Result**: Pending · **Evidence**: quarantine + fail-closed tests
- [ ] **AC-7** `BEHAVIOR-m2-persistence-seam-013` **and** `-014` — **Given** the new-application form
  **When** the user submits valid input **Then** the record appears in the list; **And when** submission is
  invalid, field errors are announced and the repository is unchanged
  - **Result**: Pending · **Evidence**: form + page rendering tests
- [ ] **AC-8** `BEHAVIOR-m2-persistence-seam-012` **and** `-015` — **Given** the dashboard
  **When** an application is created in the same session **Then** the counts change, with no `filter()` in a
  component module scope (DEBT-05 closed)
  - **Result**: Pending · **Evidence**: provider test + grep assertion in review
- [ ] **AC-9** `BEHAVIOR-m2-persistence-seam-016` — **Given** `/applications/<unknown-id>` **When** rendered
  **Then** the not-found branch appears, with no `Number(id)` coercion (DEBT-07 closed)
  - **Result**: Pending · **Evidence**: details-page test
- [ ] **AC-10** `BEHAVIOR-m2-persistence-seam-017` — **Given** an application row **When** the user deletes it
  **Then** a confirmation is required and the row disappears; the control keeps a ≥44 px target, a visible
  `:focus-visible` ring, and AA contrast (`DESIGN.md` §5/§6)
  - **Result**: Pending · **Evidence**: delete flow test + review note
- [ ] **AC-11** — **Given** the whole branch **When** verification runs **Then** typecheck exit 0, all tests
  green, build exit 0, `vite.config.js` absent afterwards, and `git diff --name-only main...HEAD` shows no
  change to `package.json` dependencies
  - **Result**: Pending · **Evidence**: commands pasted in §8

## 4. Execution Policy

- **Mode**: `Gated Mode` (one behaviour per Red→Green→Refactor cycle, human-visible in the PR)
- **TDD Enforcement Mode**: **`enabled`** — selected by the Lead Engineer on 2026-09-10 (asked directly, answer: "Enabled for domain + repository"). Each §3 behaviour must
  show a failing assertion (Red) before its implementation (Green); no test may be authored after the code it
  covers. Deviations require a TDD Exception Record
- **Batch Authorization**: `N/A` (Gated Mode)
- **Soft Checkpoint**: ~60 minutes of continuous work, or every completed behaviour, whichever first
- **Hard Checkpoint**: at or before 90 minutes, and always before a commit/PR action or context handoff
- **Event-Driven Checkpoints**: milestone complete (each M2a.n), task switch, any scope expansion, handoff,
  compaction, or detected context drift
- **Stop Conditions**: missing approval or missing context, failed verification or invariant violation, a
  blocker, hard checkpoint reached, or a developer stop instruction. Additional hard stop: **any Red test that
  cannot be made Green without changing an acceptance criterion** → stop and open a Scope Change Record
- **Host Timer Capability**: **No mechanical wall-clock timer is available to the executing agent.** The
  60/90-minute checkpoints are self-discipline estimates, not enforced scheduling; the human can trigger a
  `pk:checkpoint` at any time and that event *is* enforceable

## 5. State and Active Ownership

- **Execution State**: `in_progress`
- **Mapped `pk:tasks` Status**: `In Progress`
- **Active Task Pointer**: `TASK-m2-persistence-seam`
- **Start Time**: 2026-09-10 21:14 UTC
- **Current Actor**: Assistant (executing)
- **Next Action**: Land Red for `BEHAVIOR-m2-persistence-seam-004` (`create()` assigns a v4 string `id` and
  `createdAt`) — milestone M2a.2, the localStorage repository

### Transition History

| Previous State | New State | Timestamp | Actor | Reason | Supporting Evidence |
|---|---|---|---|---|---|
| — | `planned` | 2026-09-10 21:05 UTC | Assistant | Record created from `PLAN-m2-persistence-seam` after `pk:plan` | `docs/specs/2026-09-10-spec-m2-persistence-seam.md` |
| `in_progress` | `in_progress` (checkpoint) | 2026-09-10 21:22 UTC | Assistant | M2a.1 (validation) complete: AC-2 met, AC-1 partial; suite 13 passing | `1f6aba2`, `npm run test:run` 13 passed |
| `planned` | `in_progress` | 2026-09-10 21:14 UTC | Assistant (with Lead Engineer's standing "proceed next implementation" + all four architectural forks explicitly ruled) | Readiness fields complete: objective, scope, non-goals, 11 ACs, dependencies, verification condition, ownership, approval boundary, execution policy, TDD mode | Start Time 21:14 UTC; Active Task Pointer set; this branch `feat/m2-persistence-seam` off `main@63d769d` |

## 6. Completion Evidence (fill as it happens)

- **Changed-file summary**: M2a.1 — `src/domain/validation.ts` (new), `src/domain/validation.test.ts` (new),
  `src/types/application.ts` (`ApplicationInput` added additively; `JobApplication.id` still `number` until
  M2a.2), plus the spec/record/STATE docs. No page, component, or data module touched yet.
- **Acceptance results**: AC-2 Met. AC-1 Partial (validation half). AC-3…AC-11 Pending.
- **Verification commands / results** at `1f6aba2`, 2026-09-10 21:22 UTC:
  - `npm run test:run` → **13 passed (13)**, 2 files, exit 0
  - `npx tsc -p tsconfig.app.json --noEmit` → exit 0
  - `npm run build` → exit 0; `vite.config.js` / `vite.config.d.ts` **absent** (M1's `noEmit` guard holds)
- **Review findings / disposition**: Pending (`pk:review` before handoff)
- **Simplification findings**: Pending
- **Checkpoints / Handoffs**: 1 soft checkpoint taken at 2026-09-10 21:22 UTC (M2a.1 close). No handoff.
- **Process deviation and correction**: the first M2a.1 ladder committed Red tests together with their
  implementation for BEHAVIOR-002 and -003 (`f31b0cc`, `b016c1e`), which breaks the record's own rule that
  each behaviour shows an isolated failing state. Cause: the next behaviour's tests were appended in the same
  shell call as the previous commit. Because the branch was **unpushed** (verified: no remote head), history
  was rebuilt rather than excused — five commits `f84a970 → 1f6aba2`, each intermediate tree re-run to confirm
  it reproduces the original counts (4f|2p, 6p, 3f|8p, 11p), and `git diff` against the pre-split tip is
  **empty**, so the rewrite changed sequencing only and lost no content.
- **CI evidence**: `N/A` — this repository has **no CI** (DEBT-03). All evidence is local; a reviewer cannot
  independently re-run a pipeline, so command output must be pasted into the PR
- **Deferred scope / exceptions**: F-5 cross-tab reconciliation deferred to M2b by decision;
  UNCERTAINTY-001-1 (quota size) accepted without asserting a figure; UNCERTAINTY-002-1 (non-secure-context
  `randomUUID`) gated to M5
- **Residual risks / post-release actions**: localStorage is plaintext and origin-scoped — **M4 must not place
  session tokens here**; the storage key is a new public contract once a user has data
- **Milestone completion decision**: **Pending human approval.** Not complete until AC-1…AC-11 are met and PR
  #2 is merged

## 7. Deviation and Readiness Pointers

- Scope or contract change → `docs/tasks/execution-scope-change-TASK-m2-persistence-seam-<n>.md`
- Required TDD deviation → `docs/tasks/tdd-exception-TASK-m2-persistence-seam-<n>.md`
- **Readiness**: `planned` may advance to `ready` only when every field above is concrete; `in_progress`
  additionally requires Start Time, recorded active ownership, and this ID as the Active Task Pointer. One
  active task per Execution Scope.
