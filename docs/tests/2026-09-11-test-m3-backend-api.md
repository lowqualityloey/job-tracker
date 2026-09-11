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

---

## 11. Correction added by Slice 1 (2026-09-11, executed)

Register rows are commands, so they have to run. Three of the four Slice 1 Red commands name test methods that do
not exist; the code won, and the intent behind each row is unchanged. Reconciled here rather than by editing rows
above, so the drift is visible:

| Intent | Registered filter | **Runnable filter** | Registered failure mode | Landed as |
| :--- | :--- | :--- | :--- | :--- |
| `-026` | `~ApplicationsQueryTests.Empty_list_returns_200_empty_array` | `~Empty_catalog_returns_200_with_an_empty_array` | field/status absent | `Assert.Equal: Expected OK / Actual NotFound` ✓ as predicted |
| `-027` | `~RoundTripsEveryField` | `~A_persisted_row_is_returned_with_every_field_the_client_already_models` | round-trip mismatch | `42703 column "company_name" does not exist` — arrangement, not assertion; **weaker Red than registered**, so the behaviour got four mutation checks instead |
| `-028` | `~UnknownId_returns_404_problem` | `~An_unknown_id_answers_404_with_a_problem_document` (theory) + `~A_known_id_answers_200_with_that_record_and_nothing_else` | wrong status/body | `KeyNotFoundException` on `code`: the framework **already** sends `application/problem+json` with `status`, so the registered "wrong body" became "body without the discriminator" — measured, see §TDD-EXEC-028 |
| `-029` | `~Revision_changes_after_update` | unchanged ✓ | field absent | exactly as registered ✓ |

Two additions the register did not anticipate, both kept because they are assertions a reviewer cannot get from the
rows above: 027 checks **no response field starts uppercase and none contains an underscore** (per-field
assertions can only catch the fields someone remembered to list), and 028 answers a **malformed** id as well as an
unknown one, because the tempting `{id:guid}` route constraint would satisfy the registered case and break the
envelope rule for the input the client cannot construct.

AC-5's "client whose machine is UTC+13" is now a command, not an adjective: `TZ=Etc/GMT-13 dotnet test
api/tests/JobTracker.Api.Tests` → `Passed: 10, Failed: 0`. Note for anyone re-running it: this host is **NZST+12**,
so `Pacific/Auckland` in September measures +12, not +13.

**`-032` executed early, in Slice 1** (2026-09-11 07:35 UTC): the intent was written against a table that did not exist when the
register was authored, and once the table existed the CHECK inside it did not. The registered expectation was
"constraint not enforced"; the actual Red was `Assert.Throws() Failure: No exception was thrown` — the same
conclusion, reached by inserting a sixth status into the shipped schema rather than by reading a migration file and
believing it. Slice 0's harness probe (23514 on a `create temporary table`) is now labelled in code as a **premise
test about PostgreSQL, not evidence about `applications`**, so the confusion cannot be re-made by the next reader.

## §12 — Corrections added by Slice 2 (2026-09-11 08:31 UTC)

Same rule as §11: **nothing above this line is rewritten.** Registered intents stand; this section records where
execution diverged and why, and every row below is named by its `TDD-INTENT` id.

| Row | What changed | Why it is legitimate |
| :--- | :--- | :--- |
| `-035` | The test now reads the row's current `revision` and sends `If-Match` — the registered text says only "`DELETE` → `204`, then `GET` → `404`" | grill **F-3** amended §4.3's DELETE row (If-Match *required*) **after** this intent was registered, and `-044`'s Green broke the test. Fixing the test to match the written-down contract is honest; weakening the handler to keep a green count would have been F-3 rejected on the floor |
| `-044` | A second theory case, `[InlineData(null)]`: a DELETE with **no** `If-Match` must also be refused | F-3's amendment is that the precondition is *not optional*. A handler that rejects stale tokens while accepting absent ones enforces a rule nobody wrote. The registered row covers stale only |
| `-033` | An added assertion that `updated_at > created_at` after the winner's PUT | §5's risk table promises "`updated_at` set in the DB, never sent by the client" and §4.1 indexes on it; `DEFAULT now()` fires on INSERT only, so both statements were false of the shipped schema. Found by the update behaviour's own test, since no `-0xx` row covers timestamps |
| `-045` | **Produced no Red.** Green on first run; committed as a regression guard carrying its own positive control (same bytes as `application/json` → `201`, `count(*) == 1`) | ASP.NET Core's JSON binder refuses a non-`application/json` body with 415 before the handler executes. Stating that the framework already satisfied a registered behaviour is more useful than manufacturing a cycle around it |

**Method names did not drift this slice.** Slice 1 renamed three of four behaviours between register and code, which
§11 reconciled after the fact; in Slice 2 the registered `--filter` strings (`Create_visible_to_second_client`,
`Retry_of_create_is_idempotent`, `Delete_then_missing`, `Delete_with_stale_revision_is_refused`,
`Stale_if_match_conflicts_without_writing`, `Non_json_body_rejected`) were read out of the table *before* the tests
were written and used verbatim, so every command in §3 above the line runs as printed. That is the process fix the
§11 note implied, applied instead of merely recorded.

**Register gap opened by Slice 2, unresolved and awaiting the owner**: no behaviour anywhere in `026–045` covers a
**successful `PUT`**. `-033` performs one as its arrangement (a revision cannot become stale unless something
advanced it) and asserts its 200, so a broken update would be caught — but by a test whose name says otherwise.
Options: register `-046` for update round-trip + revision advance, or let `-042`'s browser run own it. Recorded in
`§7` of the task record alongside the two older gaps (the unasserted index, and `ETag`, which Slice 2 declined to
emit for want of a consumer).

## §13 — Corrections added by Slice 2b (2026-09-11 09:55 UTC, `BEHAVIOR-…-031` + F-1's fixture)

| Register row | Planned | Executed | Status |
| :--- | :--- | :--- | :--- |
| `…-031` | `…~Validation_matches_client_boundaries`, one `[Fact]` | `Validation_matches_declared_cases`, a 34-case `[Theory]` over the shared fixture | **executed, name amended below** |
| `…-032` | the four `CHECK`/default assertions | unchanged, plus one additive guard (see `-031+`) | executed early |

**The name drift is the finding, and it is mine rather than the register's.** Slice 2a opened §12's discipline of
reading each registered `--filter` string out of the table *before* writing the test, and every quoted command in
Slices 0–2a then ran as printed. `-031` is the one behaviour where I did not: the ladder predicted
`Validation_matches_client_boundaries` and the shipped method is `Validation_matches_declared_cases`, because the
fixture turned out to carry normalisation and accept-cases as well as boundaries, and "matches client boundaries"
describes a narrower test than the one that exists. The register's filter column is amended to the real name for
the same reason the last two slices amended rows on contact: a plan nobody corrects is not a plan, it is a document
that slowly stops being true.

**`-031+` · `No_text_column_carries_an_unchosen_default` — an assertion with no `BEHAVIOR` id.** It arrived while
closing deferred item 1, and it belongs to `-032`'s family (schema fidelity against §4.1's DDL) rather than to the
validator's. Registered here as additive so the ladder stays the authority on what is *supposed* to be covered, and
so the next reader does not conclude from its absence in §7 that it was dropped. Recommend folding into `-032`'s row
at the next amendment, since what it guards — "`NOT NULL` means *omission* is impossible, not merely explicit
`null`" — is exactly the constraint 032 asserts from the other side.

**AC-12's boundary list, audited against the fixture rather than asserted from memory.** The clause reads
"empty, max, max+1, whitespace-only, unicode, control chars, 10 kB notes, all five statuses + a sixth", and the
easiest way to fail it silently was to write 34 rows that felt comprehensive. Measured from the JSON: all five
statuses have an `api: accept` row; `status-sixth-value`, `status-lowercase`, `status-empty` reject; unicode is two
rows (`job-title-at-max-emoji` at 120 UTF-16 units, `job-title-over-max-emoji` at 122 — the pair that catches either
side switching to code points); control chars and the 10 kB probe are one row each. **The `notes` ceiling is absent
on purpose**: neither validator has one, and `notes-ten-kilobytes` is the flip point if the owner decides otherwise.

**One Green with no Red, disclosed in both directions.** `validationFixtureContract.test.ts` passed its 35 tests on
first run, because its rows were derived *from* `validation.ts`. That is not a TDD violation — the Red for this
behaviour is the 17-failure API run — but it does mean the client suite evidences *my reading of the file*, not a
behaviour driven out of a failure. §11's retracted AC-4 claim is the reason this sentence exists at all: the
temptation is to describe every passing test as if it had once failed.

**Register gaps after Slice 2b — four, all in §4.3/§4.1, none of them new code paths:**
1. **A successful `PUT` has no behaviour of its own** (§12's gap, still open): `-033` performs one as arrangement
   and asserts its 200. Recommend `-046` for update round-trip + revision advance.
2. **`PUT`'s `400 validation` is now possible and still unasserted.** `dd501ef` routes both verbs through the same
   `ApplicationValidation.Validate` call — §4.3 says both reject — but no test sends an invalid body to `PUT`. The
   rules cannot diverge between verbs, which is the argument for not duplicating them; it is not an argument for
   leaving the path untested. A `-046` that asserts the update round-trip should carry this assertion too.
3. **The index in §4.1 is not created by any migration**, so `ORDER BY created_at DESC` has no supporting index —
   invisible at five rows, and the only cheap moment to fix it is before data exists.
4. **`ETag` response headers are emitted nowhere.** §4.3 documents them; `revision` in the JSON body carries the
   same token and the client reads only the body (DECISION-m3-backend-api-006). Recommend retiring the §4.3 line at
   the next contract amendment rather than emitting a second copy of a value that must then be kept in agreement.

## §14 — Two gaps opened by Slice 3 (2026-09-11 10:35 UTC, `-036` + `-037`)

**Gap 5 — a `conflict` reaches the UI and nothing has decided what it says.** §4.3 specifies the sentence ("someone
else changed this record; reload to see it") and calls it the first user-facing line in this project that only a
server could make necessary. `-037` had to add the variant to make the table total, and AC-1 forbids touching
`src/pages/**` this milestone, so the code now exists with no behaviour asserting how it reads. It is not *unsafe*:
the provider refuses further writes on any error, so a 409 shows a generic failure and loses nothing. But a generic
failure is advice to retry, and retrying is the one thing a conflict must not invite. Two fixes — register `-046` for
the sentence and admit that file to the scope, or state in the spec's correction section that M3 ships generic and
name the milestone that owns the wording.

**Gap 6 — `AC-9` and §4.3 disagree about a `fetch` rejection, inside the same approved spec.** AC-9: "a `fetch`
rejection maps to `unavailable`". §4.3's table: `storage-error`, "deliberately **not** `unavailable`", with a
rationale attached. The spec's own behaviour ladder repeats AC-9's version. Where the ladder and the table appear to
overlap — "PostgreSQL container down / unreachable at request time → `unavailable` in the client" — they do not
conflict at all: that path is a real **503 emitted by the API**, not a `TypeError` from `fetch`. "Server down" is two
different events and the ladder names only one of them, which is how the contradiction was written without anyone
noticing. `-037`'s Green implements §4.3, the more specific statement, the one carrying reasoning, and consistent with
DECISION-006's rejection of `unavailable` for situations where the service is plainly available. Recorded rather than
chosen silently, and `-038` must assert the **label**, because in this app the two codes are otherwise
indistinguishable — the refusal gate engages for both.

**Amendment to `-037` in §7**: executed as twelve table cases plus two guards in the new
`src/data/httpApplicationRepository.test.ts`. The registered filter `-t "error mapping"` matches the `describe` and
so selects 14 tests, not 12; the count is left as-is because the two extra assertions belong with the block.
