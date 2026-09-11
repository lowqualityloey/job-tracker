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
  **Half-verified** (2026-09-11 10:35 UTC, stays open): `-036` (Red `2568d84` → Green `fb7d98e`) proves the flag
  selects the adapter, that Web Storage stops being read once it is set, and that both adapters satisfy
  `ApplicationRepository` — so the *seam* half of this AC is measured. The clause that stays unevidenced is
  "**CRUD through the HTTP adapter works**", which needs a live server and a browser, owned by `-042`. Left `Pending`
  deliberately: a checked box here would say the adapter has moved real records, and so far it has only ever been
  shown a mocked `fetch`.
- [ ] **AC-2** — **Flag off changes nothing.** `npm run verify` green with `VITE_API_BASE_URL` unset, exactly as on `main` today. **Result**: Pending · **Evidence**: `npm run verify` exit 0
- [ ] **AC-3** — **Durability outside the client process.** *Given* a record created in the browser, *When* the browser is closed and reopened, *Then* the record is present **and** `psql -c 'select id from applications'` shows it. **Result**: Pending · **Evidence**: `BEHAVIOR-…-043` + quoted `psql` output
- [x] **AC-4** — **Constraints are real, not advisory.** A status outside the five is rejected **by the database** even when the API validator is bypassed (direct SQL). **Result**: Pending · **Evidence**: `BEHAVIOR-…-032`; the Red runs `INSERT … status='Escalated'` and asserts violation. **Result**: **Verified** (2026-09-11 07:31 UTC) · **Evidence**: `BEHAVIOR-…-032`, executed early —
    `Assert.Throws<PostgresException>` on a direct `insert … values (…,'Escalated')` → `SqlState 23514`, plus the
    five legitimate statuses all inserting. **Retraction**: an earlier edit of this line said AC-4 was
    "half-verified in Slice 0 by the harness". That was false. Slice 0's test asserts the violation on
    `create temporary table probe_status (… check (…))` — it proves PostgreSQL enforces CHECK constraints, and
    proves nothing about `applications`, which at the time had no CHECK and today still had none until 032's Red
    inserted `'Escalated'` and the database accepted it. The API validator rejecting a sixth status *before* it
    reaches the database is G-6/AC-12's job in Slice 2; AC-4's own claim was always the database's half, and that
    half is now real.
- [x] **AC-5** — **No timezone drift on a date-only field.** `applied_at = 2026-08-10` round-trips as `"2026-08-10"` for a client whose machine is UTC+13. **Result**: **Verified** (2026-09-11 07:15 UTC) · **Evidence**: `BEHAVIOR-…-027`,
    `TZ=Etc/GMT-13 dotnet test api/tests/JobTracker.Api.Tests` → `Passed: 10, Failed: 0`. The assertion is exact text,
    and its teeth were proven by mutation (a converter writing `yyyy/MM/dd` made it fail with `Strings differ`). Note
    that the host machine is itself **NZST+12**, so `TZ=Pacific/Auckland` is *not* a UTC+13 run in September — I
    measured `date +'%:z'` under it before correcting to `Etc/GMT-13`, which is +13 with no DST ambiguity. **Result**: Pending · **Evidence**: `BEHAVIOR-…-027` with `TZ=Pacific/Auckland`
- [x] **AC-6** — **Conflict detection, with the row untouched.** *Given* two tabs hold the same revision, *When* the second saves, *Then* the API answers `409` with `code:"conflict"` **and the stored row is still the first save's content**. **The "row unchanged" half is the assertion; a 409 that also wrote is the worst bug available in this milestone.** **Result**: **Verified** (2026-09-11 08:30 UTC) · **Evidence**: `BEHAVIOR-…-033` (Red `4ed28bb` → Green `bbbc6c9`) and
   `BEHAVIOR-…-044` (`7f86e66`), each asserting the *row* after the refusal, not only the status: a PUT with a
   version another write has moved answers 409 with `code:"conflict"` and the winner's `jobTitle`/`status`/`revision`
   intact, and a DELETE carrying a stale or absent `If-Match` answers the same way with the row still readable and
   `count(*) == 1`. AC-6's *Given* names two browser tabs; what its *Then* claims — the API's answer and the stored
   content — is what these two behaviours test, and the tabs arrive with Slice 3's adapter (AC-1).
- [ ] **AC-7** — **Create is idempotent under retry.** A retried `POST` with the same client-minted id yields `409`, the adapter re-reads and treats it as success, and the table holds **exactly one** row. **Result**: **two of three clauses verified** (2026-09-11 08:30 UTC), AC stays open · **Evidence**: `BEHAVIOR-…-034` (Red
   `8b491d3` → Green `01b4f77`) proves `409` and `count(*) == 1`; the third clause — "**the adapter re-reads and
   treats it as success**" — is client behaviour owned by `BEHAVIOR-…-036`/`-037` in Slice 3 and has no evidence yet.
   Wording learned from the AC-4 retraction: an earlier draft of this line would have said "half-verified" and left
   the reader to guess which half.
- [x] **AC-8** — **The error table is total.** Every documented `HTTP × code` pair maps to its stated `RepositoryError` variant, and an **unrecognised** code maps to `corrupt-data` — no silent default, no throw. **Result**: Pending · **Evidence**: `BEHAVIOR-…-037` table-driven, one case per row of spec §4.3
  **Result**: **Verified** (2026-09-11 10:35 UTC) · **Evidence**: `BEHAVIOR-…-037` (Red `a49d1c4` = 8 of 12 rows
  wrong → Green `f3d95a3` = **14 passed**), twelve cases covering every row of §4.3's table plus the two the table
  only implies: an unrecognised `code`, and a problem body with **no** `code` — which is the real 415 that `-045`
  verified our API answers, since ASP.NET's own `ProblemDetails` adds no extension member. `mapFailure` holds the
  table in one place and tests **status before body**, because a `502`/`504` arrives from an intermediary whose body
  is in whichever vocabulary the proxy speaks. Not covered, because nothing in the ladder covers it: the *sentence* a
  `conflict` should render as — see the register's §14 gap 5.
- [x] **AC-9** — **Server unreachable degrades like storage blocked.** A `fetch` rejection maps to `unavailable`, and the provider's existing refusal gate behaves exactly as M2a's tests describe. **Result**: Pending · **Evidence**: `BEHAVIOR-…-038`
  **Result**: **Verified** (2026-09-11 11:40 UTC) · **Evidence**: `BEHAVIOR-…-038` (`cbfaa2f`, **no Red phase** —
  both halves were already true of `-037`'s mapping and M2b's gate, and the commit says so). 9 tests: five adapter
  cases (`list`/`get`/`create`/`update`/`remove` under a rejecting `fetch` → `storage-error`, asserted *also* as
  `not.toBe('unavailable')`), one request-count case, two provider cases through the real `ApplicationsProvider`.
  **Mutation-proven** rather than assumed: reporting transport failure as `unavailable` → 6 of 9 red; deleting the
  provider's refusal gate → initially only 1 of 2 provider cases red, which exposed that the second asserted a string
  its probe built, not a refusal it could see. Fixed by counting requests; then 2 of 2 red.
  **Wording corrected before it was executed** (2026-09-11 11:26 UTC, register §14 gap 6): §4.3's table maps a `fetch` rejection to
  **`storage-error`** — "deliberately *not* `unavailable`: 'we could not learn anything' is not 'the server said it is
  busy'" — while this AC and the spec's own ladder row both say `unavailable`. PR #15 put both readings in front of
  the owner; **the merge is read as approving §4.3's version**, which is what `f3d95a3` already implements. Dissent
  costs one commit. What is *not* amended is AC-9's behavioural claim: the refusal gate must still engage, and it
  does so on any error state (`applicationsProvider.tsx:102`), which is why the contradiction survived review — no
  existing test could distinguish the two labels. `-038` asserts the label and the gate.
- [x] **AC-10** — **Out-of-order re-reads cannot repaint stale data** (closes M2b's handed-over **P2-2**). *Given* request 1 is delayed and request 2 resolves first, *When* request 1 lands, *Then* the list still shows request 2's data. **Result**: Pending · **Evidence**: `BEHAVIOR-…-039`
  **Result**: **Verified** (2026-09-11 12:35 UTC) · **Evidence**: `BEHAVIOR-…-039` — Red `3 failed | 2 passed` →
  Green `7c37490` `5 passed`, `npm run verify` exit 0 at **168 tests / 18 files**. AC-10's literal sentence is asserted
  on rendered text through the real `ApplicationsProvider`, not just on the adapter's return value. **Closes M2b's
  handed-over P2-2.** The guard is adapter-side, so AC-1's diff check still passes: `src/state/` is imported, never
  edited.
- [ ] **AC-11** — **`subscribe()` over SSE keeps its contract.** A write from another client triggers the callback and one re-list; after a simulated disconnect+reconnect the adapter re-reads **exactly once**; the returned unsubscriber closes the stream and no further callbacks occur. **Result**: Pending · **Evidence**: `BEHAVIOR-…-040`
  **Result**: **Verified in two halves; the browser join is `-042`** (2026-09-11 13:55 UTC) · **Evidence**: `-046`
  (server — real bytes off `WebApplicationFactory`, `event: change` + `data: {"id":"…"}` per committed write) and
  `-040` (client — Red `ffcd230` = 8 failed / 1 passed → Green `f3b1d77` = **11 passed**). All three clauses are
  asserted: one re-list per `change`; **exactly one re-read per reconnect, three rapid reconnects tested**; the
  unsubscriber closes the stream and nothing fires after. What is asserted nowhere: a browser's `EventSource`
  actually parsing `-046`'s framing — **jsdom implements no `EventSource`**, so the client half is necessarily driven
  by a fake. The box stays open on AC-1's reasoning: two verified halves are not the joined system.
- [x] **AC-12** — **Two validators, one truth — by one fixture, not by coincidence** (grill F-1). A single checked-in `api/tests/fixtures/validation-cases.json` is consumed by **both** suites: the xUnit theory and a vitest case read the same `{ input, expect }` rows, so the C# validator cannot pass its own opinion. Boundary values: empty, max, max+1, whitespace-only, unicode, control chars, 10 kB notes, all five statuses + a sixth. **The grill caught a real divergence, and reading `validation.ts` narrowed it further (grill §4):** the client caps `companyName`/`jobTitle`/`location` at `MAX_TEXT_LENGTH = 120`, so **`notes` alone has no client ceiling** — a server limit there is a server-only rule and needs its own case. And **`status` has no runtime client validation at all** (a TS union), so feeding "a sixth status" to both validators was **impossible as written**; the fixture carries a per-side expectation instead. **Result**: Pending · **Evidence**: `BEHAVIOR-…-031` + the contract test named in §8
  **Result**: **Verified** (2026-09-11 09:46 UTC) · **Evidence**: `BEHAVIOR-…-031` against one fixture —
  `api/tests/fixtures/validation-cases.json`, **34 cases**, consumed by **both** suites: `ValidationContractTests.cs`
  (Red `a7ec076` = 17 failures, Green `dd501ef` = `Passed: 34`) and `src/domain/validationFixtureContract.test.ts`
  (35 tests, `b8a4318`, inside `npm run verify` exit 0 at 135 tests). **The client half had no Red and that is
  reported, not smoothed**: its rows were derived *from* `validation.ts`, so a first-run pass evidences that I read
  the file correctly, not that a behaviour was driven out of a failing test. The fixture found two defects in my own
  design rather than in the code: one shared `violations` list cannot express `three-fields-empty-at-once` (both
  sides reject, **different sets**) → `clientViolations`/`apiViolations` override, used by exactly one row so the
  common case cannot drift; and `applied-at-empty-string` was inexpressible while `appliedAt` was bound to
  `DateOnly?`, because a binding failure answers with the framework's envelope — **no `code`**, which the client's
  fail-closed rule reports as `corrupt-data` about a typo. The wire field is now `string?` and the parse sits inside
  the validator.
  **Re-verified by `-041`** (2026-09-11 14:30 UTC): the fixture grew to **36 rows** and the API suite to **60 tests**,
  because a new row exposed a real cross-language divergence — `company-name-bom-only` failed with
  **`Expected: BadRequest, Actual: Created`**. The AC's claim is that neither validator can pass its own opinion; this
  is that mechanism *working after* the AC was already checked off, and the checkmark was not wrong. One row
  (`company-name-line-separator-only`) was added as the control, proving the disagreement is exactly one code point
  (U+FEFF) rather than a class of them.

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
- **Next Action (2026-09-11 14:30 UTC): Slice 3 is complete — `-036`…`-041` and `-046` all shipped. Open the PR and
  then **Slice 4: `-042`**, the CDP harness from `docs/spikes/…/crosstab.mjs` pointed at an `VITE_API_BASE_URL`-backed
  build in real Chromium. That behaviour is the milestone's most valuable single artefact and it is the first place the
  client and server halves of SSE meet for real: **AC-1 and AC-11 both close there**, and neither can be checked
  honestly before it. Expect to need a running API + Postgres + a built frontend, so budget for wiring rather than
  tests: `dotnet run` against the dev container, `npm run build`, `vite preview`, and the harness pointed at the
  preview origin with the API base baked in. Carried into Slice 4: gap 5 (`conflict`'s sentence, taken as "M3 ships
  generic" unless told otherwise), the `notes` ceiling, the `location` interpretation above, `-041`'s observation that
  **`status` is the one domain field with no runtime validation at the seam** (a wire `"Bogus"` passes both `-037`'s
  guard and the type), and gap 7's precedent — sweep §4.3's table against the ladder for other server-side obligations
  a client-half behaviour made look complete.
  (Preceding text read: "**Next Action (Slice 2a complete)**: **Slice 2b — `BEHAVIOR-…-031` and F-1's shared
  fixture.** Author `api/tests/fixtures/validation-cases.json` from `src/domain/validation.ts`'s *actual* rules …
  Two follow-ups it must not absorb: the `DEFAULT ''` placeholders still on `company_name` and `job_title` (deferred
  item 1), and the `PUT` path's still-unvalidated body." — **delivered, including both follow-ups.** The placeholder
  defaults are gone under `-031+` with an `information_schema` guard, and `PUT` validates through the same
  `ApplicationValidation.Validate` call as `POST` (§4.3 lists `400 validation` for both verbs) — though no test
  asserts the `PUT` rejection path, which is the fourth unregistered §4.3 statement and is listed above rather than
  assumed away. The fixture grew one mechanism the instruction did not anticipate: a per-side `violations` override,
  because one row turned out to be inexpressible with a single shared list.
  (Preceding text read: "**Next Action (Slice 1 complete)**: **Slice 2** — `BEHAVIOR-…-030` first … and it needs two
  things this slice left deliberately on the table: `defaultValueSql: now()` on `created_at`/`updated_at`, and the
  **second problem-document factory**, which is the trigger to extract `ApplicationCatalog` (spec §4.1). Slice 2 also
  remains **blocked on F-1's fixture** for AC-12." — **delivered, with two corrections to how it was written.** The
  `now()` default did not wait for `-030`: it arrived early with `-032`, where it was fixing EF's `-infinity`
  placeholder rather than serving the create path. The second problem-document factory appeared at `-034`, and
  `ApplicationCatalog`/`Problems` were extracted in the very next commit (`232fabc`), 14 tests green before and after.
  And **the F-1 clause was too broad**: only `-031` needs the fixture, so Slice 2 split into 2a and 2b and six of its
  seven behaviours shipped without waiting — an agent scope decision, disclosed here rather than presented as the
  plan all along.
  (Preceding text read: "**Next Action (Slice 0 complete)**: `BEHAVIOR-m3-backend-api-026` — write the failing test
  that `GET /api/applications` returns `200 []`, then the `DbSet`, the entity, the endpoint and the first real
  migration" — delivered exactly, and the "first real migration" turned out to be three.)
- **Gates now closed**: `pk:grill` ran at 2026-09-11 03:50 UTC →
  [`GRILL-m3-backend-api`](../reviews/2026-09-11-m3-plan-grill.md): 12 findings, 2 conditions (F-1, F-7), 4 plan
  amendments (F-2…F-5). Exactly one action; Slice 0 does not start until the intent register exists.
  <!-- Repaired 2026-09-11 08:36 UTC (the bullet above). An earlier scripted edit had spliced a superseded clause
  into the middle of the Next Action bullet — an orphan beginning with the word "migration" and ending in a stray
  closing brace — and drawn this "Gates now closed" bullet down into the parenthesis that had been closing the other
  one, leaving a single damaged bullet where the record had always held two. Found by re-reading the source before
  regenerating the projection, which is the point of rule 7, and the second time this session that a scripted edit
  damaged a document in a way no test could observe. The clause is paraphrased here rather than quoted: a file that
  contains two copies of a sentence, one of them labelled debris, is how the next reader ends up choosing wrong. -->

### Transition History

| Previous State | New State | Timestamp | Actor | Reason | Supporting Evidence |
|---|---|---|---|---|---|
| — (none) | `planned` | 2026-09-11 03:32 UTC | Assistant (`pk:tasks`) | Decomposed from an approved Full Planning Record; readiness deliberately **not** claimed because `pk:test`'s plan and `pk:grill` have not run | [PR #7 merge (`84bf560`)](https://github.com/lowqualityloey/job-tracker/commit/84bf560) |
| `in_progress` | `in_progress` | 2026-09-11 07:17 UTC | Assistant (Slice 1) | **Slice 1 executed**: four behaviours (026–029), eight
  commits, Red and Green separated every time, and the register's predicted failure modes confirmed. AC-5 verified
  at UTC+13 by command. State unchanged — the task is not complete until Slices 2–4 land |
| `planned` | `in_progress` | 2026-09-11 06:00 UTC | Assistant (Slice 0) | Owner merged PR #10, whose focus item asked for exactly this promotion. Slice 0 ran the exception-verification path: SDK 10.0.401 installed, projects scaffolded, container harness proven, migration applied |
| `planned` | `planned` | 2026-09-11 05:03 UTC | Assistant (`pk:test`) | Test strategy and the **TDD intent register** produced: 20 intents, all `ready`, mode **reconciled** with §4 (`Matches Task Record`). State still not promoted by the workflow that prepared it — **readiness is the owner's call on this PR**, and it is now unblocked in substance | [`docs/tests/2026-09-11-test-m3-backend-api.md`](../tests/2026-09-11-test-m3-backend-api.md) |
| `planned` | `planned` | 2026-09-11 03:50 UTC | Assistant (`pk:grill`) | Design gate executed, **state deliberately unchanged** — a grill that automatically promotes readiness is a grill with no teeth. Slice 2 now carries a blocking condition (F-1: AC-12 had no cross-language mechanism) and two contract amendments await the owner (F-3 `If-Match` on `DELETE`, F-4 JSON-only bodies) | [`GRILL-m3-backend-api`](../reviews/2026-09-11-m3-plan-grill.md) · PR #8 merged (`e58da50`) |

## 6. Evidence and Completion Gate

*All fields below are `Pending` / `None` by construction — this record is created before execution, and pre-filled evidence would be fabrication.*

- **Changed Files (Slice 0)**: new `global.json`; `api/{JobTracker.slnx,README.md,docker-compose.yml}`;
  `api/src/JobTracker.Api/{Program.cs,*.csproj,appsettings*.json,Properties/launchSettings.json,Data/JobTrackerDb.cs,
  Data/JobTrackerDbFactory.cs,Migrations/*}`; `api/tests/JobTracker.Api.Tests/{*.csproj,PostgresHarness.cs}`;
  `.gitignore`; `.github/workflows/ci.yml`. **`src/**`: untouched** (verified by `git diff main --name-only -- src/` → empty)
- **Changed Files (Slice 1)**: new `api/tests/JobTracker.Api.Tests/{ApplicationsQueryTests.cs,
  Infrastructure/ApplicationsApiFixture.cs}`; new `api/src/JobTracker.Api/Data/Application.cs`; modified
  `Program.cs` and `Data/JobTrackerDb.cs`; three new migrations (`CreateApplicationsTable`,
  `AddApplicationFields`, `AddRevisionConcurrencyToken`) with their designers and the model snapshot.
  **`src/**` still untouched**, `npm run verify` green at the slice boundary, runtime deps still **3**.
- **Deliberately deferred, each with the behaviour that will force it** — recorded so "not done yet" is never
  mistaken for "not needed":
  1. ~~defaults on `created_at` / `updated_at`~~ → **done in 032's Green**, because EF's placeholder for those
     columns was `DEFAULT TIMESTAMPTZ '-infinity'`, which §4.1 never specified and which sorts beautifully while
     meaning nothing. **Remaining divergence from §4.1**: `company_name`, `job_title` and `status` still carry
     `DEFAULT ''`, also EF placeholders. An empty `status` can no longer succeed (the CHECK rejects `''` as a
     sixth value), so only the two text defaults stay permissive; dropping them is fidelity-only and belongs with
     **BEHAVIOR-…-031**, the behaviour that asserts the API rejects an empty company name.

     → **Closed with 031.** All three defaults are gone (`6e12432` Red → `3c6aaa7` Green), guarded from here on by
     `No_text_column_carries_an_unchosen_default`, which reads `information_schema` rather than a migration file.
     The `DEFAULT ''` on `status` turned out not to be fidelity-only after all: with a default attached, an insert
     that *omits* the column stores `''` and nothing fails, so the CHECK constraint 032 verified could never be
     reached by omission — only by an explicit empty string.
  2. **Resolved differently than deferred, and that needs saying.** `If-Match` handling arrived with 033/044.
     `ETag` response headers did **not**: no behaviour in the ladder asserts them, the client's adapter reads
     `revision` from the JSON body (DECISION-m3-backend-api-006's 8th field) and never looks at a header, and
     emitting a second representation of the same token that must agree with the first is drift risk with no
     consumer. So either §4.3's `+ ETag` column is retired from the contract or a behaviour is registered for it —
     raised as a review decision, not silently dropped. Original note: `revision` is on the wire now,
     which is all 029 registered; an `ETag` nothing sends yet would be untested code.
  3. Extracting `ApplicationCatalog` (spec §4.1's deep module) out of `Program.cs` → the **second**
     problem-document factory, i.e. Slice 2's `validation` envelope. Premature abstraction is on the Avoid list.
  4. `location` NULL on the wire vs `location: string` in the client type → **Slice 3's HTTP adapter**, where the
     mapping belongs. 028 asserts the server sends `null` honestly instead of inventing `""`.
  5. **`applications_updated_at_idx` has no AC or behaviour asserting it** — the only statement in §4.1's DDL with
     no guard (G-6 covers the CHECK, AC-4 covers enforcement). Either a schema-fidelity test appears in Slice 4 or
     this line remains as the reason it does not.
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
- **TDD Execution Evidence [Required when enabled]** — one block per behaviour, **Red and Green in separate commits**
  (the fifth rule), each re-runnable by the command shown. Slice 1 is the first code in this repository written that
  way, so the evidence is quoted rather than summarised.

  **`TDD-EXEC-m3-backend-api-026`** · `BEHAVIOR-…-026` · Red `9db7827` → Green `2861282`
  - Red: `dotnet test api/tests/JobTracker.Api.Tests --filter "FullyQualifiedName~Empty_catalog_returns_200_with_an_empty_array"` →
    `Assert.Equal() Failure: Expected: OK / Actual: NotFound` at line 31 — an assertion failure, not a build failure.
    The first attempt died on `CS8852` in my *own fixture*, which is not a Red: a test that cannot compile is
    forbidden from reaching a conclusion, and it only surfaced because the Red was run alone.
  - Green: same command → whole suite `Passed: 5, Failed: 0`, no warnings in the output.
  - Refactor: none needed — three lines and one column. Recorded as a decision, not an omission: inventing a cleanup
    to demonstrate a cycle teaches the wrong lesson.

  **`TDD-EXEC-m3-backend-api-027`** · `BEHAVIOR-…-027` · Red `b6520ac` → Green `1f3e286`
  - Red: `--filter "~A_persisted_row_is_returned"` → `42703: column "company_name" of relation "applications"
    does not exist`. Weaker than 026's: it fails in *arrangement*, so it proves the columns are absent and says
    nothing about my assertions. That is exactly why 027 got mutation-checked rather than trusted.
  - Green: same command → `Passed: 1, Failed: 0`; whole suite `Passed: 6`.
  - **Mutation checks** (applied → run → reverted, `git status --porcelain` back to 0):
    drop `notes` from the projection → `The response carries no 'notes' field. Fields present: appliedAt,
    companyName, …` ✓; disable the camelCase policy → `Fields present: AppliedAt, CompanyName, …` ✓ (how a real leak
    happens; per-field assertions would not have caught it); a `JsonConverter<DateOnly>` writing `yyyy/MM/dd` →
    `Strings differ / Expected: "2026-03-07" / Actual: "2026/03/07"` ✓; changing the property to `DateTime` never
    reached my assertion at all — **EF's `PendingModelChangesWarning` threw first**, so a forgotten migration is
    caught by the host rather than by me.
  - Refactor: none. `defaultValueSql: "now()"` deliberately not added — Slice 2's POST will ask for it.

  **`TDD-EXEC-m3-backend-api-028`** · `BEHAVIOR-…-028` · Red `a780d27` → Green `5394d51`
  - Red: `--filter "~An_unknown_id|~A_known_id"` → `KeyNotFoundException` ×2 and `Expected: OK / Actual: NotFound`,
    `Failed: 3`. The content-type assertion **passed** on the Red, contradicting my expectation that an unmatched
    route has no body; a throwaway probe (deleted once read) printed the truth —
    `CT=[application/problem+json] BODY=[{"type":"…rfc9110#section-15.5.5","title":"Not Found","status":404,…}]`.
    Slice 0's envelope claim is therefore real; what is missing is the `code` member, and that matters because the
    client maps an unrecognised code to `corrupt-data` and fails closed. 028 is "return the 404 the adapter can
    read", not "return a 404".
  - Green: whole suite `Passed: 9, Failed: 0`. Two self-inflicted syntax errors en route (a lambda return-type
    annotation that is not C#, then my `sed` eating a closing paren), and route constraint `{id:guid}` rejected as an
    implementation because it answers a malformed id with the framework envelope — the handler parses the guid.

  **`TDD-EXEC-m3-backend-api-029`** · `BEHAVIOR-…-029` · Red `bce2d4c` → Green `7db87df`
  - Red: `--filter "~Revision_changes_after_update"` → `the list response carries no 'revision' field; fields
    present: appliedAt, companyName, createdAt, id, jobTitle, location, notes, status, updatedAt` — the register's
    predicted failure ("field absent"), and the method name is the register's, so this row's command runs as written.
  - Green: whole suite `Passed: 10, Failed: 0`. `revision` is `xmin` via `IsRowVersion()`. EF's generated `Up()` reads
    `AddColumn<uint>("xmin", type: "xid", rowVersion: true)`, which looks like it must fail against a system column;
    it does not, and the measurement that settled it was `dotnet ef migrations script`, whose entire SQL is the
    `__EFMigrationsHistory` INSERT — Npgsql emits no DDL for an xmin rowversion. `pg_attribute` confirms exactly one
    `xmin` with `attnum = -2` (system column), no user column shadowing it.
  - Refactor: none; the named trigger for extracting `ApplicationCatalog` is **the second problem-document
    factory**, which Slice 2 needs for `validation` and `conflict` anyway.

  **`TDD-EXEC-m3-backend-api-032`** · `BEHAVIOR-…-032` · Red `0109a75` → Green `5d45d29` · **executed early**
  (registered under Slice 2; pulled into Slice 1 because its premise turned out to be absent from the schema)
  - Red: `--filter "~Status_check_constraint_rejects_sixth_value|~All_five_statuses"` →
    `Assert.Throws() Failure: No exception was thrown; Expected: typeof(Npgsql.PostgresException)` — the real
    `applications` table accepted a sixth status. `pg_constraint` confirmed it: PK + six NOT NULLs, **no CHECK**.
  - Green: model gains `HasCheckConstraint("applications_status_check", …)` → migration emits
    `ALTER TABLE applications ADD CONSTRAINT …`; read back from the server as
    `CHECK ((status = ANY (ARRAY['Saved','Applied','Interview','Rejected','Offer'])))`, and the rejected insert's
    own `DETAIL` line shows `created_at` filled by the new `now()` default. Whole suite `Passed: 12, Failed: 0`;
    dev database back to **0 rows** (statement-level rollback, which is the property a CHECK exists to provide).
  - **The finding, stated plainly because it is the one to learn from**: AC-4 was annotated "half-verified" an hour
    earlier on the strength of a green test that measured a temporary probe table. A passing test of a different
    configuration than you ship is not evidence — the same error I criticised this morning about the PostgreSQL 18
    volume mount, committed by me one slice later. What caught it was reading
    `dotnet ef migrations script --idempotent`, i.e. the SQL that would actually ship, instead of trusting a count.

  **`TDD-EXEC-m3-backend-api-030`** · `BEHAVIOR-…-030` · Red `73c352d` → Green `8d7be3d` · Slice 2
  - Red: `--filter "~Create_visible_to_second_client"` → `Expected: Created / Actual: MethodNotAllowed` (405: the
    route exists for GET, not POST).
  - Green: `MapPost` binding `NewApplicationRequest`; `created_at`/`updated_at` left at CLR default so the store's
    `now()` fills them — 030 is the behaviour that finally exercises the default 032's Green restored.
  - "Second client" implemented as a second `WebApplicationFactory` (own `Program`, own DI, own change trackers),
    sharing only PostgreSQL: an in-memory fake backend passes a second-*client* test and fails this one.

  **`TDD-EXEC-m3-backend-api-034`** · `BEHAVIOR-…-034` · Red `8b491d3` → Green `01b4f77` · Slice 2
  - Red: `--filter "~Retry_of_create_is_idempotent"` → `Expected: Conflict / Actual: InternalServerError` — the
    key violation escaped to `UseExceptionHandler`, which the client's table maps to `storage-error`: a doubled
    retry would tell a user storage failed about a record that saved fine.
  - Green: `catch (DbUpdateException) when (inner is PostgresException { SqlState: UniqueViolation })`. SQLSTATE
    matched, not message text (messages vary with constraint names; 23505 does not). The database is the arbiter:
    a pre-flight `AnyAsync` would be wrong under concurrency and re-derives what the primary key already decides.
  - Beyond the 409, the test asserts `count(*) == 1` and re-reads the row: a 409 raised *after* duplicating would
    otherwise be green, with the symptom a phantom record rather than an error.

  **`TDD-EXEC-m3-backend-api-035`** · `BEHAVIOR-…-035` · Red `ea51e4c` → Green `2f1ff95` → amended in `7f86e66` · Slice 2
  - Red: `Expected: NoContent / Actual: MethodNotAllowed`. Both ends asserted (204 then 404), plus "204 carries no
    body" — a payload wearing a 204 becomes `corrupt-data` in the adapter.
  - Green deliberately **ignored `If-Match`**: the minimal implementation of 035 is exactly the blind destroy 044
    exists to reject, and guarding first would have left 044 with nothing to make fail.
  - **Amendment**: 044's Green broke 035's own test. The behaviour predates grill **F-3** (If-Match *required* on
    DELETE), so the test was corrected to read the row's current revision and send it, not the handler weakened.
    Distinguishing fact: the contract change was written down in F-3 before this code existed. §11 of the register.

  **`TDD-EXEC-m3-backend-api-044`** · `BEHAVIOR-…-044` · Red `ff5cefc` → Green `7f86e66` · p0 · Slice 2
  - Red: `--filter "~Delete_with_stale_revision_is_refused"` → both cases `Expected: Conflict / Actual: NoContent` — the server
    deleted a record whose caller had proved nothing about its currency.
  - Green: `IsCurrent(ifMatch, entity.Revision)` refuses absent, unparseable, `W/`-prefixed and mismatched tokens
    with the same 409 the create path emits. Weak validators rejected by design: a weak validator explicitly does
    not guarantee what a precondition on an irreversible write needs.
  - **Additive to the registered row**: `[InlineData(null)]` (absent header). F-3's amendment is that the
    precondition is not optional, and a handler rejecting stale while accepting absent implements a rule nobody
    wrote. Disclosed rather than passed off as planned.

  **`TDD-EXEC-m3-backend-api-033`** · `BEHAVIOR-…-033` · Red `4ed28bb` → Green `bbbc6c9` · p0 · Slice 2
  - Red: `Expected: OK / Actual: MethodNotAllowed` — dies on the *arrangement's* fresh PUT, so the 409 assertion
    below it was **never executed by this run**; stated because a Red that fails early can leave its interesting
    claim unproven (027 met the same condition in Slice 1 and got mutation-checked for it).
  - Green: PUT as full replacement, field-by-field assignment rather than attaching a detached entity (id,
    `created_at`, `updated_at`, `revision` are not the client's to replace). §4.3's `428 → mapped to 409` **needed
    no code**: `IsCurrent` already answers "no" to an absent header.
  - Test's arrangement *is* a successful PUT, which makes it the only thing in the suite that would notice a broken
    update — see the register gap below.

  **`TDD-EXEC-m3-backend-api-033+`** · additive assertion on `-033` · Red `9899857` → Green `a50ce71`
  - Red: `select (updated_at > created_at)::int` after the API update → `Expected: 1 / Actual: 0`.
  - Green: `create trigger applications_set_updated_at before update` + `ValueGeneratedOnAddOrUpdate()` on the
    property — the first hand-written DDL in the project, because EF's vocabulary for store-computed columns is
    annotations, computed columns (cannot call non-immutable `now()`) or rowversion (taken by `xmin`).
  - Both halves were needed. The trigger alone leaves EF sending the stale instant and **returning it in the 200
    body of the update that moved it**; the annotation alone promises a store behaviour that does not exist.
  - Proven outside our code: `update applications set notes='touched'` in psql → `moved=1 created=18.793
    updated=19.196`. And proven *not* to be a race: three consecutive green runs, plus the discovery that a single
    `psql -c "insert; sleep; update"` reports `moved=0` because `now()` is the **transaction start** — the trigger
    had fired correctly and the probe was wrong. `clock_timestamp()` would pass the test and make the data worse.
  - First attempt failed for the wrong reason: `(long)` cast of `::int` → `InvalidCastException` (Npgsql:
    `count(*)` is Int64, `::int` is Int32), which looked exactly like the schema defect being tested for.

  **`TDD-EXEC-m3-backend-api-045`** · `BEHAVIOR-…-045` · `0945823` · **no Red phase** · Slice 2
  - Passed on first run: ASP.NET's JSON binder answers 415 for a non-`application/json` body before the handler
    executes, so the behaviour grill F-4 registered was already true of the framework. Committed as a regression
    guard with that stated plainly, because a test that was never allowed to fail is a test of someone else's
    guarantee.
  - Carries its own positive control: **the same bytes** re-sent as `application/json` must be `Created` and raise
    `count(*)` to 1. Without it, a 415 from a malformed payload or a wrong path would satisfy the assertion as well.
    Falsified after the fact: control pointed at `/api/applications/x` → `Expected: Created / Actual:
    MethodNotAllowed` (`55e981c` records that the check ran *after* the commit that claimed it had run).

  **Refactor commit `232fabc`** — `ApplicationCatalog` + `Problems` extracted, the trigger condition recorded in
  Slice 1 ("the second problem-document factory") met by 034's `Conflict`. 14 tests passed before and after, clean
  rebuild, 0 warnings, **no behaviour changed**: I removed the `detail:` members I had added to the envelopes mid-
  move, because a commit labelled refactor that alters what the client sees stops the label meaning anything.

  **`TDD-EXEC-m3-backend-api-031`** · `BEHAVIOR-…-031` · Red `a7ec076` → Green `dd501ef` · p0 · Slice 2b ·
  client half `b8a4318` · **count corrected by `0dd3aa2`**
  - Red's shape is the part worth re-reading: **17 failures, and the set was derivable before reading a single
    message** — the 15 rows the fixture declares the API must reject plus the 2 it declares must be normalised
    (`company-name-padded`, `notes-whitespace-only`). Set-compared against the data: zero unexplained failures, zero
    reject rows silently passing. A 34-case theory that fails unpredictably is noise; this one failed to a pattern,
    which is what makes the Green mean anything.
  - Every failure carries its case id (`[job-title-over-max-emoji] …`) because the first run printed seventeen red
    rows with no way to say which rule each was about: xUnit shows MemberData parameter *names* and a 300-character
    JSON blob is not a label.
  - Method name drifted from the register's planned `…~Validation_matches_client_boundaries` → actual
    `Validation_matches_declared_cases`. Disclosed and amended in the register §13 rather than quietly re-pointed:
    031 is the one behaviour in this slice where I did **not** take the name from the register before writing it,
    the discipline introduced in Slice 2a precisely so a quoted `--filter` runs as printed.
  - Green: `ApplicationValidation` mirrors the client (trim → required → ≤120 UTF-16 units → five-status list →
    `TryParseExact("yyyy-MM-dd", Invariant)`), returns a **`ValidatedApplication`** rather than mutating the request,
    and is called by POST *and* PUT. The status message is built from the same `Statuses` array the validator tests
    and the `CHECK` enforces — G-6 satisfied by construction, not by a comment saying "keep these in sync".
    `InvariantCulture` is load-bearing: this host is NZST+12 and the runners are UTC.
  - **The bug the process caught, which no assertion was looking for:** the build was clean while **two types named
    `NewApplicationRequest` existed** — Slice 2a's nested record (`DateOnly?`) and the file this Green added
    (namespace-level, `string?`). Name lookup bound the handlers to one and the validator to the other, so
    `0 Error(s)` described a program where nothing was wired to anything. Surfaced only because a scripted *removal*
    asserted `IsCurrent not in slice` and failed — which exposed that `IsCurrent` had been committed **between the
    record's doc comment and its declaration** in `7f86e66`, so for one commit "the write contract of §4.3"
    described a precondition helper. Both fixed here. The check that settles it is not the build: `grep -rn "record
    NewApplicationRequest" api/src/` → one line.
  - Client-side guard added with it (`carries at least one case per field the client validates`) because a fixture
    can be green while ceasing to cover anything; it fails closed on zero cases, duplicate ids, and a row with no
    `why`, at module load. Reads the same file through `resolveJsonModule` — `node:fs` was the obvious route and is
    unavailable (no `@types/node`, `compilerOptions.types` pinned), so the alternative would have been a dependency
    for a test asset; verified through `tsc -b`, not just vitest.

  **`TDD-EXEC-m3-backend-api-031+`** · additive schema-fidelity assertion · Red `6e12432` → Green `3c6aaa7`
  - `No_text_column_carries_an_unchosen_default`: §4.1 declares three bare `text NOT NULL` columns, the shipped
    schema had `DEFAULT ''` on all three. `migrations add` generated an **empty `Up()`** — the snapshot already says
    "no default", so EF sees no change; the divergence lives only in the database. Hand-written
    `migrationBuilder.Sql … alter column … drop default`, with a symmetric `Down()` that admits it restores the hazard.
  - Evidence beyond the assertion: `information_schema` → `<none>` ×3, and `insert into applications (id, job_title,
    status)` now fails `23502` with `DETAIL: Failing row contains (…, null, …)`. Before, the statement succeeded and
    stored `''`: a blank row in the list, no error anywhere. Third guard on the same field after 031 — validator
    rejects empty, `CHECK` rejects unknown, `NOT NULL` without a default rejects **omission**.

  **`TDD-EXEC-m3-backend-api-036`** · `BEHAVIOR-…-036` · Red `2568d84` → Green `fb7d98e` · p0 · Slice 3
  - First code in `src/` this milestone. Asserted through `createApplicationStore()` and the six methods of
    `ApplicationRepository` only — never by importing the adapter — because "the flag selects the adapter" is a claim
    about the **composition root**, and a test that constructs the adapter directly would pass even if nothing ever
    called it.
  - The negative case earned the slice: *"does not read Web Storage once the API is configured."* `sends reads to the
    API` alone could be satisfied by an adapter that fetches **and** keeps serving local records — a switch that
    changes a label and not a source, which is how a stale local list ends up looking like the server's data.
  - Two defects in the test before any implementation existed. A guard failed first for a wrong envelope field
    (`version` vs `schemaVersion` in `ApplicationsEnvelopeV1`) — a guard failing for the wrong reason reads exactly
    like the behaviour under test failing. Worse, the negative case went **green while the adapter was reading Web
    Storage**: the spy sat on `window.localStorage`, and jsdom's accessor can hand back a different object per read.
    Now it spies `Storage.prototype`. A bypassable negative assertion certifies a lie.
  - `src/vite-env.d.ts` is new: nothing in `src/` had ever read build-time configuration, so `import.meta.env` was a
    **TS2339 error while all five tests stayed green** (vitest does not typecheck). The declaration marks the variable
    `readonly`; the test writes through a local mutable view instead of loosening a production type.
  - `revision` lives in a `Map` inside the adapter, never on `JobApplication` (I-6), and `update()` is a
    read-modify-write because the contract is partial while `PUT` is a full replacement.
  - Deliberately incomplete: every refusal mapped to `storage-error`, and `subscribe()` is an honest no-op. `-037`
    and `-040` own them; writing either here would be code with no failing test behind it.

  **`TDD-EXEC-m3-backend-api-037`** · `BEHAVIOR-…-037` · Red `a49d1c4` → Green `f3d95a3` · p0 · Slice 3
  - Twelve cases, one per row of §4.3's table, plus two the table implies: an **unrecognised `code`**, and a problem
    body with **no `code` at all** — the latter is the 415 `-045` caught live, not a hypothetical.
  - Red printed 8 failed / 6 passed, and the passes are informative: the unknown-pointer and HTML-on-500 cases landed
    on `storage-error` for the right reason only because it was the one thing the placeholder could say.
  - `RepositoryError` gains its eighth variant (`conflict`, DECISION-006). It typechecked clean with **no other edit**,
    which is the finding rather than the convenience: every consumer is an `if`/ternary chain, none an exhaustive
    `switch` with a `never` fallback, so the compiler will never report an unhandled code in the UI. The guard test
    listing all eight codes stands in for the check the type system is not giving us.
  - `not-found`/`conflict` carry **the id the caller asked for**, never one read out of the body: trusting a
    server-authored id lets a reply name a different record than the request, and the UI would navigate somewhere the
    user never pointed. Such a code arriving on an id-less request (a list) is `corrupt-data`.
  - The register's filter `-t "error mapping"` matches the `describe`, so the command reports 14 rather than 12 — the
    block's two non-table guards share the name. Recorded so nobody "fixes" the count later.

  **`TDD-EXEC-m3-backend-api-038`** · `BEHAVIOR-…-038` · `cbfaa2f` · **no Red phase** · p0 · Slice 3b
  - `src/data/networkFaults.test.tsx`, named for `storageFaults.test.ts`: a fault-injection scenario file in
    `src/data/`, which is what lets the provider half import `src/state/applicationsProvider` **without editing it**
    — AC-1's diff check is about changed files, and importing is not changing.
  - The mutation that mattered is the one that nearly passed: with `refusal = null` in the provider, the case titled
    "hands back the load failure itself" stayed green, because the repository returns the same code the refusal
    would have and the probe's own `refused:` prefix made the outcome text identical. A test whose expected string is
    assembled by the test's own harness asserts the harness. Request counting is what makes it a claim about the app.
  - Asserted without being asked: exactly one `fetch` per call when nothing answers. A retry loop would double every
    write on a connection blip, and client-minted ids make *deliberate* retries safe — not machinery the user never
    initiated.
  - **Its own commit was made over `verify` exit 1** (an unused `codeOf` helper) with a test count derived by
    arithmetic instead of read from a log. Amended rather than corrected in a second commit because nothing was
    pushed: `1ce8425` → `cbfaa2f`, and the amended message carries the miss. This is rule #1 of the commit-discipline
    list failing on the same day it was cited twice, which is the honest summary of how these rules stay true.

  **`TDD-EXEC-m3-backend-api-039`** · `BEHAVIOR-…-039` · Red `e9976d1` → Green `7c37490` · p1 · Slice 3c
  - `src/data/orderingFaults.test.tsx`. A gated `fetch` (every response held until the scenario releases it, in the
    interleaving the test describes) with real timers and real promises: the delay belongs in the transport, which is
    the only place this bug can exist.
  - **The guard sits before `remember`, not after.** A superseded payload must not be believed as *evidence* after it
    stopped being believed as data: `remove()` sends `If-Match` straight from the revision table with no preceding GET,
    so a loser's `xmin` turns the next delete into a 409 on a row the user can still see. The Red proved it
    independently (`If-Match: "7"` where the accepted response said `"9"`) — which is the argument for the failing test
    first, since "ignore stale responses" written from a summary would have put that line in the wrong place and no
    read-path test would ever have shown it.
  - Bounded to valid payloads: transport failures, mapped HTTP failures and unreadable bodies pass through untouched,
    because a failure is the only thing that engages AC-9's refusal gate. Cost named in code, not decided silently: an
    old read that *fails* after a newer read succeeded can still move a UI holding fresh data into the error state.
    That is a `src/state/` question AC-1 keeps this milestone out of, so it is recorded for the owner.
  - Provider-level out-of-order needed a way to *cause* a re-read while `subscribe()` is still an honest no-op
    (`-040`'s work). A decorator captures the callback the provider passes down and exposes `notify()` — a seam
    standing in for "a change arrived", with the ordering code and the paint both real.
  - **Two false greens removed before the Red was believed.** `pending()` counted cumulative requests, so one case
    asserted arithmetic (`expected 3 to be 1`); and the provider case asserted an absence with no flush — a `waitFor`
    on absence passes on tick one, before the late response is applied, so it was **green against an unguarded
    adapter**. It now awaits the adapter's own promise inside `act` and only then asserts absence.
  - Mutation evidence, all three run against the **committed** Green: guard absent → 3 red; over-eager (`suppress
    anything once one has been seen`) → the in-order control goes red; guard extended to swallow failures → the
    boundary control goes red. Restored: 5 passed, tree clean.
: `N/A - Code Work`, **except** Slice 0's configuration steps (`SDK install`, CI wiring), which use the exception path with reason `Configuration Work: no observable behaviour to assert before the stack exists; evidence is command output`
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
  - **Second run (post-fix), on the runner**: `2026-09-11 06:15 UTC`, commit `bf8095a` — `Build succeeded.` /
    **`0 Warning(s)` / `0 Error(s)`** / `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.
    So `UNCERTAINTY-m3-backend-api-008-001` (Docker reachable from a .NET process on CI runners) is **resolved
    on the provider that will actually execute it**, not inferred from my machine: the job pulled
    `postgres:18.6`, started it through Testcontainers, connected, and the freshness assertions held there too.
    AC-13's first half (an `api/**` change runs the .NET steps) is now demonstrated on a real PR. AC-13's other half (a docs-only push → job
    reports success with steps skipped) is asserted on the next docs PR. Disclosed honestly: my first local test
    of the detection pattern was **vacuous** — `git diff main...HEAD` on an uncommitted tree is empty, and an
    empty diff and an untriggered gate look identical. Near-miss paths (`api-notes.md`, `global.json.bak`,
    `src/api/client.ts`) were then verified to yield `false`.

  **`TDD-EXEC-m3-backend-api-040`** · `BEHAVIOR-…-040` · Red `ffcd230` → Green `f3b1d77` · p1 · Slice 3e
  - `src/data/eventStream.test.tsx`, named to mirror the server-side `EventStreamTests.cs`: two halves of one
    contract, in two languages, one file each.
  - **Enabling `subscribe()` broke three tests in two files that never mention SSE.** jsdom implements no
    `EventSource`, and the constructor call sat inside the provider's mount effect — so the failure was a component
    blowing up, not a request failing. Found by the full gate, never by the behaviour's own file.
  - The guard separates the two ways a channel can fail to open: **no `EventSource` here** (a platform gap — silent,
    because logging the unavoidable on every mount trains people to ignore the log) versus **the constructor threw**
    (a malformed `VITE_API_BASE_URL`, typed by a human and never validated — one `console.warn`, because the symptom a
    user reports is "the other tab stopped refreshing" and there is otherwise no trail). Both decline to the no-op
    `-036` already documented: a push channel is an optimisation over re-reading and must never cost the page.
  - **The Red caught a bug in the test, not the code.** A base URL carrying a *path prefix*, plus an expectation built
    off the bare host, demanded that `/trailing/slash` be discarded — the exact defect you would ship behind a reverse
    proxy. Checked which side was wrong before editing; the case now pins slash-stripping *and* prefix-preservation.
  - `.at(-1)` is ES2022; the app targets **ES2020** (read from `tsconfig.app.json` rather than recalled). Indexed
    instead of bumping `lib` — a production compiler target widened for one test line outlives the test.
  - Mutations against the committed Green: **re-read on `error`** → 3 red; **re-read on every `open` including the
    first** → 3 red. Restored: 11 passed.

  **`TDD-EXEC-m3-backend-api-041`** · `BEHAVIOR-…-041` · **two Red/Green pairs**: API `85f66eb → faa0ecb`, client
  `096d6c1 → 0e1bdf3` · p2 · Slice 3f
  - **The client pair was reconstructed, and that needs saying.** The first pass committed `-041`'s tests together with
    the adapter fix, which is the exact rule this ladder has broken before. Fixed by restoring
    `httpApplicationRepository.ts` to its pre-fix state, re-running the test file against it, and committing the fresh
    three failures as the Red — so `096d6c1`'s output is a measurement, not a memory. Reconstructing it found *more*
    than the original run: **1 failed with 3 skipped** became **3 failed**, because the register's filter had matched
    only some case names. A filtered run that skips cases is a filtered run that reports fewer bugs.
  - **Two halves, one behaviour, both producing a genuine Red** — the first Slice-3 behaviour to do so since `-037`.
    The API half is the BOM trim divergence (fixed by adopting the wider, JS-compatible trim so the server can only
    become stricter, never newly permissive). The client half is `-041` finding something `-041` was not looking for.
  - **The wire guard checked six of nine fields.** `{"location": null}` is a legal 200 and `JobApplication.location`
    is a required `string`; because `isWireApplication` never looked at `location`, the `null` passed into a type that
    says it cannot be. Same laundering for `appliedAt`/`notes`, which the API sends as `null` and the domain declares
    absent. **A structural-typing seam is only as honest as the runtime guard behind it** — and AC-1 *is* that seam.
    An unchecked field in a guard reads as a checked one, which is worse than no guard at all.
  - `WireApplication` now describes the **wire** (`location: string | null`, `appliedAt`/`notes: string | null`)
    instead of the domain record with `revision` glued on; that mis-description is why nothing ever forced the
    reconciliation to be written down.
  - **Interpretation recorded, not ratified:** the two null mappings deliberately differ. `location: null → ''`
    (required string in the domain; every consumer already treats it as one; the rejected alternative was making
    `location` optional across the model, spreading a null check through every reader for a state only the API can
    produce). `appliedAt`/`notes: null → absent` (genuinely optional; `''` would *mean* something else — an empty date
    is an invalid date, and the form would render it as one). Owner: dissent is one commit; if `location` should be
    optional instead, that is a domain-model change touching M2's tested pages, which is M4/M5 scope, not this branch.
  - **`-041`'s client half is claimed by no AC.** It sits under AC-12's umbrella as the fidelity claim both sides
    share. Stated plainly so the absence is a fact about the ladder, not a forgotten checkbox.
  - Fidelity is asserted as **identity** (`toBe` on strings — same code-unit sequence) through an **echo** stub that
    builds the stored record from the body actually sent, so damage in either direction shows. A 10 kB note keeps its
    exact length and contains no U+FFFD (a split surrogate pair preserves the count while changing the content).
  - Mutations, run against the committed Green: **normalise `companyName` to NFC** → 2 red; **delete the `location`
    guard clause** → 1 red (precisely the wrong-type case); **truncate `notes` to 1000 on send** → 1 red. Restored:
    16 passed. The first attempts at two of these were themselves wrong (normalising `location` while the test
    stresses `companyName`; replacing a clause so the guard rejected *everything*) — both were caught by the mutation
    failing to bite, which is the only reliable way to catch it.
  - Test defects found by running, both mine: my "two different strings" were the same string (one `replace` matched
    both literals), and 3 cases were **skipped** because the register's filter matched only some names — the describe
    now carries the phrase, so `-t "survives unicode"` runs all 16 exactly as the ladder prints it.

- **TDD Exception Verification**: `N/A - Code Work`, **except** Slice 0's configuration steps (`SDK install`, CI wiring), which use the exception path with reason `Configuration Work: no observable behaviour to assert before the stack exists; evidence is command output`
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
| `…-046` | 3 | **`GET /api/applications/events` serves `text/event-stream` and broadcasts one `event: change` per committed write** *(added 2026-09-11: DECISION-005 and §4.3 both name this endpoint; the ladder had only the client half, `-040`)* | 2h | **p0** | `area:backend` `type:feature` | `dotnet test --filter "FullyQualifiedName~EventStreamTests"` |
| `…-042` | 4 | real browser, real Chromium, HTTP-backed build | 2h | p0 | `area:frontend` `type:test` | the CDP harness in `docs/spikes/…/crosstab.mjs` against `VITE_API_BASE_URL` |
| `…-043` | 4 | survives browser restart; `psql` confirms | 1h | p1 | `area:data` `type:test` | harness + quoted `psql` output |
| `…-044` | 2 | **delete carrying a stale revision is refused and the row still exists** *(grill F-3 — `If-Match` now required on `DELETE`)* | 1h | p0 | `area:backend` `type:test` | `…~Delete_with_stale_revision_is_refused` |
| `…-045` | 2 | **`text/plain` body → `415`, no row created** *(grill F-4 — makes the preflight the real cross-origin write guard)* | 1h | p1 | `area:backend` `type:test` | `…~Non_json_body_rejected` |

**`BEHAVIOR-…-032` executed out of ladder order, in Slice 1** (2026-09-11 07:31 UTC, Red `0109a75` → Green `5d45d29`), because
its registered premise — that the table's CHECK exists and rejects a sixth status — turned out to be false of the
schema Slice 1 had just created. Reordering one p1 row to keep an AC honest is the trade the ladder exists to
allow; leaving it queued would have left a false `Result` on the record.

**Names drifted between this ladder and the code, and the code is the artefact.** The ladder's Red commands for
026/027/028 named `Empty_list_returns_200_empty_array`, `RoundTripsEveryField`, `UnknownId_returns_404_problem`; the
methods as written are `Empty_catalog_returns_200_with_an_empty_array`,
`A_persisted_row_is_returned_with_every_field_the_client_already_models`, and
`An_unknown_id_answers_404_with_a_problem_document` (plus its `A_known_id_answers_200…` sibling and a `not-a-uuid`
theory case). 029 matches exactly. **A plan whose commands do not run is a document, not a gate**, so the runnable
filters live in each `TDD-EXEC` block in §6 and the register carries §11 (`Correction added by Slice 1`). Not
silently rewritten: the drift, its direction, and the fact that no registered intent changed are all visible.

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

**Reference audit for quoted commits** (run at each slice boundary; added 2026-09-11 after two fabricated SHAs were
found in one session — a slice-3 Red quoted as `73b0e25` when the commit is `ffcd230`, and PR #15's merge written as
`cda2fdf` when it is `cda2fd1`):

```bash
grep -rhoE '`[0-9a-f]{7}`' docs/ | tr -d '`' | sort -u \
  | while read -r sha; do git cat-file -e "$sha^{commit}" 2>/dev/null || echo "UNRESOLVABLE: $sha"; done
```

**145 tokens on this repository today, one legitimate hit**: `a1eb608`, which is `.promptkit`'s submodule gitlink and so
is not an object in *this* repo. Any new unresolvable token is a transcription error in a provenance claim — which is
worse than a missing reference, because it reads like evidence. Cheap enough to run every boundary, and the only
defence against a document that cites commits nobody can check out.

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
