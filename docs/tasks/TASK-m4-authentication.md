# Task Record: M4 — Authentication (sessions, ownership, and the credential `EventSource` can carry)

<a id="TASK-m4-authentication"></a>

> **Fill-in note:** `sdlc-overlay-v1`, matching `TASK-m3-backend-api`. **No field is left blank to imply a value**:
> `Pending` means "not executed yet", `N/A - <reason>` means "does not apply", and a number is written only beside the
> command that produced it. That convention exists because M3's records were written from intent several times —
> including one claim of an unfalsifiable spec target that was simply a truncated read (gap 10b's retraction).

## 1. Identity and Authority

- **Record Type**: `Task Record`
- **Task ID**: `TASK-m4-authentication`
- **PromptKit Adaptation Profile**: `sdlc-overlay-v1`
- **Work Type**: `Code Work` (Slice 0 is `Configuration/Documentation Work` — recorded per-behaviour)
- **Planning Record Link**: [`PLAN-m4-authentication`](../specs/2026-09-12-spec-m4-authentication.md#Planning-Record-PromptKit-Adaptation)
- **Planning Depth Reference**: `Full` · **Ceremony Level**: `2 (Controlled)`
- **Specification**: [`docs/specs/2026-09-12-spec-m4-authentication.md`](../specs/2026-09-12-spec-m4-authentication.md)
- **Assumption Record Links**: `ASSUMPTION-m4-auth-001` (localhost `__Host-`) · `002` (single user) · `003` (no rate limiter)
- **Decision Records**: `DECISION-m4-auth-001…006`, all in the spec; provenance of approval recorded in §8 below
- **Grill record**: **`Done`** — [`docs/reviews/2026-09-12-m4-plan-grill.md`](../reviews/2026-09-12-m4-plan-grill.md) (Q1–Q9, merged in #36 at `f0ff049`). **Reversed `DECISION-003`**: its no-new-dependency premise was an **npm** count applied to backend code, and `PasswordHasher<TUser>` ships in the shared framework (compile-proved: `CS5001` only, no `CS0246`). Narrowed `002` to Identity's stores, corrected `001`'s "impossible" to "expensive", restated AC-3, raised **Q4** (cross-site harness topology) as an open decision gating Slice 2.
- **Test plan**: **`Created 2026-09-12`** — [`docs/tests/2026-09-12-test-m4-authentication.md`](../tests/2026-09-12-test-m4-authentication.md): 21-row intent register, seam allocation, mock boundaries (never-mocked: PostgreSQL, the cookie path, the auth pipeline, `PasswordHasher`), a risk→seam table naming what only a browser can see, CI allocation (**three behaviours deliberately NOT enforced in CI, recorded as a gap**), and two named blockers. **`-057` begins from a reproduced Red** (gap 12's live 500) — the rarest useful position a ladder can start in.

- **External Reference**: `N/A` — the Local Task Record is authoritative; no GitHub issue mirrors it (project convention)
- **Owner / Actor**: Lead Engineer (accountable; approves, merges, signs) · Assistant (executing)
- **Execution Scope**: **New** `api/src/JobTracker.Api/Auth/*`, one EF migration set (`users`, `sessions`,
  `applications.owner_id`), `src/auth/*` (login route + session probe). **Changed** `Program.cs`, `ApplicationCatalog.cs`,
  `NewApplicationRequest.cs` (gap 12), `src/data/httpApplicationRepository.ts` (ninth variant), `src/domain/*` (**only**
  the `RepositoryError` union), `.github/workflows/ci.yml` **only if** an auth job needs separating, `tests/browser/*`
  (cookies). **Untouched**: `src/pages|components|state` — **this is AC-12 and it is expected to FAIL**; if a login
  screen cannot be added without a page, that is a decision recorded, not a surprise absorbed silently.
- **Approval Boundary (human-only)**: merging PRs; **any new runtime dependency** (the count is 3 and has held since M0);
  any credential literal in source or fixtures; deploying; pushing to `main`; the M4→M5 gate list.
  **Agent-only**: branch, commits, push of the branch, PR creation, and running every command in §3.
- **Created**: 2026-09-12 04:20 UTC
- **Active Task Pointer**: `TASK-m4-authentication` — **taken from `TASK-m3-backend-api` on 2026-09-12.** M3 retains no agent work (owner sign-off only); see that record's pointer field.
- **TDD Enforcement Mode**: **`enabled`** — canonical here. Red, Green and Refactor are **separate commits** (AGENTS.md
  rule 5; eight commit-discipline rules now, nine after #33).

> **Approval status, stated plainly**: the owner approved this design by **merging PR #34 on 2026-09-12 04:17 UTC**.
> That merge preceded the recording of the approvals, so §8 registers them as **inferred from merge, not from ticked
> boxes** — spec §7's eight checkboxes are still physically unchecked and are the owner's to tick. **Dissent costs one
> commit** against any line in §8 attributed this way.

## 2. Objective and Boundaries

- **Objective**: every `/api/applications*` route, including `/events`, requires a valid session carried as a
  `__Host-`-prefixed HttpOnly cookie; `applications` become owned; the frontend learns `unauthorized` and can log in,
  **without breaking the live cross-tab update M3 built**.
- **In scope**: session auth (two tables), PBKDF2 hashing with cost in the envelope, owner scoping with expand–contract
  migration, CSRF token on writes, gap 12's `id` validation, the ninth `RepositoryError` variant, a login route + session
  probe, real-browser proof that SSE works authenticated, and the **Slice 0 bundle baseline**.
- **Explicit Non-Goals**: registration · password reset/recovery · email verification · MFA · external providers ·
  **roles and tenancy** · logout-everywhere · token refresh (no token) · rate limiting (gated to M5) · CSP ·
  AWS/IAM (M5) · Argon2id (**deferred with a named trigger: M5 exposure**) · `notes`/`location` open items carried from
  M3.
- **Definition of Done**: §3's ACs verified **with their commands quoted against each**, §6's gate filled, spec §7 signed
  by the owner, and the M5 gate list written into `docs/STATE.md` §2 rather than left in prose.

## 3. Acceptance Criteria

**Each carries the command that proves it.** An AC whose command cannot be run yet is marked `Pending` — never `Verified`
on the strength of intent. *(M3's AC-11 and AC-14 both had prose claiming closure over an unticked box; every status
below is asserted from the file, and the tally is checked as `checked + open == 17`.)*

- [x] **AC-1** — **Every data route rejects anonymity, the stream included.**  *(verified 2026-09-12 by `-055`: the six-method theory **and** AC-1's own curl gate on a live server, preflight excluded by design; see §6)*
  · `curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5080/api/applications` → `401`; **same for
  `/api/applications/events`, `…/{id}`, and for `POST`/`PUT`/`DELETE`.**
  · *Why the stream is named:* an attribute typo on one endpoint is invisible to every other test.

- [x] **AC-2** — **The session cookie carries all four attributes, asserted on the header.**  *(verified 2026-09-12: `-051`'s header assertions AND the AC's own curl gate on a real Kestrel; see §6)*
  · `curl -i -s -X POST …/api/auth/login -H 'content-type: application/json' -d @/tmp/creds.json | grep -i '^set-cookie'`
  → contains `__Host-JTSession`, `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, **and no `Domain=`**.
  · *Artifact discipline:* these are **header-only** properties. A body assertion cannot detect their absence — that is
  gap 10a's lesson (`ETag`/`revision`: **a test asserting *A or B* cannot detect the absence of *A***).

- [ ] **AC-3** — **Login is not enumerable by message or timing.**
  · Wrong-email and wrong-password bodies are **byte-identical**; `dotnet test --filter ~EnumerationResistance` asserts
  50 samples each and a documented median bound, **with a dummy-hash verify on the absent-user path**.
  · *Named weakness, from spec §7:* a 5 ms bound over 50 samples may be noise. If the method can't distinguish a
  400 ms missing verify, the AC is **restated or dropped**, not quietly passed. M3's gap 10b retraction is the standing
  reason to prefer "this number is weak" over "this number is safe".

  · **⚠️ Restated by grill Q8 (2026-09-12).** The **assertions** are the byte-identical bodies, the identical status code, and the dummy verify's presence proven structurally. The 5 ms/50-sample timing bound is **reported with `n`, min/median/max and warm-up, and asserts nothing** — no run has measured whether that bound separates a 400 ms signal from noise. AC count stays 15; only this AC's gate weakened, deliberately and on the record.

- [x] **AC-4** — **Password cost is measured and bounded, and the assertion is the test.**  *(verified 2026-09-12 by `BEHAVIOR-048`+`-049`; percentiles from the dedicated bench — the committed in-suite guard is a **contention-immune ratio** after its absolute band flaked, see §6's amendment)*
  · `dotnet test --filter ~PasswordCost` → `Hash` p95 < 1000 ms, `Verify` p50 within 200–800 ms; iterations recorded in
  the envelope. **No number is asserted in this record before that run.**

- [x] **AC-5** — **The session id is regenerated at login**  *(verified 2026-09-12 by `-052`: replayed pre-login cookie gets `401`, new one `200`, DB shows revoked=1/live=1)* — a pre-login cookie is invalid after, and the test proves it
  by re-using the old value. *(Fixation is otherwise a comment, not a behaviour.)*

- [ ] **AC-6** — **Logout revokes server-side.** `sessions.revoked_at` set, verified by `psql`; the old cookie then gets
  `401`. Clearing the browser cookie is not the control.

- [ ] **AC-7** — **Ownership: user B cannot see or touch A's rows, and gets `404` — never `403`.**
  · Two seeded users in the test fixture only; `GET`/`PUT`/`DELETE` on A's id as B → **`404`**.
  · *403 is an oracle* — it confirms the id exists.

- [ ] **AC-8** — **Gap 12 fixed: a malformed `id` is a `400` validation problem, not a `500`.**
  · `curl -s -X POST … -d '{"id":"not-a-guid",…}'` → `400` + `errors[].pointer: "/id"` + `code: 'validation'`.
  · **Red is today's `500` on exactly this body** — the defect is already reproduced and recorded in the M3 test doc §21.

- [ ] **AC-9** — **`owner_id` migration is expand–contract with the assertion before the constraint.**
  · `SELECT count(*) FROM applications WHERE owner_id IS NULL` → **0**, *then* `SET NOT NULL`; both migrations applied to
  a **non-empty** database in the test path, not just an empty one (M3's risk table flagged this).

- [ ] **AC-10** — **The owner index is used.** `EXPLAIN` on the scoped list query asserts an index scan, not a seq scan.
  · *At 36 rows a full scan is free; at 100 k it is the incident.* This is the target most likely to be "verified" by
  reading the SQL and nodding.

- [ ] **AC-11** — **A cross-site write without `X-CSRF-Token` is rejected — and the same write with it succeeds.**
  · Browser harness from a second origin (`jt-bridge` recipe in STATE §3A). **The positive control is mandatory:**
  without it a broken endpoint passes as a secured one.

- [ ] **AC-12** — **SSE still delivers cross-tab while authenticated, in real Chromium.**
  · `docker exec … node /srv/httpCrossTab.mjs` → **7/7 with cookies**, no test-only shims.
  · **jsdom cannot attempt this** (no `EventSource`, no origin policy) — which is *why* it is an AC and not a nice-to-have.

- [ ] **AC-13** — **All 65 M3 API tests pass under auth with `Skipped: 0`.**
  · `dotnet test` → `Passed: 65, Failed: 0, Skipped: 0`. **Read `Skipped` as well as `Failed`** (M3 lost an hour to a
  filter matching only part of a `describe`).

- [ ] **AC-14** — **The login route's bundle delta is < 3 kB against the Slice 0 baseline**, computed from build output,
  **configuration named**.
  · `git worktree add /tmp/m4base <Slice 0 sha> && VITE_API_BASE_URL=… npx vite build && cat dist/assets/*.js | gzip -c | wc -c`.
  · *Two M3 lessons fused:* the baseline is captured **in advance** (§2.8 told Slice 3 to record it and Slice 3 never
  did, making the delta uncomputable), and the **configuration is part of the number** (flag-off the adapter is
  tree-shaken and the delta is a flattering, meaningless 0 kB).

- [x] **AC-15** — **No new runtime dependency, and no credential literal in source.**  *(verified 2026-09-12: deps 3/5 unchanged, 0 credential literals, guard live, seed landed — see §6 `-050b`)*
  · `node -p "Object.keys(require('./package.json').dependencies).length"` → **3**; `grep -rniE "password\s*=\s*\"|secret\s*=\s*\"" api src` → **0 matches**.
  · Plus: **the app refuses to boot with default credentials in `Production`** — a startup guard, because a seeded
  account is the failure mode of a published one.

- [ ] **AC-16** — **Credentialed CORS is explicit, not incidental.** Preflight echoes `Access-Control-Allow-Credentials: true` with the **exact** origin and never `*`.
  · *Why it exists:* `Program.cs:38` promised that adding credentials later would "break quietly rather than loudly," and it was right — browsers reject the wildcard+credentials pair and ASP.NET will not stop you configuring it. Grill **Q3**.
- [ ] **AC-17** — **Sessions expire, and expired rows are pruned.** An idle window **and** a hard cap both end a session; expired/revoked rows are removed opportunistically at login.
  · *Precondition (test plan §3.2):* needs an injected `TimeProvider`; otherwise `-066` can only be tested by sleeping or mutating the system clock across a shared fixture. The clock seam is therefore decided at Slice 2's start, and if refused the behaviour degrades to "a column nobody reads" — stated rather than papered over. Grill **Q5/Q6**.

## 4. Invariants

- **I-1** — No state-changing `GET`, ever. `SameSite=Lax` safety is a consequence of this, so breaking it silently breaks
  AC-11's premise. *(Check: grep for `MapPost|MapPut|MapDelete` and assert none are `MapGet`.)*
- **I-2** — `RepositoryError` gains **exactly one** variant (`unauthorized`), and `-037`'s table-driven mapping is
  extended **row-for-row**, so a new problem code cannot pass unobserved.
- **I-3** — `JobApplication` gains no auth field. `owner_id` is a **server-side scope**, never client-visible or
  client-submitted — the client must not be able to name its own owner.
- **I-4** — The session token never appears in a URL, a log line, or `localStorage`.
- **I-5** — Runtime dependencies stay at 3. Challenged as a *reflex* in the grill, honoured as a constraint until then.

## 5. Behaviour Ladder (from spec §6)

**Reconciled against spec §6 on 2026-09-12 04:30 UTC — the record defers to the spec.** A first draft of this section gave Slice 0 a behaviour ID and ran the ladder to `-066`: **20 behaviours where the spec defines 19.** Silent renumbering between a spec and its task record is how M3's phantom section reference was made — a paraphrase hardening into a pointer nobody checked. Spec §6 read **whole, without truncation** (gap 10b's exact mechanism), gives:

**Slice 0 = no behaviour ID** (Configuration/Documentation: its evidence is the recorded baseline bytes, filed under §6, not a behaviour) · **`-047`…`-050`** Slice 1 · **`-051`…`-054`** Slice 2 · **`-055`…`-059`** Slice 3 · **`-060`…`-062`** Slice 4 · **`-063`…`-065`** Slice 5 · plus **`-066`** (Slice 2) and **`-067`** (Slice 3), **appended** by the §6 amendment = **21 behaviours** (`-047`…`-067`), continuing after M3's `-046`.
· *Count command, run:* `awk '/^## 6\./,/^## 7\./' docs/specs/2026-09-12-spec-m4-authentication.md | grep -oE '\`-0[0-9]{2}\`' | tr -d '\`' | sort -u | wc -l` → **21**, range `-047` / `-067`. **The count moved 19 → 21 by a documented spec amendment (`-066`, `-067`), not by this record drifting** — that distinction is why the derivation is printed: an unexplained count is a bug report waiting to be filed against the document. **The command itself was corrected once**: a `-0[0-9]{2}` scan double-counts M3's `-037` reference in §6 prose and reports 22. **The count is derived from the spec's own lines, not from this paragraph's intention.**

> **Amendment 2026-09-12 — executed out of ID order, and the IDs are left as written so the reorder stays visible.**
> `-055` (the authentication gate) was executed before `-052` and `-054`, because both of those rows state their proof as
> *"replaying the old cookie gets `401`"* — and before `-055`, **a replayed cookie and no cookie produce the same `200`**,
> so both p0 behaviours could only have asserted something weaker than their own rows demand. Discovered at `-052`'s
> execution step, not in review. **The rows themselves are unchanged**: the numbering is the plan's history, and silently
> renumbering to hide a dependency error would destroy the evidence that the plan had one.

Each gets a `TDD-EXEC-m4-authentication-NNN` block **written in the turn that earns it**; M3 finished with 21 behaviours
and 20 blocks, and writing the missing one exposed a false claim about two others. **The count is asserted, not recalled.**

## 6. Evidence and Completion Gate

### Slice 0 — pre-M4 bundle baseline (executed 2026-09-12 05:25 UTC, at `41b32af`)

**This slice exists because §2.8 of M3 assigned a baseline to Slice 3 and Slice 3 never took it**, which made the
`< 2 kB` delta uncomputable until it was reconstructed from an old commit. The command, run on `main` with **nothing**
of M4's code present:

```
rm -rf dist && VITE_API_BASE_URL="http://172.23.124.252:5080" npm run build && cat dist/assets/*.js | gzip -c | wc -c
```

| Configuration | JS gzipped | Delta vs flag-off |
| :--- | ---: | ---: |
| **flag-OFF** (`VITE_API_BASE_URL` unset) | **60,118 B** | — |
| **flag-ON**, URL = `http://172.23.124.252:5080` (this machine's sandbox IP) | **61,368 B** | **+1,250 B** |
| flag-ON, URL = `http://localhost:5080` | **61,359 B** | **+1,241 B** ← M3's §2.8 figure |

**Finding, and it belongs in the record rather than being smoothed over: the baseline is configuration-dependent down to
the length of the URL literal.** The API base is inlined by `import.meta.env` substitution, so a 14-character IP versus a
9-character `localhost` moves the gzip sum by 9 bytes — and M3's `+1,241` was taken with the **`localhost`** spelling while
today's natural harness build uses the IP. **The flag-off reproduction landed on M3's number exactly (60,118), which is
what tells us the drift is in the URL and not in the code.** `-065` therefore compares against **61,368 B at the IP
configuration**, and states which literal it used; a delta quoted without its URL is a number about a build nobody ships
(the same trap the flag-off 0 kB delta was).

**Also recorded:** `dist/` is gitignored (checked with `git check-ignore`), and the tree was verified clean
(`dirty=0`) after the builds — a build artifact left in the worktree is how an unrelated file rides into a commit.

---

**`TDD-EXEC-m4-authentication-047`** · `BEHAVIOR-047` · Red `0d28da4` → Green *(this commit)* · p0 · **Slice 1**
- **Red, quoted from the run:** `dotnet test --filter "FullyQualifiedName~PasswordServiceTests"` →
  **`Failed: 5, Passed: 0, Skipped: 0`**, every failure `System.NotSupportedException : BEHAVIOR-047 Red: no
  implementation yet.` `bin/` and `obj/` deleted first, so the build was clean rather than incremental (AGENTS.md rule 8),
  and the whole output read: **0 warnings, 0 errors.**
- **Green:** `dotnet test --filter ~PasswordServiceTests` → **6/6**; full suite → **`Failed: 0, Passed: 71, Skipped: 0`**
  — the 65 M3 behaviours plus these 6, no skips, so AC-13 holds at this boundary.
- **Ladder-hygiene deviation, disclosed rather than manufactured.** The tamper test was *wrong* and a sixth test was added
  during Green, so neither has its own Red commit. The defect: flipping the **last** character of Identity's hash replaces
  base64 **padding**, producing a malformed string rather than a tampered credential — and `PasswordHasher` reacts to that
  by **throwing `FormatException`** instead of returning `Failed`. Both cases are now separate tests because they broke for
  different reasons. The Red evidence for the new one is that logged throw, in a tree differing from Green only by the
  not-yet-written `catch`. **A manufactured intermediate commit to satisfy the ritual would have been worse than naming
  this**: `-043`'s record set the precedent that an honest "no Red here, and why" beats a staged failure.
- **⚠️ Gap 12's sibling, found by a unit test before any endpoint existed.** `Verify` on a corrupt *stored* value threw
  toward a 500 — same shape as the binder throwing on an unparseable `Guid` client id, one layer down. Fixed narrowly:
  `catch (FormatException)`, **not** `catch (Exception)` — converting a real bug into a wrong-password answer is the one
  outcome a login path must never produce silently. **Recorded as the practice task's own sweep paying off immediately.**
- **One decision with no test yet, named instead of hidden:** `SuccessRehashNeeded` counts as verified. Treat it as
  failure and `-048` — whose job is raising the iteration count above Identity's inherited 100,000 — **silently locks out
  every existing account the moment it goes green.** `-048` must therefore assert both the configured count *and* that a
  lower-count hash still verifies while needing rehash. The interface was also split into its own file after a Green
  rewrite deleted `IPasswordService` while replacing the implementation sharing its file — `CS0246` pointed at the
  declaration site, not the vanished file.
- **Still unreached by any caller:** the service is not registered in `Program.cs` — deliberately, because `-050`'s boot
  guard has to be the thing that makes the app refuse to start. **A `null` password still throws
  `ArgumentNullException` at this seam**; that becomes a 400 at the credential DTO's validator in Slice 2, and is written
  here so the ladder doesn't lose it to a passing suite.

**`TDD-EXEC-m4-authentication-048`** · `BEHAVIOR-048` · Red `19ba5f3` → corrected `48a4006` → Green *(this commit)* · p1 · **Slice 1**
- **Red, as it finally failed:** `Failed: 1, Passed: 1, Total: 2` — `Our_hashes_do_not_declare_the_framework_default_cost`,
  `Expected: Not 100000 / Actual: 100000`. **Green:** focused **2/2**, full **`Failed: 0, Passed: 73, Skipped: 0`**,
  0 warnings, whole output read (`bin/`+`obj/` deleted first).
- **This behaviour took three commits because the first Red asserted a framework behaviour that does not exist.** It asked a
  default-configured `PasswordHasher` to verify our hash and expected `SuccessRehashNeeded` on a count mismatch. Probed:
  `declared350=350000 b0=1 defaultVerifier=Success at350Verifier=Success` — **the verifier takes the iteration count from
  the envelope, so a cost difference never raises the rehash signal in this format.** The observable had been invented from
  a belief about the framework rather than a look at it: the precise habit this record's integrity ledger is made of, caught
  by a 30-second measurement run instead of by a production incident.
- **A correction commit, not an amended Red.** `19ba5f3` stays in history with its false premise, because "the Red was wrong
  and here is the measurement that said so" is evidence and a silent rewrite would have been a cover-up of exactly the kind
  gap 10b taught this repo to name out loud.
- **The property forced a format coupling, and the guard buys it back.** -048's claim is not observable through the public
  API at all, so the test decodes `BinaryPrimitives.ReadUInt32BigEndian(envelope[5..9])` — with `Assert.Equal(1, envelope[0])`
  **before** it, so a layout change fails loudly instead of reading iterations out of the salt and passing on nonsense.
- **Two numbers, both measured today:** Identity's inherited default is **100,000** on this machine (the value the test
  asserts *against*), and one `Service.Hash` costs **~200 ms** at the newly declared **350,000** (focused run: 302 ms total
  for 2 tests). `-049`'s cost band therefore has real headroom and a number it can actually falsify.
- **Knock-on correction, in its own commit:** `-047`'s remarks justified absorbing `SuccessRehashNeeded` with a lockout
  scenario that measurement says cannot occur via iteration count. The absorption stays (the signal is real for compat
  formats, PRF and key-size changes) but the *stated reason* is replaced — **a comment that invents a threat to justify a
  judgement is worse than the judgement**, because the next reader will not re-test the threat, only trust the comment.

**`TDD-EXEC-m4-authentication-049`** · `BEHAVIOR-049` · no Red commit *(disclosed below)* — negative control instead · single test-only commit on `feat/m4-049-cost-band` *(read its SHA with `git log --oneline main..HEAD`; none written here — this record has carried a fabricated SHA before)* · p2 · **Slice 1**
- **Measured first, asserted second.** Spec §2.3's numbers were unrun when the plan was written; AC-4's own clause is
  "**no number is asserted in this record before that run**". Dedicated bench, 13 iterations of each, first observation
  **reported rather than hidden**:
  `hash n=12 (warm-up excluded: 273.5) min=247.6 p50=279.1 p95=314.8 max=336.0` ·
  `verify n=12 (warm-up excluded: 316.2) min=251.5 p50=271.2 p95=312.2 max=330.3` — **both ratified targets pass as
  written** (314.8 < 1000; 271.2 ∈ 200–800) at the 350,000 iterations `-048` chose.
- **No Red to fabricate.** The assertion was derived from a measurement of the code that already exists, so it passes on
  arrival — same honest-no-Red case as `-043`/`-045`. Its substitute is the **negative control**: `MaxAcceptableMs`
  temporarily `1000 → 1` produced `Failed: 1, Passed: 0` with `Assert.InRange() Failure`, file restored byte-identical
  (`diff -q`). A cost guard nobody has watched fail is a comment with a green checkmark.
- **This behaviour's real finding is that it disagreed with itself, and the test was right.** The first draft's doc comment
  claimed the warm-up "was not slower than the steady state" — true of the bench (273.5 ms, inside the range). **In the
  full suite the first `Hash` measured 1108.9 ms and failed the 1000 ms ceiling.** Both statements were true of their own
  run; only the second one matters, because `xunit` runs test collections in parallel: **an in-suite timing assertion
  measures the scheduler, not the hasher.** Fixed by a discarded warm-up call (now load-bearing, not precautionary) and by
  splitting the jobs: the percentile claim belongs to the dedicated bench, the committed guard is deliberately coarse
  (150–3000 ms per observation) so it catches a 10× change in either direction without ever flaking on contention.
- **A lower bound with a purpose, stated:** the regression this suite would otherwise never see is cost going **down** — an
  iteration count quietly returned to a demo-friendly value. `Verify` returning `true` says nothing about what it cost.
  Measured floor 247.6 ms; 150 ms is far below it and still catches a 10× slip (~25 ms).
- **AC-4 checked on this evidence — and the check is annotated, not bare**: percentile figures come from the bench, the
  committed guard asserts a coarser envelope. Recorded so nobody later reads 3000 ms as the ratified target.
- **⚠️ Post-execution amendment (same day, discovered while executing `-052`): the absolute band flaked for real, and the
  ceiling was NOT moved.** Full-suite run: `Assert.InRange() Failure: Range: (200 - 800) Actual: 1012.4146` — the same test
  passes in 302 ms alone. PR #40 had promised the alternative in writing ("the correct response is not to raise the ceiling
  again — it's to conclude latency cannot be asserted in a parallel suite at all"), so the assertion became a **ratio
  against a reference `PasswordHasher` at the same declared iteration count, timed adjacently** (within 2×; contention
  multiplies both and cancels). A demo-iteration swap still fails it at ratio ≈ 0.003; "the whole machine got slower" no
  longer does, which was never a product defect. A second fact pins that both sides are the same cost function — a
  reference hasher verifies our envelope as `Success`, impossible unless `-048`'s count is the one written.
  **The coarse 150–3000 ms envelope described above is superseded**; the absolute percentiles live in this bench and in
  spec §2.3's annotation, not in CI. Evidence: `Failed: 0, Passed: 103, Skipped: 0` on **two consecutive full runs.**
- **Two process notes.** (1) `Assert.InRange(collection, lo, hi)` does not exist — `Assert.InRange<T>(T,T,T)` — so the first
  run died with **CS0411**; the compiler caught a test that would have asserted nothing. (2) My "run it twice more" loop
  printed nothing for both repeats: the grep pattern used single spaces against padded output. **Same rule-8 mistake, caught
  by the absence of the expected line.** What is actually attested is `Failed: 0, Passed: 74` on **two full-suite runs**
  (18 s), not two focused repeats.

**`TDD-EXEC-m4-authentication-050`** · `BEHAVIOR-050` — guard half (**seed half recorded directly below; it landed the same day**) · Red `63a70e0` → Green *(this commit)* · p0 · **Slice 1**
- **Red:** `Failed: 3, Passed: 1` — three Production boots that must refuse all **succeeded** (`Assert.Throws() Failure: No
  exception was thrown`), while *Development boots with no credentials* passed on arrival. **A Red where all four fail would
  have meant the test was wrong about the world, not the code.**
- **Green:** focused **4/4**, full **`Failed: 0, Passed: 78, Skipped: 0`** (15 s), 0 warnings, `bin/`+`obj/` deleted first.
- **What the first attempt exposed about my own assumption.** I gave the boot test a bogus connection string, reasoning
  "nothing touches the DB at startup". False: `Program.cs` runs `Database.Migrate()` before `app.Run()`, so every Production
  case failed with `NpgsqlException : Failed to connect to 127.0.0.1:1`. **A test that asserts "boot threw" cannot
  distinguish a working guard from a dead socket** — gap 12's lesson in mirror image, since this one would have gone green
  for an unrelated reason. Fixed by joining `[PostgresCollection]` and asserting exception **type and message**.
- **Design decision the test earned:** the guard runs **before** the migration. "Refuse to boot" should mean the process
  does no work on the way out — checking after `Migrate()` would leave a half-migrated database behind from a deployment
  that was never supposed to exist.
- **Fails closed by inheritance:** `IsProduction()` is true when `ASPNETCORE_ENVIRONMENT` is unset, so forgetting to
  configure an environment lands in the strict branch. **Not asserted** — `WebApplicationFactory` always sets an
  environment, so the real default is untestable in-process; recorded as reasoning, not as a passing test.
- **`AC-15`'s own grep caught my code before I ran it:** the sentinel was `PlaceholderPassword = "change-me-before-deploy"`,
  which matches AC-15's `password\s*=\s*\"` pattern *while being the opposite of a secret* — a placeholder that must never
  ship. The name changed (`PlaceholderSentinel`), not the check. **Negative control on the gate itself:** restoring the old
  name produced **1 hit**; current state **0 hits**, file restored byte-identical. AC-15's dependency count is also
  unchanged (npm deps still 3, API `PackageReference`s still 5 — no new package, `dotnet ef` is not installed and none was
  added). **AC-15 stays unchecked**: its rationale is a seeded account, and the seed does not exist yet — flipping it now
  would let a partially-true gate read as verified.
- **Two of my own tool mistakes, both caught by reading:** the `using` I scripted landed *after* the line
  `using (var scope = …)` — because "starts with `using `" matches a `using` **statement** as well as a directive — giving
  CS1001; and the commit message for the Red initially claimed "no exception was thrown" from a grep that printed nothing,
  so the claim was verified against the log before it was allowed to stand.
- **Deferred half, stated as a gap not a choice:** "seeded user comes from configuration" needs the `users` table (no
  migration exists) and `dotnet ef` is not installed here. Spec §4.2 orders the guard *before* the seed, so this lands now;
  **`-050` is not complete and is not recorded as complete.**
  **→ Resolved:** the migration and the seed are executed in the next block, and `dotnet ef` turned out to need only a local tool manifest — plus a discovery that
  `Microsoft.EntityFrameworkCore.Design` was *already* referenced, so the PR's warning about "5 → 6 PackageReferences" was wrong and nothing moved.

**`TDD-EXEC-m4-authentication-050b`** · `BEHAVIOR-050` **seed half** — completes `-050` · Red `30da057` → Green *(this commit)* · p0 · **Slice 1**
- **Red:** `Failed: 4, Passed: 0` — all four on `42P01: relation "users" does not exist`. Disclosed there and again here:
  **uniform failure reasons mean one of those four asserted nothing** — the "no user is created when credentials are unset"
  case asserts an absence, and an absence holds trivially with no table. Forward-looking regression cover, not a Red.
- **Green:** focused **6/6**, full **`Failed: 0, Passed: 84, Skipped: 0`** (16 s), 0 warnings. Seeding runs
  **after `Migrate()` and before `app.Run()`**, through `BootstrapUserSeed.SeedAsync`, using `-047`'s real
  `IPasswordService` — the first production caller the seam ever had, which is why `PasswordService` is registered in
  `Program.cs` *now* and not three behaviours ago.
- **The load-bearing assertion is not "a row exists."** It is *"the stored value verifies against the application's own
  hasher for the configured password and for no other"* — that single check rules out the cheap ways to satisfy the weak
  claim (hard-coded email, plaintext password, a row nobody can authenticate against) — plus idempotency in **both**
  directions: one row after a second boot, **and** the original hash untouched when the second boot was configured with a
  different password. A seed that re-applies configuration on every restart silently undoes a legitimate password change.
- **Schema, and the probe that saved me from a wrong conclusion.** The mapping declares `email` as `citext` with a unique
  index, per spec §3 line 243. Two follow-up assertions then **failed**, and the obvious reading was "citext isn't
  working, accounts differing only by case will duplicate." A direct catalog probe said otherwise:
  `coltype=citext | exact=1 | mixed=1 | ext=1` — **the schema was right and both of my queries were wrong**: an inlined
  literal compares case-insensitively while a *typed text parameter* does not resolve the same way (fixed with
  `$1::citext`), and `pg_typeof(...) FROM users LIMIT 0` returns no rows at all, so the fallback reported citext as
  `information_schema`'s "USER-DEFINED" — technically true and useless as an assertion. **Had I trusted the failure
  instead of measuring the claim, I'd have "fixed" a correct schema.**
- **`-049`'s lesson, arriving a behaviour later:** a `[Fact]` that throws on purpose is still in the assembly until it is
  deleted, and the full suite ran with my probe file present because the `rm` shared a shell line with the `dotnet test`
  that had already compiled it. **The run reported `Failed: 1, Passed: 84, Total: 85` and the only failing test was mine,
  deliberately.** Reading *which* test failed is what turned an apparent defect into a note about my own cleanup order;
  84/84 is from the clean rerun after deletion, verified with `ls`.
- **Tooling decision executed as approved-by-merge (option (a)):** `dotnet-ef 10.0.12` in a new **`api/dotnet-tools.json`**
  local manifest — no `PackageReference` added. Verified rather than assumed: **npm runtime deps 3, API
  `PackageReference`s 5 (unchanged), credential-literal grep 0 hits.** The PR's claim that this would move the API to 6
  references was false — `Microsoft.EntityFrameworkCore.Design` was already referenced — which is the third time this
  session a number I quoted before looking turned out to be wrong, and is recorded as such rather than quietly corrected.
- **`AC-15` flipped on this evidence** — no new runtime dependency, no credential literal in source, boot guard live, and
  the seeded account it describes now exists. It had been left unchecked in the guard-half commit precisely because its
  rationale was an account that did not exist yet.

**`TDD-EXEC-m4-authentication-051`** · `BEHAVIOR-051` · Red `13d003d` → Green *(this commit)* · p0 · **Slice 2 begins**
- **Red:** `Failed: 4, Passed: 0` — `Expected: NoContent / Actual: NotFound` (route absent), `no Set-Cookie header`, and the
  DB-link test with no cookie to correlate. Weakest Red shape (like `-046`), named as such; three of the four assertions
  still bite after Green because they pin attribute strings and a row, not a status code.
- **Green:** focused **4/4**, full **`Failed: 0, Passed: 88, Skipped: 0`** (18 s), 0 warnings. `sessions` table via
  tool-generated `AddSessionsTable` (FK `ON DELETE CASCADE`, index on `expires_at`), `AuthCatalog.MapPost("/api/auth/login")`,
  `Problems` gained `unauthorized` (DECISION-005's ninth variant) and an optional validation title.
- **`AC-2`'s gate is a curl against a running server, so it was run — and the first run measured the wrong machine's process.**
  `dotnet run` applies `launchSettings.json`, which **overrides `ASPNETCORE_URLS`**: my app logged `Now listening on:
  http://localhost:5039` while **a 15-hour-old API binary from an earlier session (PID 636600) owned :5080**. Every curl
  returned `404 Not Found` from *that* pre-M4 server, and the log's own `Seeded bootstrap user` line — from my process —
  made it look like the new code was serving and refusing the route. **A 404 I could easily have reported as "AC-2 fails",
  or waved away as "the tests pass anyway".** Fixed by killing the stale PID explicitly (never `pkill -f`, per the hygiene
  rule) and passing `--no-launch-profile`. Same family as `-049`'s parallel-suite timing: **the measurement was of the
  wrong target, and nothing in the output said so.**
- **AC-2, verified on the wire:**
  `HTTP/1.1 204 No Content` · `Set-Cookie: __Host-JTSession=081ea7a0-…; Secure; HttpOnly; SameSite=Lax; Path=/` — **no
  `Domain=`, no `Expires=`/`Max-Age=`**, and `SELECT count(*) FROM sessions` → **1** in the *real* dev database. Empty body
  → `400` with `code: validation`; wrong password → `401 unauthorized`.
- **Unplanned evidence for `-050`'s non-overwrite rule:** the second boot against the persistent dev DB logged no seed line
  at all, because the account already existed — so the configured password was *not* re-applied, verified across a real
  process restart rather than only inside a Testcontainers lifecycle.
- **Why login works but nothing is protected yet:** `-051` deliberately ships **no** gate on `/api/applications*` (AC-1
  stays open). Landing global 401 with the credential route would have turned M3's 65 tests red for reasons unrelated to
  the change, and you need a working login before you can write the test that says "this route now requires it".
- **`LoginRequest(string?, string?)` is nullable on purpose**, and the reason is DECISION-m3-backend-api-004: with
  `required` non-nullable members, `{}` dies in the JSON binder and the framework answers with a problem document that
  carries **no `code`** — an envelope that looks machine-readable but isn't. Missing values are now a validation finding
  this handler controls, which is also why the empty-body test is here and not parked with `-053`.

**`TDD-EXEC-m4-authentication-055`** · `BEHAVIOR-055` *(executed out of order — see §5's amendment)* · Red `6a9af32` → Green *(this commit)* · p0 · **Slice 2's keystone**
- **Red:** `Failed: 9, Passed: 0` — `Expected: Unauthorized` on all six methods plus the stream. **AC-1 was simply false
  before this commit, and nothing in the suite had ever said so** — M3's 65 tests all asserted an unprotected surface.
- **Green:** focused **9/9**, full **`Failed: 0, Passed: 97, Skipped: 0`** (18 s), 0 warnings. **M3's 65 tests pass under
  the gate with `Skipped: 0`, which is `-058`'s claim observed early** — `-058` still gets its own behaviour, because
  "it happened to pass today" is not the same as "a test asserts it".
- **AC-1's own gate, run as written** (`curl … -w '%{http_code}'` against a live server, port ownership verified before
  and after — see `-051`'s record for why that is no longer assumed):
  `GET /api/applications` → **401** · `GET …/{id}` → **401** · `GET …/events` → **401** · `POST` → **401** · `PUT` → **401** ·
  `DELETE` → **401** · `POST /api/auth/login` → **204** · `OPTIONS` preflight with `Origin:` → **204**.
- **Why a path rule rather than `RequireAuthorization()` per endpoint** (three reasons, in `SessionGate`'s remarks): the
  stream is exactly the route a per-endpoint declaration misses (`-046`'s history); `RequireAuthorization()` **throws at
  startup** without a registered authentication scheme, which would drag in the Identity handler plumbing `DECISION-002`
  rejected; and "is this route public?" is answerable from one file instead of N call sites that each claim to have opted
  in. **Cost stated, not hidden:** a prefix rule also catches a hypothetical `/api/applications-public`, so the match is
  `StartsWithSegments` (segment-wise, case-insensitive like routing) rather than `string.StartsWith`.
- **Preflight must not be gated.** A browser cannot attach cookies to an `OPTIONS`, so gating it breaks every cross-origin
  write with a 401 a client reads as "server down". `UseSessionGate` is installed **after** `UseCors` — real preflights
  short-circuit there — plus an explicit `IsOptions` pass-through for requests with no `Origin`. Asserted above.
- **`Guid.TryParse` before the database**, because the cookie value is attacker-controlled text on every request; feeding
  it to Npgsql unparsed is how a gate becomes a 500 generator — gap 12's class at the widest input surface M4 has. Asserted
  as `A_garbage_session_value_is_401_rather_than_500`.
- **The stream assertion is deliberately awkward**: `GetAsync(..., ResponseHeadersRead)` plus `DoesNotContain
  "text/event-stream"`, because SSE commits headers eagerly — a gate applied after the first write yields **200 + a
  stream that then goes quiet**, which a bare status check can read as success.
- **`UtcNow` here, `TimeProvider` deferred honestly.** `-066` is the behaviour that needs an injectable clock and it is the
  one that will decide whether the seam is acceptable in production; standing up a seam now and leaving one caller unswept
  is the half-done version of the same idea. Recorded as the known debt it is.
- **Three of my own tool mistakes, all caught before they shipped**, and worth listing because they are the mundane kind:
  a Python heredoc with mismatched quote delimiters aborted with **none** of its edits applied (the `&& echo WIRED` guard
  is the only reason that didn't become a commit whose message described changes it didn't contain — see rule 9's entry);
  chained `.UseSetting(...)` calls appended **after** the terminating `;`, producing CS1519/CS1001/CS1031; and a
  `CreateIndependentHost()` that became `Task<HttpClient>` at one call site needing `await`. Also: the branch I was on was
  named `-052` while delivering `-055`, so it was renamed **before** the first commit rather than explained after.
- **Consequence for local development, stated so nobody discovers it by surprise:** the frontend's data views are now
  **401 until `-061`'s login screen ships**. That is the intended order — the gate cannot be verified before there is a
  credential path — but `npm run dev` against this API is a broken app today, and the fixture authenticates so tests don't
  show it.

**`TDD-EXEC-m4-authentication-052`** · `BEHAVIOR-052` · Red `4da189e` → Green *(this commit)* · p0 · **Slice 2**
- **Red:** `Failed: 3, Passed: 2` — old cookie answered `OK` where `Unauthorized` was required, and the DB showed
  `revoked=0 live=2`. **The two that passed are the point:** `-051` already issues a fresh `Guid` per login, and *that is
  precisely the property session fixation survives*. Rotating a value nobody invalidates is theatre, and this Red is the
  difference between the two.
- **Green:** focused **5/5**, full **`Failed: 0, Passed: 103, Skipped: 0`** on **two consecutive runs**, 0 warnings.
  Rotation is nine lines in the login handler: the session id the client *arrived* with gets `revoked_at` set, filtered on
  `RevokedAt == null`, then a new row is inserted — same request, same unit of work.
- **Why at the credential boundary and not in the gate:** fixation is defeated only if the superseded id dies in the same
  breath that its replacement is created. Revoked in the gate, a stolen id survives until someone's next request happens to
  be a read; revoked at logout, it survives forever.
- **Revoked rather than deleted**, so the audit question stays answerable: *when did this session stop working, and was
  that logout, expiry, or a second login?* Asserted as three counts (`total=2`, `revoked=1`, `live=1`) precisely because
  the cheap wrong implementations differ and both are wrong — leave it valid (this Red) or drop the row (unauditable).
- **Positive control included:** `the rotated session is still accepted where the old one is refused`. Without an `OK` for
  the new cookie in the same request shape, "old cookie gets 401" is equally satisfied by a server that has stopped
  accepting anything. Same endpoint, one header character different, opposite verdicts.
- **Cookie replayed by hand, no `CookieContainer`** — whether a held cookie is honoured after re-login *is* the policy under
  test, and a jar would decide it silently on the client's behalf.
- **`-049`'s guard broke while this behaviour was being verified**, and its own commit is separate: see the amendment above.
  Summary — median 1012.4 ms against an 800 ms band under a now-102-test suite; the ceiling was not raised, the assertion
  became a ratio.
- **Two of my own mistakes, both in the test rather than the product:** `count(*)::int FILTER (WHERE …)` is invalid SQL
  (`42601`) — `FILTER` precedes the cast, so the counts are `(count(*) FILTER (…))::int`; and `UseEnvironment` is an
  extension method in `Microsoft.AspNetCore.Hosting`, which fully qualifying the parameter's type did not bring into scope
  (CS1061). Also `WebApplicationFactory` needed `Mvc.Testing`.
- **`AC-5` checked.** Its wording was the test design, not a suggestion: *"a pre-login cookie is invalid after, and the test
  proves it by re-using the old value."*

---

`Pending` for Slices 1 (rest)–5 — filled at execution. Rules carried forward: print the AC ID lists and assert `checked + open == 17` (M3 had
four checkbox slips where scripted edits hit prose and never the prefix); regenerate §3A of STATE **whole** at each
boundary; read spec lines **without truncation** before asserting anything about them.

## 7. Risks Handed Forward

**M5's gates, written here so deployment cannot arrive with them still in prose:** login rate limiting/lockout · CSP ·
`Secure`-only enforcement across environments · **cross-instance SSE fan-out via PG `NOTIFY`** (grill Q16 — sticky
sessions pin *readers*, not *writes*) · Argon2id · multi-user/tenancy if `ASSUMPTION-002` ever changes.

## 8. State Transition Table

| Previous State | New State | Timestamp | Actor | Reason | Supporting Evidence |
| :--- | :--- | :--- | :--- | :--- | :--- |
| — (none) | `planned` | 2026-09-12 04:20 UTC | Assistant (`pk:tasks`) | Decomposed from the approved M4 spec (PR #34, `fa4ed7d`) | This record; spec §2/§6 |

**Decision provenance — recorded honestly, because the merge happened before the recording.**
`DECISION-001` **cookie** · `002` **two tables** · `003` **PBKDF2, cost in envelope, Argon2 deferred** ·
`004` **`owner_id` + client ids retained, 404-not-403** · `005` **ninth variant `unauthorized`** ·
`006` **`SameSite=Lax` + antiforgery header**.
All six are **the spec's recommendations, approved by the merge of PR #34**, not by ticked boxes — **spec §7's eight
checkboxes remain physically unchecked in `main` (`fa4ed7d`), and six of them are owner-only.** The two that are not:
the **grilling** item is agent work and **not yet done** (it precedes Slice 1), and the **"every §2 command was run"**
item is **partly unmeetable at spec stage** — several commands target behaviour that does not exist yet, so it is
re-scoped to *"every target carries a command"* now and *"every command was run"* at the §6 gate. **Stating that
distinction is the gap 10b lesson applied to my own checklist rather than waiting to discover it later.**
