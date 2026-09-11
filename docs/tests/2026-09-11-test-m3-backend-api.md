# Test Plan Specification: M3 — Backend API (HTTP adapter over PostgreSQL)

- **Author**: Assistant (`pk:test`), for owner review
- **Status**: `In Review` — readiness gate for `TASK-m3-backend-api`
- **Created**: 2026-09-11
- **Test Frameworks**: **Vitest 2** (jsdom, existing) · **xUnit + Testcontainers.PostgreSql** (new, API side) · the existing **raw-CDP harness** for the one browser journey (no framework added — see §3)

---

## 1. TDD Intent Register (Supporting Test-Plan View)

- **Task Record Link**: [`TASK-m3-backend-api`](../tasks/TASK-m3-backend-api.md#TASK-m3-backend-api)
- **Work Type Reference**: `Code Work` (behaviours `026`–`045`); Slice 0 is `Configuration Work` → exception path in §1.3
- **TDD Enforcement Mode Reference**: `enabled`
- **Mode Authority**: `Canonical Local Task Record`
- **Mode Reconciliation**: **Matches Task Record** — the Task Record records `enabled` and this plan proposes nothing different, so readiness is not blocked by a mode disagreement.

### 1.1 Enabled Code Work Intent

One row per behaviour, written **before** implementation. Every Red command is exact and runnable, and each names
the file that behaviour creates. `Green`/`Refactor` re-run the same command — the acceptance condition never changes
mid-cycle.

| Intent ID | Behavior ID | Test / Expected Failing Assertion | Runnable Red Command | Status |
| :--- | :--- | :--- | :--- | :--- |
| `TDD-INTENT-m3-backend-api-026` | `BEHAVIOR-m3-backend-api-026` | `GET /applications` on an empty table returns `200` + `[]`; fails: route not mapped (404) | `dotnet test api/tests/JobTracker.Api.Tests --filter "FullyQualifiedName~Empty_list_returns_200_empty_array"` | `ready` |
| `TDD-INTENT-m3-backend-api-027` | `BEHAVIOR-m3-backend-api-027` | Every field round-trips; `appliedAt="2026-08-10"` returns the same string under `TZ=Pacific/Auckland`; fails: no endpoint | `TZ=Pacific/Auckland dotnet test api/tests/JobTracker.Api.Tests --filter "FullyQualifiedName~RoundTripsEveryField"` | `ready` |
| `TDD-INTENT-m3-backend-api-028` | `BEHAVIOR-m3-backend-api-028` | Unknown uuid → `404` + `application/problem+json` with `code:"not-found"`; fails: no handler | `dotnet test ... --filter "~UnknownId_returns_404_problem"` | `ready` |
| `TDD-INTENT-m3-backend-api-029` | `BEHAVIOR-m3-backend-api-029` | List response carries `revision`; it **changes** after an update and is stable across reads; fails: field absent | `dotnet test ... --filter "~Revision_changes_after_update"` | `ready` |
| `TDD-INTENT-m3-backend-api-030` | `BEHAVIOR-m3-backend-api-030` | `POST` persists; a second client's `GET` sees it; fails: no write path | `dotnet test ... --filter "~Create_visible_to_second_client"` | `ready` |
| `TDD-INTENT-m3-backend-api-031` | `BEHAVIOR-m3-backend-api-031` | **Fixture-driven**: every row of `validation-cases.json` gets its declared verdict from the API validator; fails: fixture has no consumer | `dotnet test ... --filter "~Validation_matches_declared_cases"` **and** `npx vitest run src/domain/validation.fixtureCases.test.ts` | `ready` |
| `TDD-INTENT-m3-backend-api-032` | `BEHAVIOR-m3-backend-api-032` | Direct SQL `INSERT ... status='Escalated'` is rejected by the `CHECK` constraint (23514); fails: constraint absent | `dotnet test ... --filter "~Status_check_constraint_rejects_sixth_value"` | `ready` |
| `TDD-INTENT-m3-backend-api-033` | `BEHAVIOR-m3-backend-api-033` | Stale `If-Match` → `409` + `code:"conflict"` **and a re-read proves the row is unchanged**; fails: last write wins | `dotnet test ... --filter "~Stale_if_match_conflicts_without_writing"` | `ready` |
| `TDD-INTENT-m3-backend-api-034` | `BEHAVIOR-m3-backend-api-034` | Retried `POST` with the same client id → `409`; adapter treats it as success; `count(*) == 1`; fails: duplicate row | `dotnet test ... --filter "~Retry_of_create_is_idempotent"` | `ready` |
| `TDD-INTENT-m3-backend-api-035` | `BEHAVIOR-m3-backend-api-035` | `DELETE` → `204`, then `GET` → `404`; fails: no delete route | `dotnet test ... --filter "~Delete_then_missing"` | `ready` |
| `TDD-INTENT-m3-backend-api-036` | `BEHAVIOR-m3-backend-api-036` | With the env var set the store returns the HTTP adapter, unset returns local; both satisfy `ApplicationRepository` (structural check compiles); fails: no selector | `npx vitest run src/data/applicationStore.test.ts` | `ready` |
| `TDD-INTENT-m3-backend-api-037` | `BEHAVIOR-m3-backend-api-037` | Table-driven: each `status × problem code` row maps to its declared `RepositoryError`; **unknown `code` → `corrupt-data`**; fails: mapping absent | `npx vitest run src/data/httpApplicationRepository.test.ts -t "error mapping"` | `ready` |
| `TDD-INTENT-m3-backend-api-038` | `BEHAVIOR-m3-backend-api-038` | `fetch` rejects → `unavailable`; the provider's write-refusal gate then blocks further writes as M2a specifies; fails: unhandled rejection | `npx vitest run src/data/httpApplicationRepository.test.ts -t "server unreachable"` | `ready` |
| `TDD-INTENT-m3-backend-api-039` | `BEHAVIOR-m3-backend-api-039` | Response for request 1 resolves **after** request 2 → the list still shows request 2's data; fails: stale overwrite (closes P2-2) | `npx vitest run src/data/httpApplicationRepository.test.ts -t "ignores an older response that lands late"` | `ready` |
| `TDD-INTENT-m3-backend-api-040` | `BEHAVIOR-m3-backend-api-040` | A change event triggers one re-list; **three rapid reconnects produce at most one re-list per `open`** (fake timers); `unsubscribe()` closes the stream and no further callbacks fire; fails: no subscription | `npx vitest run src/data/httpApplicationRepository.test.ts -t "subscribe"` | `ready` |
| `TDD-INTENT-m3-backend-api-041` | `BEHAVIOR-m3-backend-api-041` | Emoji, RTL, CJK and a 10 kB `notes` value survive create → read byte-for-byte; fails: encoding path absent | `npx vitest run src/data/httpApplicationRepository.test.ts -t "survives unicode"` | `ready` |
| `TDD-INTENT-m3-backend-api-042` | `BEHAVIOR-m3-backend-api-042` | Real Chromium, HTTP-backed build: create in tab A appears in tab B without a manual reload; fails: app not served by the API build | the CDP harness (`docs/spikes/2026-09-11-real-browser-cross-tab-check/crosstab.mjs`) against `VITE_API_BASE_URL` — **manual, not in `verify`** | `ready` |
| `TDD-INTENT-m3-backend-api-043` | `BEHAVIOR-m3-backend-api-043` | Close and reopen the browser: the record is still listed **and** `psql -c 'select id from applications'` prints it; fails: data only in memory | harness + quoted `psql` output | `ready` |
| `TDD-INTENT-m3-backend-api-044` | `BEHAVIOR-m3-backend-api-044` | `DELETE` with a stale revision → `409` + `code:"conflict"`, and the row **still exists**; fails: blind destroy | `dotnet test ... --filter "~Delete_with_stale_revision_is_refused"` | `ready` |
| `TDD-INTENT-m3-backend-api-045` | `BEHAVIOR-m3-backend-api-045` | Body sent as `text/plain` → `415`, and `count(*)` is unchanged; fails: JSON bound from a non-JSON content type | `dotnet test ... --filter "~Non_json_body_rejected"` | `ready` |

**Two statuses are deliberately not `ready`:** Slice 0's steps (no behaviour — §1.3), and nothing else. Every
behaviour above has a concrete command *and* a concrete expected failure, which is the contract's bar.

### 1.3 Configuration Work — exception verification (Slice 0)

Slice 0 has no observable behaviour to assert before the stack exists, so it takes the exception path rather than
inventing a Red test (`N/A - Configuration Work: no observable behaviour before the stack exists`).

| Step | Acceptance | Evidence |
| :--- | :--- | :--- |
| Install pinned SDK | `dotnet --version` prints **10.0.401** | command output quoted into the Task Record |
| Scaffold `api/` | `dotnet test` runs and exits 0 with **zero** tests discovered | command output |
| PostgreSQL 18.6 up | `postgres --version` prints `18.6`; server reachable on `127.0.0.1:5432` only | `docker compose ps` + `psql -c 'select version()'` |
| First migration applies | `dotnet ef database update` exits 0; `\d applications` shows the `CHECK` and the index | `psql` output |
| CI job gating | docs-only push → job **success / steps skipped**; `api/**` push → steps executed | two run URLs |

---

## 2. Feature Risk Assessment & Critical Invariants

| Risk / Invariant | Severity | Target Seam | Protection Mechanism |
| :--- | :--- | :--- | :--- |
| **A 409 that also wrote** (conflict detection that mutates) | Critical | Integration (real PG) | `BEHAVIOR-…-033`/`-044` assert the row is unchanged after refusal, not just the status code |
| **Silent last-write-wins returns** via a mapping slip (409 → `unavailable`) | Critical | Unit (adapter) | `BEHAVIOR-…-037` table-driven over every `status × code` row; unknown `code` → `corrupt-data` |
| **Client/server validation divergence** (AC-12, grill F-1) | High | Unit + contract fixture | One `validation-cases.json` read by **both** suites; server-only rules must appear as named cases |
| **Stale response overwrites fresh data** (P2-2) | High | Unit (adapter) | `BEHAVIOR-…-039` monotonic sequence per `list()` |
| **Timezone drift on a date-only field** | High | Integration | `BEHAVIOR-…-027` run under `TZ=Pacific/Auckland` |
| **Cross-origin write via a simple request** (grill F-4) | Medium | Integration | `BEHAVIOR-…-045`: only `application/json` accepted → preflight becomes the guard |
| **Data visible only in the browser process** | Medium | E2E + direct SQL | `BEHAVIOR-…-043` pairs the browser assertion with `psql` |
| **Reconnect storm hammering the API** | Low | Unit (fake timers) | `BEHAVIOR-…-040` asserts ≤1 re-list per `open` |

## 3. Test Seam Allocation Matrix

### Unit Tests — Vitest, no I/O (<1 ms)

| Test Target | File | Scenarios |
| :--- | :--- | :--- |
| HTTP → `RepositoryError` mapping | `src/data/httpApplicationRepository.test.ts` | every declared pair, unknown code, transport failure, malformed JSON |
| Out-of-order guard | same | slower first request, equal timestamps, three-way interleave |
| `subscribe()` lifecycle | same | one callback per event, ≤1 re-list per `open`, unsubscribe, double-unsubscribe |
| Adapter selection | `src/data/applicationStore.test.ts` | flag set/unset, both adapters satisfy the interface |
| Client validator vs fixture | `src/domain/validation.fixtureCases.test.ts` | every row of the shared fixture |

### Integration Tests — xUnit + **real PostgreSQL** (Testcontainers)

*The workflow's rule is explicit and is being followed: never EF InMemory or SQLite. SQLite ignores row locks, treats `CHECK`/`jsonb` differently, and would make `BEHAVIOR-…-032`/`-033` pass while proving nothing.*

| Test Target | File | Scenarios |
| :--- | :--- | :--- |
| Query/list | `ApplicationsQueryTests.cs` | empty table, ordering, revision present and changing |
| Create/update/delete | `ApplicationsWriteTests.cs` | round-trip incl. `date`, idempotent retry, delete-then-404, stale `If-Match` on **both** writes, DB `CHECK` rejection |
| Validation contract | `ValidationContractTests.cs` | every fixture row; `415` for a non-JSON body |
| SSE fan-out | `StreamEndpointTests.cs` | `text/event-stream` content type, one event per write, no event when nothing changed |

### End-to-End — the existing CDP harness, **not** Playwright

| Journey | File | Steps & Assertions |
| :--- | :--- | :--- |
| Cross-tab over HTTP | `docs/spikes/2026-09-11-real-browser-cross-tab-check/crosstab.mjs` | Launch Chromium, two targets on **one** browser connection, create in A, assert the card appears in B, then restart the browser and assert persistence + `psql` |

**Deliberate deviation from the template, with the reason:** `pk:test`'s pyramid names Playwright, and this repo
has chosen **not** to add it — the M2b check was built on raw CDP with zero new dependencies, it is documented, and
it is deliberately **outside `verify`** (`PROMPTKIT.md`). Adding a browser framework for one journey would add a
~2.8 GB image and a second test runner to maintain for coverage this harness already provides. The harness's own
limitations are recorded in the spike doc (Chromium only, happy path only, one run, not CI) and apply equally here.

## 4. External Mock Boundaries

*Rule: never mock the database/ORM/internal repositories; mock only across a process boundary.*

| Boundary | Mechanism | Scope | Behaviour simulated |
| :--- | :--- | :--- | :--- |
| **PostgreSQL** | **none — real container** (Testcontainers) | `api/tests/**` fixture | n/a: deliberately unmocked |
| **HTTP API** (from the frontend) | Injected fake `fetch` + fake `EventSource` in unit tests | `src/data/httpApplicationRepository.test.ts` | status codes, problem+json bodies, network rejection, out-of-order resolution |
| **The local adapter** | the real one, in-memory | `src/data/applicationStore.test.ts` | flag-off path stays green |

**Why a fake `fetch` and not MSW:** the adapter *is* the thing under test, so a fake at the `fetch` call keeps the
adapter's own code in the test; MSW would add a dependency and a service-worker surface to assert the same mapping.
Recorded as a deviation from the template's MSW default, with the rule it protects intact ("mock only across a
process boundary" — `fetch` is that boundary).

## 5. Test Data Factories

No factory convention exists yet (13 test files inline their fixtures), so this plan introduces the smallest one
rather than a framework:

```ts
// src/test/factories/application.ts
export function buildApplicationInput(overrides: Partial<ApplicationInput> = {}): ApplicationInput {
  const id = crypto.randomUUID().slice(0, 8)
  return {
    companyName: `Company ${id}`,
    jobTitle: 'Engineer',
    location: 'Auckland',
    status: 'Applied',
    appliedAt: '2026-08-10',
    notes: undefined,
    ...overrides,
  }
}
```

- **C# mirror**: `api/tests/JobTracker.Api.Tests/Factories/ApplicationFactory.cs` with the same defaults, so a
  failure in one stack is reproducible in the other.
- **Isolation**: no tenant dimension exists, so isolation is by **unique ids per test** plus a per-test
  `TRUNCATE applications` against a container shared per test class (Option A adapted, not Option B — transaction
  rollback hides the `CHECK`-violation behaviour under test).
- **Boundary fixtures**: empty table · all five statuses · a sixth status (server-only) · `""` and `"   "` ·
  exactly 120 / 121 chars · 10 kB `notes` · `2026-02-30` · unicode/RTL/CJK.

## 6. Anti-Flakiness & Timing Checklist

- [x] Zero arbitrary `sleep()` in new suites; the SSE tests drive an **injected** EventSource rather than waiting.
- [x] Timers: `vi.useFakeTimers()` for the reconnect-storm case (`BEHAVIOR-…-040`).
- [x] Real-DB tests reset with `TRUNCATE` between cases; one container per test class, never one per test.
- [x] Ports: the container's port is **dynamic** (Testcontainers maps it) so parallel runs cannot collide; the dev
  compose file pins `127.0.0.1:5432` for humans only.
- [x] The one browser journey keeps the harness's existing rule: no `sleep`-based assertion, and the
  `Runtime.evaluate` result shape (`{result:{value}}`) is respected — the bug that once made a healthy app look broken.

## 7. CI Pipeline & Coverage Targets

- **Frontend gate (unchanged)**: `npm run verify` — typecheck → eslint → stylelint → `vitest run` → build. Target
  runtime < 60 s (currently ~10 s). Every PR runs it.
- **API gate (new)**: `dotnet test api/` inside the `api` job, gated by an internal `if:` on changed paths (grill
  F-10) so the job always reports a status. Target runtime < 3 min including container pull; if the runner has no
  Docker, the fallback is a **service container** (grill uncertainty), which is a fixture swap.
- **Browser journey**: manual, on demand, **not** in CI — unchanged from M2b's decision.
- **Coverage target**: **no numeric threshold, deliberately.** A percentage would be met by unit tests over trivial
  branches while `BEHAVIOR-…-033` (the one that can actually lose data) stays unwritten; adding a coverage tool is
  also a new dependency. The coverage argument here is the ladder itself: 20 behaviours, each with a named failing
  assertion, and every acceptance criterion in the Task Record pointing at one of them. **If a threshold is wanted,
  say so and it becomes a Slice 4 task rather than a silent default.**

> **Correction added by Slice 0 (2026-09-11 05:59 UTC):** the paragraph above argues a coverage tool "is also a new
> dependency". True of my intent, **false of the outcome** — `dotnet new xunit` silently brought
> `coverlet.collector 6.0.4` with it, so the dependency arrived the moment the project was scaffolded. Removed,
> because nothing here measures coverage and `AGENTS.md` requires each addition to be justified. The decision
> stands on its own merits; one of its two arguments did not. The general lesson: **read a generated
> `.csproj` before trusting what the template did not tell you.**

---

## 8. Correction to the grill record (found while writing this plan)

`GRILL-m3-backend-api` F-1 claimed *"the client has no length ceiling on `location`/`notes` at all"* and cited an
invariant that reads differently in the spec. Reading `src/domain/validation.ts` before writing the fixture shows
that is **wrong in part**: `MAX_TEXT_LENGTH = 120` **is** enforced on `companyName`, `jobTitle` and `location`
(lines 74–85). The accurate statement is narrower and more useful:

- **`notes` alone has no client-side ceiling** — so a server limit on `notes` *is* a server-only invention, and it
  needs an explicit fixture case.
- **`status` is not validated at all client-side** (it is a TypeScript union only, line 14 of
  `types/application.ts`), so AC-12's plan to feed "a sixth status" to *both* validators is **impossible as
  written**: the client has no runtime verdict to compare. The fixture therefore records a **per-side expectation**
  (`expect.server`, and `expect.client: "not-applicable"` where the type system is the only gate) instead of
  pretending both sides answer every case.

The mechanism F-1 prescribed (one shared fixture) is unchanged and still the fix; its supporting reasoning was
wrong and is corrected here. This is the **fourth time this session that narration outran the artefact** — and the
first inside a document whose subject was scrutiny, which is worth more than the embarrassment: the fix was
AGENTS.md step 2 (*inspect the relevant files before changing anything*), applied one step later than it should
have been.
