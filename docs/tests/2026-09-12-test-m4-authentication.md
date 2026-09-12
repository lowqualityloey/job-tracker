# Test Plan Specification: M4 — Authentication (sessions, ownership, cookie-carried credentials)

- **Author**: Assistant (`pk:test`), for owner review
- **Status**: `In Review` — readiness gate for [`TASK-m4-authentication`](../tasks/TASK-m4-authentication.md)
- **Created**: 2026-09-12
- **Test Frameworks**: **xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql** (all three already in place from
  M3) · **Vitest 2** (jsdom) · **raw-CDP Chromium harness** (`tests/browser/`, `jt-bridge`)
- **Ladder**: `BEHAVIOR-047`…`067` — **21 behaviours**, M3's `-046` being the last before it
- **Count derived, not asserted** — and the first derivation was wrong, so the command ships with its trap:
  `awk '/^## 6\./,/^## 7\./' docs/specs/2026-09-12-spec-m4-authentication.md | grep -oE '\`-0(4[7-9]|5[0-9]|6[0-7])\`' | tr -d '\`' | sort -u | wc -l` → **21**.
  **A naive `-0[0-9]{2}` window returns 22**, because spec §6 cites M3's `-037` mapping pattern in prose. *Counting tokens
  in a section is not counting the behaviours defined in it* — the same family as every absence-grep in this project.

---

## 1. TDD Intent Register

Each row is the promise a Red must fail to break. **Written before the code exists — which is the entire point of this
document.** M3's `-037` and gap 9 both showed what happens when rows are added after implementation: they describe what
got built instead of what was demanded.

| Intent | Behaviour | The promise (what fails without it) | Seam | P |
| :--- | :--- | :--- | :--- | :--- |
| `…-047` | `-047` | `Verify(Hash(p), p)` is true; a **tampered** envelope, wrong salt and wrong iteration count are all false | Unit (API) | p0 |
| `…-048` | `-048` | Iterations are **explicitly configured**, not inherited: the envelope decodes to the configured count (Identity's default is 100k and inheriting it silently is the failure) | Unit | p1 |
| `…-049` | `-049` | Cost band is a **test**, not folklore: `Verify` p50 in-band, `Hash` p95 < 1000 ms — so a runtime upgrade can't quietly change the cost | Unit | p1 |
| `…-050` | `-050` | Seeded user comes from configuration, and the app **refuses to boot with default credentials in `Production`** (a seeded account is the failure mode of a published one) | Integration | p0 |
| `…-051` | `-051` | Login → `204` **and** a `Set-Cookie` header carrying `__Host-JTSession`, `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, **no `Domain=`** — asserted **on the header**; a body assertion cannot detect their absence | Integration | p0 |
| `…-052` | `-052` | Session id is **regenerated at login**: the pre-login cookie is dead afterwards, proven by replaying the old value | Integration | p0 |
| `…-053` | `-053` | ~~Wrong-email and wrong-password are **byte-identical** in body~~ → **restated on execution:** every problem-document member except the framework's per-request `traceId` is equal, `traceId` is present on **both** paths and differs between them, and **neither body contains either email**. Dummy verify's presence proven structurally (counting `IPasswordService` + declared iteration count decoded from the envelope). Timing **reported, never asserted** (grill Q8) — see task record `TDD-EXEC-m4-authentication-053` | Integration | p0 |
| `…-054` | `-054` | Logout sets `revoked_at` — **verified in `psql`, not inferred from the response** — and the old cookie then gets `401` | Integration + DB | p0 |
| `…-055` | `-055` | **Every** data route answers `401` anonymously, `[Theory]` over all six methods **including `/events`** — the stream is the one an attribute typo leaves open, and it is invisible to every other test | Integration | p0 |
| `…-056` | `-056` | User B reading/writing A's row gets **`404`, never `403`** (403 is an existence oracle), and B's list never contains A's rows | Integration | p0 |
| `…-057` | `-057` | **Gap 12 closed:** `{"id":"not-a-guid"}` → `400` + `errors[].pointer:"/id"`. **Red is the 500 reproduced today** (M3 test doc §21), so this row has a known-failing starting state rather than an imagined one | Integration | p0 |
| `…-058` | `-058` | **All 65 M3 tests pass under auth with `Skipped: 0`** — a regression net that catches M4 quietly disabling M3's assertions to get green | Integration | p0 |
| `…-059` | `-059` | The owner index is **used**: `EXPLAIN` asserts an index scan. At 36 rows a seq scan is free; at 100 k it is the incident | DB | p1 |
| `…-060` | `-060` | Problem code → `RepositoryError` variant mapping is **table-driven row-for-row** (the `-037` pattern), so `unauthorized` cannot be added without its row and no code can pass unobserved | Unit (FE) | p0 |
| `…-061` | `-061` | Login screen covers **empty / loading / error / success** with the intended URL preserved and honoured after login | Unit (FE) | p0 |
| `…-062` | `-062` | An `EventSource` failure **probes the session once** instead of retrying forever — a 401 on a stream is otherwise an infinite loop with a spinner | Unit (FE) | p1 |
| `…-063` | `-063` | A cross-site write without `X-CSRF-Token` is rejected — **and the same call with the token succeeds.** Without the positive control a broken endpoint passes as a secured one · **restated into two cases at execution, owner-approved** (spec §6, ~18:10 UTC): ① a **same-site** authenticated write with no header → `403`, ② a **cross-site** write → `401`, because the cookie never arrives and the gate is never consulted; each case carries its own positive control. One case could not attribute its own result — both answers are refusals, and only the *code* says which layer produced them | Browser | p0 | Browser | p0 |
| `…-064` | `-064` | `httpCrossTab.mjs` → **7/7 while authenticated**, in real Chromium. **jsdom cannot attempt this**: no `EventSource`, no origin policy | Browser | p0 |
| `…-065` | `-065` | Login-route bundle delta < 3 kB vs **Slice 0's** baseline, **configuration named** (flag-off tree-shakes the adapter and a 0 kB delta would mean nothing) | Build | p1 |
| `…-066` ★ | `-066` | **Expiry is real:** an idle window **and** a hard cap both end the session, driven by an injected `TimeProvider` (see §3.2), and expired/revoked rows are **pruned** so `sessions` can't grow without bound | Integration + DB | p0 |
| `…-067` ★ | `-067` | CORS echoes the **exact** origin with `Access-Control-Allow-Credentials: true` and **never `*`** — because browsers reject the wildcard+credentials pair *silently*, and ASP.NET will not stop you writing it | Integration + Browser | p0 |
| `…-068` ◆ | `-068` | The change stream is **scoped to the owner of the changed record**: a write by B never reaches A's open stream — and **both of A's tabs still receive A's write**, because an unscoped broadcast was a fine answer until `-050` made this multi-user and the frame body carries `data: {"id": …}` | Integration | p0 |
| `…-069` ◆ | `-069` | A **wrong-typed wire member** (`companyName: 42`, `status: 5`, `notes: [1,2]`) and a malformed body are `400` + `code: "validation"` naming the member — the framework already logged `InvalidJsonRequestBody` with a 400 in hand, and the app was answering 500 with no discriminator | Integration | p0 |
| `…-074` ◆ | `-074` | **The data-protection key ring outlives the process that made it, on every instance.** `-063`'s token is readable only by the host that minted it — measured, not theorised: `OwnershipTests` built a fresh `WebApplicationFactory` per request and six of its own writes came back `403 antiforgery`. Needs shared key persistence (`PersistKeysTo*` over a store every instance can reach) plus an integration case that a token issued on one host validates on another. Until then the deployed behaviour is **a session may only be written to by the instance that issued it**, which behind a load balancer is a user-visible fault, not a theoretical one | Integration | p1 |
| `…-075` ◆ | `-075` | **One browser-harness recipe instead of two `connect()`s and two duplicated boot scripts.** `httpCrossTab.mjs` and `csrfGate.mjs` each carry their own CDP connect / openTab / authenticate block (~150 lines duplicated), and `run-063.sh` and `run-064.sh` — both written today, one copied from the other by me — each re-implement the scratch-DB + self-signed-cert + `vite build` + in-container-run sequence, including two fixes made twice (`DROP DATABASE … WITH (FORCE)`, and killing `JobTracker.Api` by exact name because `dotnet run` does not forward signals). **`run-043.sh` is deliberately out of that set**: measured, it has no TLS step, no build step, no `FORCE` and no `pkill` — it is an http-plus-in-container-dist recipe, so unifying it with the other two would be a rewrite wearing the name of a refactor. Extract the shared pair, then **re-run both rows against the extraction**: a change to evidence tooling that is not verified by re-producing the evidence is just editing | Build/infra | p2 |
| `…-071` | `-071` | **The API serves the built front end as its own origin** (DECISION-007 `(a′)`): the shell at `/`, a real asset as **itself**, a deep client route as the **fallback** — and **`/api/**` still answers as the API**: an unknown API path is a `404` **problem document, never the HTML shell**, and a gated one is still `401`. Gated to Development: **a Production boot with the bundle present serves nothing** | Integration | p0 |

> **`-070` is deliberately unassigned — it is a phantom, found while allocating this number.** Two records
> (`TASK-m4-authentication.checkpoint-001.md` and `docs/STATE.md` §3A) list "**`-070`'s scoped-mutation-gate verdict**" among
> the things the owner owes, and `grep -rln "scoped.mutation" docs/` returns **those two files and nothing else** — no ladder
> row, no task-record entry, no spec reference, no EXEC record. Same defect class as the false AC list corrected on
> 2026-09-12: a list **propagated rather than re-derived**, this time a *number* cited as though assigned. Skipped rather
> than reused on purpose — quietly making it this hosting behaviour would leave the two records that reference it reading as
> satisfied while whatever was actually meant goes unaddressed. **Asked, not repaired.**

**Numbering, stated because `-070` is the lesson:** `-074`/`-075` sit past a gap. `-072` and `-073` are **reserved by
STATE §3A's decisions D-2 and D-3** — proposed rows that have not been ratified into this register, and therefore not
executed and not verifiable. They are not free numbers to reuse, and the gap is the evidence that they are not.
Spec §6 carries the matching amendment for `-074`/`-075`.

◆ = **execution-time additions, not grill-derived** (`-068` set the precedent; `-074`/`-075` follow it). Same rule as ★: appended at the end, never renumbered into the slice that provoked them.

★ = **grill-derived additions** (`Q5`/`Q6` expiry+prune, `Q3` credentialed CORS). Appended as `-066`/`-067` rather than
renumbered into their slices: **renumbering a ratified ladder is exactly how M3's phantom cross-references were made.**
Spec §6 carries the matching amendment.

## 2. Seam Allocation — where each AC is actually proven

| AC | Seam | Why there and not somewhere cheaper |
| :--- | :--- | :--- |
| 1, 5, 6, 8, 13 | API integration (`WebApplicationFactory`) | The unit under test is the **pipeline**: an attribute on an endpoint group is invisible to a handler test |
| 2, 16★ | API integration **on the header** + one browser check | Cookie attributes and `Allow-Credentials` are transport artifacts; jsdom enforces neither |
| 3 | API integration (bodies) + **reported** timings | Assertion on what is deterministic; measurement of what isn't |
| 4 | Unit (API) | Cost is a property of the hasher, not the stack |
| 7 | API integration, two users in the **test** fixture | Owner scoping is a query property; only the test data is multi-user |
| 9, 10 | PostgreSQL (Testcontainers) + `psql`/`EXPLAIN` | Migration ordering and plan choice are not observable through HTTP |
| 11, 12, 14★ | **Real Chromium / build output** | Cross-site behaviour and bundle bytes are produced by tools jsdom is not |
| 12 | Real Chromium (SSE + cookies) | The single most jsdom-hostile pair in the project |
| 15 | Static analysis (`node -p`, `grep`) | A dependency count and a credential literal are not behaviours; asserting them as tests would be theatre |
| 17★ | Integration + `TimeProvider` | Expiry without a clock seam means either a slow test or a fake clock nobody trusts |

**The rule this table encodes: an AC whose seam is "browser" may not be downgraded to jsdom for convenience.** M3 learned
that `-042`'s cross-tab property has no jsdom equivalent at all; a green frontend suite is **silent**, not reassuring.

## 3. Mock Boundaries

**3.1 Never mocked:** PostgreSQL (Testcontainers, same image as dev) · cookie handling and the redirect/preflight path
(`WebApplicationFactory` uses a cookie container, not a stub) · ASP.NET's authentication pipeline (custom
`IAuthenticationSchemeProvider` mocks would test the mock) · `PasswordHasher` itself — Q2's whole reversal was about not
reimplementing what the framework ships, and re-implementing it in a test double would undo the point.

**3.2 The one seam that must be designed in: `TimeProvider`.** `-066` cannot be tested honestly without injectable time:
the alternatives are a test that sleeps through an idle window or one that mutates the system clock — the first is slow
and flaky, the second leaks across the shared-collection fixture M3's tests depend on. **Decision to make at Slice 2,
not at the test:** sessions store `expires_at`/`last_seen` computed from an injected `TimeProvider`, defaulting to
`TimeProvider.System` in production. If that is refused, `-066` degrades to "expiry is a column nobody reads", and the
plan says so out loud rather than leaving a green test that proves nothing.

**3.3 Faked deliberately:** the browser's clock in the CDP harness (spans measured, not asserted — the M3 §19 rule that
measurement is not an assertion, and why the latency harness is deliberately **not** in CI).

## 4. Risk → Detection Seam (which failure only *where* can see it)

| Risk | Only detectable at | Not detectable at |
| :--- | :--- | :--- |
| Cookie attributes silently missing | `Set-Cookie` header assertion / browser | jsdom, body-only API tests |
| `/events` left anonymous | `-055`'s theory | any single-endpoint test |
| SSE dead once authenticated | Chromium (`-064`) | the **entire** frontend suite |
| Wildcard origin + credentials | browser preflight (`-067`) | ASP.NET, which accepts the config |
| Cross-site probe accepted | browser from 2nd origin (`-063`) | jsdom (no origin policy) |
| `404` vs `403` oracle | two-user fixture (`-056`) | single-user suite, entirely |
| Session row growth unbounded | DB count after N logins (`-066`) | every HTTP-level test |
| Enumerateable by timing | **not reliably detectable** — reported only | an assertion that would pass/fail on noise |

**Last row is the honest one.** The grill found that a timing assertion is a coin flip at this sample size, so the
mitigation is structural (the dummy verify) and the number is *published*, not gated. **A suite that "passes" a timing
assertion it cannot separate from noise is worse than one that admits it cannot test the property.**

## 5. CI Allocation

| Suite | CI? | Reason |
| :--- | :--- | :--- |
| API (`dotnet test`, Testcontainers PG) | **Yes** | Fast enough (M3: ~29 s local), deterministic, the whole auth contract |
| Frontend (`npm run verify`) | **Yes** | Already the gate; `-060`–`-062` live here |
| Fixture contract (36 rows → more with `-057`'s id rows) | **Yes** | It is the spec in data form |
| **Browser (`-063`, `-064`, `-067`'s preflight)** | **No — local, gated at sign-off** | Needs `jt-bridge`, a host-reachable API and a container that can route out; **Q4's origin question is unresolved and would make a CI version flaky rather than informative** |

**Stated plainly, because M3's §19 precedent earns it:** three of M4's most important behaviours are **not** enforced in
CI. That is a real gap, and the alternative — a browser job that fails for topology reasons — is worse: it trains
everyone to ignore it. **M5's deployment work should revisit this with the co-location question settled** rather than
shipping a job that is routinely red.

## 6. What this plan deliberately does **not** test

Login rate limiting and lockout (`ASSUMPTION-003`, M5) · CSP and XSS (there is no CSP; `HttpOnly` is the mitigation and
it is asserted as a header) · Argon2id (deferred, trigger = M5 exposure) · multi-user/tenancy beyond the two-user fixture
`-056` needs · password composition policy (there is none, deliberately: **complexity rules are theatre without a
strength meter, and a strength meter is scope**) · cross-instance SSE fan-out (grill Q16 → M5, PG `NOTIFY`) · load and
concurrency on the sessions table · recovery/verification flows (non-goals).

**Also not tested, and named so it can't be mistaken for coverage:** the `__Host-`-over-`http://localhost` assumption
(`ASSUMPTION-001`) is proven by `-051` running in a real browser or not at all — **if the browser rejects it, `-051` must
fail rather than be relaxed in dev config.** A cookie name that differs per environment is how tests drift from
production.

## 7. Readiness Statement

**Ready for implementation, with two named blockers.** `-057` has a **reproduced** Red (gap 12's 500, live in `main`), so
the ladder starts with a known-failing assertion instead of an imagined one — the rarest and most useful position a TDD
plan can be in. The blockers: **① Q4's harness-origin decision (owner), which gates Slice 2's browser work and nothing
before it**, and **② §3.2's `TimeProvider` decision, which gates `-066`** and must be taken at Slice 2's start rather than
discovered mid-test. **Slice 0 (`-none`) and Slice 1 (`-047`…`-050`) can begin immediately**, and Slice 0's baseline bytes
should be taken first so `-065` stays computable.

### Counts, re-derived after this document

| Claim | Command | Value |
| :--- | :--- | :--- |
| Behaviours (register rows) | `grep -cE '^\| `…-0(4[7-9]|[5-6][0-9])`' docs/tests/2026-09-12-test-m4-authentication.md` | **21** |
| Ladder range | `… \| grep -oE '[0-9]{3}$' \| sort -n \| sed -n '1p;$p'` | **`-047` … `-067`** |
| Acceptance criteria | `grep -cE '^- \[.\] \*\*AC-' docs/tasks/TASK-m4-authentication.md` | **17** (0 verified) |
| Grill questions | `grep -c '^## Q' docs/reviews/2026-09-12-m4-plan-grill.md` | **9** |

**Every projection of these numbers was updated in the same commit** — `docs/STATE.md`, the task record's §3/§5, and
spec §6's amendment. They previously read 19 behaviours / 15 ACs; **a plan that moves the ladder must move all four
projections of it**, or the plan itself becomes the drift. The failure this prevents is the one STATE §3A produced
once already: five turns of locally-true edits accumulating into a globally-false block.**
*Re-check command, scoped on purpose:* `grep -cE '19 behaviours|15 ACs' docs/STATE.md docs/tasks/TASK-m4-authentication.md docs/specs/2026-09-12-spec-m4-authentication.md` → **0 / 0 / 0**. **It must exclude this file**: the sentence above it
*mentions* the old numbers to say they are gone, so a repo-wide grep returns **2 matches here** — a re-check command
that matches its own prose is a permanent failure by construction. **Verified 0/0/0 at commit time; the two in this
file are describing, not asserting.**
