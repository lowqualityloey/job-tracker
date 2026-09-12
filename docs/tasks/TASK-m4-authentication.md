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
  · *(partly verified 2026-09-12 by `-053`: message-indistinguishability proven and verify cost equalised **structurally**; medians 282 ms vs 310 ms reported. **Left open for the 5 ms bound's restrike-or-restate decision** — see §6.)*
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

- [x] **AC-6** — **Logout revokes server-side.**  *(verified 2026-09-12 by `-054`: `revoked_at` read from a real PostgreSQL with hand-written SQL, cookie dead on the wire `200→204→401`, scoping proven against a second live session; `psql` client absent on this machine — see §6's deviation note)* `sessions.revoked_at` set, verified by `psql`; the old cookie then gets
  `401`. Clearing the browser cookie is not the control.

- [x] **AC-7** — **Ownership: user B cannot see or touch A's rows, and gets `404` — never `403`.**  *(verified 2026-09-12 by `-056`: 404 on read/write/delete of another account's row — including with a correct `If-Match`, which would otherwise answer 409 and leak existence — and the two lists are disjoint; the identical `not-found` envelope is compared member by member)*
  · Two seeded users in the test fixture only; `GET`/`PUT`/`DELETE` on A's id as B → **`404`**.
  · *403 is an oracle* — it confirms the id exists.

- [x] **AC-8** — **Gap 12 fixed: a malformed `id` is a `400` validation problem, not a `500`.**  *(verified 2026-09-12 by `-057`: four malformed-id shapes answer `400` + `code: validation` + `"#/id"` on a live server, the valid body still `201`, and SQL confirms no row was written; malformed **route** ids stay `404` by deliberate asymmetry)*
  · `curl -s -X POST … -d '{"id":"not-a-guid",…}'` → `400` + `errors[].pointer: "/id"` + `code: 'validation'`.
  · **Red is today's `500` on exactly this body** — the defect is already reproduced and recorded in the M3 test doc §21.

- [x] **AC-9** — **`owner_id` migration is expand–contract with the assertion before the constraint.**  *(verified 2026-09-12 by `-056`: expand/contract pair rehearsed against the **real dev DB with three rows and no account** — the contract raised naming the count, rolled back, and succeeded after the documented remedy; guard exposed as a testable statement, `owner_id` has no database default and refuses NULL with `23502`)*
  · `SELECT count(*) FROM applications WHERE owner_id IS NULL` → **0**, *then* `SET NOT NULL`; both migrations applied to
  a **non-empty** database in the test path, not just an empty one (M3's risk table flagged this).

- [x] **AC-10** — **The owner index is used.**  *(verified 2026-09-12 by `-059`: baseline measured first on a disposable container; the plan is a **Bitmap Heap Scan** through `applications_owner_id_idx`, not an Index Scan; the same query without the index falls back to a Seq Scan; and `idx_scan` moves on a real authenticated request. 7.8× at 100k/500 owners, 15% at 100k/2 — the index pays for accounts, not rows)* `EXPLAIN` on the scoped list query asserts an index scan, not a seq scan.
  · *At 36 rows a full scan is free; at 100 k it is the incident.* This is the target most likely to be "verified" by
  reading the SQL and nodding.

- [x] **AC-11** — **A cross-site write without `X-CSRF-Token` is rejected — and the same write with it succeeds.**
  · Browser harness from a second origin (`jt-bridge` recipe in STATE §3A). **The positive control is mandatory:**
  without it a broken endpoint passes as a secured one.
  *(verified 2026-09-12 20:56 UTC by `-063`: **11/11** in real Chromium, `tests/browser/run-063.sh`, app origin
  `https://172.23.124.252:5443`, second origin `http://172.23.124.252:4179`. Both halves of the clause ran: a
  same-site tokenless write → `403` `code=antiforgery` **with no row written** — the scratch database counted
  `0` rows matching `CSRF-063-%`, because a status code says what was returned and only a read says what happened —
  and the same write carrying the issued token → `201`, visible in a fresh read.)*
  · **The clause is narrower than the verification, and that is the finding:** the cross-site case answers **`401`,
  never `403`** — the cookie never arrives, so the header is never consulted. Two status codes are the only thing that
  distinguishes "the platform withheld the credential" from "the antiforgery gate refused", which is precisely why the
  owner-approved split into two cases was necessary rather than tidy. Extra controls ran at the same seam: a
  wrong-valued header → `403` (the value is compared, not the presence), a token from a rotated-out session → `403`
  while the new session's token is served, and the same-origin write with `credentials: 'omit'` → `401`, so case 2's
  answer cannot be read as a dead endpoint.

- [x] **AC-12** — **SSE still delivers cross-tab while authenticated, in real Chromium.**
  · `docker exec … node /srv/httpCrossTab.mjs` → **7/7 with cookies**, no test-only shims.
  · **jsdom cannot attempt this** (no `EventSource`, no origin policy) — which is *why* it is an AC and not a nice-to-have.
  *(verified 2026-09-12 by `-064`: 7/7 twice at the path this clause names, over `https://172.23.124.252:5443` with
  `__Host-JTSession` asserted present and `secure=true` after a **form** login, and no product file changed; the same
  checks **fail 4 of them** under `E2E_SKIP_LOGIN=1`, which is the control that makes the 7/7 mean what the clause says.
  The clause's one PASS without a session — *nothing written to localStorage* — is recorded as a limitation of that
  check, not smoothed over)*

- [x] **AC-13** — **All 65 M3 API tests pass under auth with `Skipped: 0`.**  *(verified 2026-09-12 by `-058`: the 65 is now an executable manifest — 28 methods, 65 expanded cases, composition recorded — and proven to bite by three mutation probes; `Skipped: 0` is a standing assertion, not an observation)*
  · `dotnet test` → `Passed: 65, Failed: 0, Skipped: 0`. **Read `Skipped` as well as `Failed`** (M3 lost an hour to a
  filter matching only part of a `describe`).

- [x] **AC-14** — **The login route's bundle delta is < 3 kB against the Slice 0 baseline**, computed from build output,
  **configuration named**.
  *(verified 2026-09-13 at `b489f18` by `-065`: **1,242 B < 3,072 B**, configuration named below the number rather
  than beside it — `VITE_API_BASE_URL=http://172.23.124.252:5080`, `node v24.20.0`, `npm 11.19.0`, `vite 5.4.21`,
  `package.json` and `package-lock.json` verified byte-identical to the baseline commit before anything was built.)*
  · **The baseline was rebuilt, not quoted**, in the same run and from the same dependency tree
  (`git worktree add --detach /tmp/m4base-065 41b32af`, hard-linked `node_modules`): it reproduced Slice 0's
  **60,118 B flag-off / 61,368 B flag-on exactly — off by +0 / +0 bytes**, which is what turns
  *"the delta is 1,242 B"* from an arithmetic accident into a comparison. A remembered baseline subtracted from a
  fresh build measures two afternoons, not one milestone.
  · `git worktree add /tmp/m4base <Slice 0 sha> && VITE_API_BASE_URL=… npx vite build && cat dist/assets/*.js | gzip -c | wc -c`.
  · *Two M3 lessons fused:* the baseline is captured **in advance** (§2.8 told Slice 3 to record it and Slice 3 never
  did, making the delta uncomputable), and the **configuration is part of the number** (flag-off the adapter is
  tree-shaken and the delta is a flattering, meaningless 0 kB).

- [x] **AC-15** — **No new runtime dependency, and no credential literal in source.**  *(verified 2026-09-12: deps 3/5 unchanged, 0 credential literals, guard live, seed landed — see §6 `-050b`)*
  · `node -p "Object.keys(require('./package.json').dependencies).length"` → **3**; `grep -rniE "password\s*=\s*\"|secret\s*=\s*\"" api src` → **0 matches**.
  · Plus: **the app refuses to boot with default credentials in `Production`** — a startup guard, because a seeded
  account is the failure mode of a published one.

- [x] **AC-16** — **Credentialed CORS is explicit, not incidental.** Preflight echoes `Access-Control-Allow-Credentials: true` with the **exact** origin and never `*`. *(verified by `-067`'s Integration half, 2026-09-12 — `CorsCredentialsTests`: the pair on preflight AND on the 401 response, an unlisted origin gains nothing, and a `*` in `Cors:AllowedOrigins` now refuses to boot rather than silently matching nothing)*
  · *Why it exists:* `Program.cs:38` promised that adding credentials later would "break quietly rather than loudly," and it was right — browsers reject the wildcard+credentials pair and ASP.NET will not stop you configuring it. Grill **Q3**.
- [x] **AC-17** — **Sessions expire, and expired rows are pruned.** An idle window **and** a hard cap both end a session; expired/revoked rows are removed opportunistically at login. *(verified by `-066`, 2026-09-12 — `SessionExpiryTests`, 7 cases, no sleeps: the window slides on use and dies on silence; the cap ends a session that kept being used; login prunes expired and past-retention revoked rows and cannot touch a live one)*
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

**`TDD-EXEC-m4-authentication-053`** · `BEHAVIOR-053` · Red `242ea5e` → Green *(this commit)* · p0 · **Slice 2**
- **Red:** `Failed: 2, Passed: 3` — `Assert.Single (0 calls recorded)` on both structural tests. **The three that passed are
  the lesson:** identical status, identical body, no cookie, no crash — every message-level assertion this behaviour needs was
  already true, while the absent-email branch skipped PBKDF2 entirely. Spec §2.4 says it outright: *the classic tell isn't the
  message, it's the missing 400 ms.*
- **Green:** focused **5/5**, full **`Failed: 0, Passed: 108, Skipped: 0`** (22 s), 0 warnings. `user is null` now verifies
  `DummyCredential.Envelope` and returns; the present path verifies the real hash. One verify on every path, same declared cost.
- **The dummy envelope is generated at runtime, not pasted as a constant.** A literal base64 hash freezes whatever
  `IterationCount` was current when someone produced it; the next time that security parameter moves, the absent path would
  verify at the *old* cost and **the fix would silently re-create the oracle it exists to close**, with every message-level
  test still green. Deriving it through `PasswordService` makes equal cost structural. Lazy (`ExecutionAndPublication`) so
  ~300 ms lands on the first failed unknown-email login per process rather than on every boot — including Testcontainers
  hosts. Also noted: the lazy version makes that first request *slower* than a wrong-password response, an oracle pointing the
  other way, and one-time rather than per-probe.
- **⚠️ Row `-053` restated — "byte-identical in body" is not achievable in this application.** Discovered by the first
  version of the assertion failing with `"code":"unauthorized","traceId":"00-b97b516c5cfde…"`: **ASP.NET's default problem
  writer injects a per-request W3C trace id, so no two problem documents in this app are ever byte-identical.** A random
  correlation id carries no account information, and suppressing it globally would discard the id M3's SSE/problem debugging
  depends on for zero security gain. The committed claim is now precise instead of impossible: every member *except*
  `traceId` equal, `traceId` **present on both** paths (its absence on one side would itself be a tell), the two trace ids
  *different* (proving it is per-request, not a stable fingerprint), and **neither body contains either email in either
  direction.** The test-plan row is amended in place with the original wording visible.
- **Proof is structural because `-049` already proved timing can't be asserted here.** A counting `IPasswordService`
  substituted at the DI seam records the exact envelope each path handed the hasher, and the test decodes the declared
  iteration count from the envelope (byte 0 marker = `1`, big-endian uint32 at [5..9] — measured in `-048`). That catches the
  cheap dodge too: *"a verify happens"* is satisfiable by verifying a 1,000-iteration stub, which only moves the oracle.
- **Timing, reported and NOT asserted** (grill Q8; 25 samples per path against a live Kestrel on `127.0.0.1:5080`, port
  ownership verified before and after): absent email median **282 ms** (min 259, max 771) · real email + wrong password median
  **310 ms** (min 269, max 509) · valid login median **287 ms** (n=10). Residual gap **28 ms** with near-total distribution
  overlap. **For contrast, derived not measured:** the pre-fix difference was a whole verify, i.e. `≈271 ms` from `-049`'s
  bench — two orders of magnitude above this noise floor. **AC-3's original "within 5 ms" bound is still unmet at the median
  and remains the owner's restrike-or-restate decision; nothing in CI asserts it.**
- **AC-3 left OPEN deliberately.** Message-indistinguishability is now proven and cost-equalisation is proven structurally,
  but the AC's wording says *"or timing"* and the timing half has an unowned numeric claim attached to it. Same discipline as
  `-050`: a behaviour can land while its AC waits on a decision, and the record says which is which.
- **Six of my own errors, all in the test file, all fixed before commit:** CS9176 (`var x = [.. list]` has no target type
  for a collection expression), two missing usings (`System.Text.Json`; the `Mvc.Testing`/`TestHost` pair), a Python heredoc
  whose `\"` unescaped into bare quotes and produced invalid C# — **second recurrence of that exact trap this session** — a
  non-draining read helper that would have reported an earlier attempt's verify as a later one's (not cosmetic here: it is
  the difference between proving absence and proving nothing), and a leftover `wrongCalls[1 - 1]`.
- **Practice-task result pending from the human** (browser cookie jar over plain HTTP) is **not** a dependency of this
  behaviour; it gates `-063`/`-064`.

**`TDD-EXEC-m4-authentication-054`** · `BEHAVIOR-054` · Red `c602750` → Green *(this commit)* · p0 · **Slice 2**
- **Red:** `Failed: 6, Passed: 0` — four `Expected: NoContent/Unauthorized, Actual: NotFound` (no logout route exists), and
  `Revocation_is_scoped…` reported `Actual: OK` where `Unauthorized` was required, because nothing could revoke anything.
- **Green:** focused **6/6**; full **`Failed: 0, Passed: 114, Skipped: 0`** (32 s), 0 warnings.
- **AC-6's own gate, on the wire** (live Kestrel, port ownership verified before and after):
  `GET /api/applications` **200** → `POST /api/auth/logout` **204** → same cookie **401** →
  `POST /api/auth/logout` anonymously **401** → `GET /api/auth/session` **401**.
- **⚠️ `-055`'s gate under-covered the spec, and this increment fixes it.** Spec §4.3's *Auth* column marks
  `POST /api/auth/logout` **and** `GET /api/auth/session` as *authorized*, but `-055` protected only the
  `/api/applications` prefix — because `-055`'s own row says "every **data** route", and it satisfied that row exactly.
  **A behaviour can be delivered as written and still be short of the contract it came from.** The gate is now
  `IsProtected(path)`: data prefix, **or** the auth prefix minus `/api/auth/login`. The last line of the wire check above is
  the payoff: `GET /api/auth/session` answers `401` even though **no such endpoint exists yet** — the prefix rule covers the
  contract row a behaviour row nobody has written.
- **Why logout must not be anonymous-reachable, beyond the spec column:** an endpoint that answers `404` to one caller and
  `401` to another is itself a discriminator, and logout is the *one* gated route whose request an attacker can send carrying
  nobody's cookie.
- **Revocation is scoped to the presented session id — asserted twice over, on both ends.** `UPDATE sessions SET revoked_at =
  now() WHERE user_id = @me` passes every other test in this file (row revoked ✓ cookie dead ✓ 204 ✓) and is a stolen-cookie
  denial-of-service handle against a whole account, plus it collapses "log out here" into "log out everywhere", which spec
  lists as out of scope. So `Revocation_is_scoped_to_the_session_that_was_presented` uses **two independent clients** — not
  two logins from one, because `-052`'s rotation would correctly kill the first — and asserts both verdicts both ways.
- **`revoked_at` is not rewritten by a repeat logout** (`RevokedAt IS NULL` in the filter), the same policy `-052` chose for
  rotation: the column records when a session *stopped being valid*. Asserted by comparing `extract(epoch from revoked_at)`
  before and after a second call, so the check is to PostgreSQL's own microsecond precision with no .NET rounding in the
  middle.
- **Deviation from AC-6's literal wording, stated rather than glossed:** AC-6 says *verified by `psql`* and **this machine has
  no `psql` client** (`command -v psql` → absent). The revocation assertions read a real PostgreSQL 18.6 through Npgsql with
  hand-written SQL — the same database, the same query text, `psql` being a client and not a mechanism. AC-6 is checked on
  that reading; if the literal client is required, `apt install postgresql-client` and the exact statement is in
  `LogoutEndpointTests.ReadSession`.
- **The clearing `Set-Cookie` is asserted, and it is *courtesy*, not control** — AC-6: "clearing the browser cookie is not
  the control." Asserted because `__Host-` cookies can only be replaced by a header carrying the same constraints (no
  `Domain`, `Path=/`, `Secure`), so a malformed clearing header leaves the dead id in the jar. **Honest limit on the wire
  evidence: I piped `head -3`, so the 204's headers were truncated before `Set-Cookie` — the expiry attribute set is proven
  in-process, not on the wire.** Reporting a truncated assertion as a verified one is how `-051`'s gate measured a stranger.
- **`-066` now has three callers of one clock question** (`-050`'s `expires_at`, `-055`'s gate check, `-054`'s
  revoke-on-write) and still owns the `TimeProvider` seam.

**`TDD-EXEC-m4-authentication-056`** · `BEHAVIOR-056` (+ AC-9's guard) · schema `75f76e6` → Red `2996409` → Green *(this commit)* · p0 · **Slice 3**
- **Red, measured at `2996409` with the scoping stashed out of the tree:** `Failed: 6, Passed: 2`. All six behaviour tests
  fail (B reads, writes, deletes A's row; the lists cross-contaminate; ownership is forgeable); the two that pass are the
  schema pins, which *should* already hold because the migration landed one commit earlier. `git stash list` and
  `git status --porcelain` were both asserted before the run and after the pop — **an unisolated tree produces a Red that is
  a performance**, and the first attempt at this did exactly that: repo-root vs `api/`-relative pathspecs meant nothing was
  stashed, and the run reported 8/8 *passing* at what was supposed to be the Red.
- **Green:** focused **8/8**, full **`Failed: 0, Passed: 122, Skipped: 0`** (49 s), 0 warnings.
- **Honest note on order.** This increment was developed schema-first, so the Red landed *after* the column it depends on —
  correct dependency order, not the sequence I worked in. The stash-and-rerun above is what keeps the Red real rather than
  decorative: history now shows a commit where these eight tests exist and six of them fail, verified by execution rather
  than asserted by a message.
- **AC-9's non-empty rehearsal, on the real dev database** (the suite's own hosts migrate an *empty* `applications` table,
  which AC-9 names as insufficient): three rows, zero accounts → **expand** adds `owner_id NULL` + index, backfills to
  nothing, leaves all three unowned → **contract** raises
  `AC-9 guard: applications has 3 of rows with owner_id is null…` → the transaction rolls back, `is_nullable` still `YES`,
  migration history unchanged → seed an account, assign owners explicitly, re-run → **succeeds**, `nullable=NO`,
  `default=NONE`, FK present. Rehearsal rows deleted afterwards.
- **`FILTER` before the cast, `default` never:** EF generated `defaultValue: new Guid("00000000-…")` on the contract's
  `AlterColumn`. Removed, and pinned by a test, because that default *is* the "owned by nobody" state this behaviour exists
  to make unreachable — and it downgrades a clear `23502 not_null_violation` into an opaque `23503 FK violation`.
- **The 404-not-403 rule is implemented as one composite predicate, not as a permission check.** `a.Id == guid &&
  a.OwnerId == owner` makes a foreign row and a nonexistent row produce the identical `404 not-found` envelope (asserted
  member-by-member on `type`/`title`/`status`/`code`, since `-053` established byte equality is impossible with the
  framework's `traceId`).
- **`B_writing_A_s_row_gets_404_even_with_a_correct_if_match` is the test this file exists for.** `-056`'s row warns that
  `403` is an existence oracle. This app can also answer `409 Conflict` — through the concurrency token — so B supplying A's
  *real* revision proves existence without any 403 being sent. Scoping in the id lookup is what closes it; the oracle would
  have survived a review that only checked the 403 advice.
- **`Ownership_cannot_be_forged_from_the_request_body`:** a POST carrying `ownerId` is ignored (unknown JSON members are not
  this app's error surface) and the row is owned by whoever the gate authenticated, compared against the **actual** user id
  read from the database rather than "not empty" — `Guid.Empty` would pass a null-coalescing bug, which is precisely what
  EF wanted to make the default.
- **Three assertions corrected by running them instead of reasoning:** PUT answers **200 + body** (`Results.Ok(entity)`),
  not 204; a stale `If-Match` answers **409** (`Problems.Conflict`), not 412; and an open `NpgsqlDataReader` on a connection
  makes the next command on it throw `NpgsqlOperationInProgressException` (my plumbing, reported as a product failure until
  I read the stack). Also two compile errors (`JsonElement` is not `IDisposable`; a static helper calling an instance one)
  and the boot-order trap: inserting the second account before any host boots hits `42P01 relation "users" does not exist`,
  because **booting a host is what runs `Migrate()`**.
- **⚠️ GAP, named and unowned: `/api/applications/events` is not owner-scoped.** Read from the source, not assumed:
  `ApplicationEventBus` is one global `Channel<string>` of application ids and the stream writes
  `event: change\ndata: {"id":"<guid>"}` to **every** subscriber. So B sees *that* one of A's records changed and when, and
  gets valid ids to probe — not A's data, but an activity oracle across accounts, which is the class of thing `-056` exists
  to eliminate. **No ladder row owns it** (`-046` is M3's stream test, `-056` is the CRUD surface), and the payload is
  deliberately a "re-read" hint rather than a record, so the fix is small: one channel per owner, or filter by
  `OwnerId == owner` at publish time. **Proposed as `-068`** rather than folded in here, because a fix nobody was asked for
  is a second change riding inside this one, and because the human's pending coverage audit should be the thing that decides
  whether it's p0.
- **AC-7 and AC-9 checked.** 8 verified + 9 open = 17, asserted.

**`TDD-EXEC-m4-authentication-057`** · `BEHAVIOR-057` (+ AC-8) · Red `a4baeea` → Green `3421e53` · p0 · **Slice 3**
- **Red, measured:** `Failed: 9, Passed: 2, Skipped: 0, Total: 11` — six `[Theory]` rows all reporting `produced 500`, plus
  the POST case, the PUT case, and the no-row-written case. **Green:** focused **11/11**, full
  **`Failed: 0, Passed: 133, Skipped: 0`** (44 s), 0 warnings. AC-8 checked → **9 verified + 8 open = 17**, asserted.
- **The premise was measured before the code, and it widened the scope.** `-057`'s row says *"Red is the 500 reproduced
  today"*, so the first act was a curl sweep on a live Kestrel with an authenticated session (port ownership checked before
  and after). It found **`PUT` with a malformed *body* id** 500-ing — the same defect on a verb the row never mentions, which
  is now `-057`'s fourth test. Probing also confirmed what the row *didn't* need to fix: `GET`/`DELETE` with a malformed
  **route** id already answer `404` + `code: not-found`, because both handlers `Guid.TryParse` the segment.
- **`Guid` → `string?` was not the fix, and the run caught me treating it as one.** Focused result after the type change:
  **9/11**, the two survivors being `{"id":123}` and `{"id":{"kind":"uuid"}}`. System.Text.Json refuses to bind a number or
  an object to a `string` too, so widening a field fixes only the inputs that were *already* that type. The real fix is
  `AnyTokenToTextConverter` on the one member: `null` stays `null`, a string passes through, anything else is rendered to its
  raw JSON text and handed to the validator, which rejects it. **The class of the bug is "the framework parses before the app
  can" — the diagnosis was right the first time; the remedy was too narrow.**
- **AC-8's own gate, curl as the task doc writes it** (`POST -d '{"id":"not-a-guid",…}'` → `400` + `errors[].pointer: "/id"`),
  on a live server after Green: `not-a-guid`, `123`, `{"kind":"uuid"}`, and `""` all answer
  `400 | code:"validation" | "#/id"`, and a valid body still answers `201`. The row is not written: counted in SQL, because
  *"validate later and let the database complain"* is precisely a 400 that also inserted something.
- **Two things deliberately left asymmetric.** A malformed **route** id stays `404`: answering `400` there would report
  *"well-formed but not yours"*, which is `-056`'s existence oracle reopened through a status code. Body-malformed means *"the
  value you want stored is not a GUID"*; route-malformed means *"nothing here"* — pinned by a test so the next reader doesn't
  unify them. And the pointer is **`#/id`**, matching the measured house shape (a bad status already answers `#/status`),
  where `-057`'s row abbreviates it as `/id`; asserted against the convention rather than the abbreviation, and the difference
  is recorded here rather than silently resolved.
- **⚠️ SIBLINGS FOUND BY PROBING, NOT FIXED — proposed `-069`.** Wrong-typed *values* in the other members still die in the
  binder on the same server, minutes later: `companyName: 42` → **500, no `code`**; `notes: [1,2]` → **500**; `status: 5` →
  **500**; and the valid control → **201**, so the probe was live. Root cause is identical (reference-type members tolerate
  `null` but not a wrong token type), the blast radius is every member of every wire record, and **no ladder row owns it**.
  Left alone on purpose: `-057` names the id, and a sweep across the contract is the human's call to rate, not a change that
  should ride inside this commit. Making `Id` a `JsonElement`-style catch-all for *all* members would also have stored `"42"`
  as a company name, which is a different product.
- **Disclosure — a commit message written from intent, corrected by the tree.** The first Red commit (`1e781f2`) carried the
  text *"`Failed: 9, Passed: 2` … all six theory rows are now genuine failures"* while the tree said **8/3**, because the
  scripted edit that was supposed to fix `BodyWithId` had died on a Python `SyntaxError` (C# `"""` inside a heredoc —
  a recurring mistake, and this is the third or fourth time) and I wrote the message from what I expected the edit to have
  done. Two independent tells: **six inputs, five failures** (the object-shaped row was sending a body with no `companyName`
  at all, so it "passed" as a 400 for an unrelated reason — a self-defeating assertion hiding in the pass column), and a
  `CS1002` on the retry (`; expected`) where the expression-body rewrite had eaten its terminator along with the `};` line.
  Fixed via a script file instead of a heredoc, re-measured at **9/2**, and the commit amended to `a4baeea` so the message now
  describes its own diff. Root cause is the one already named three times in this milestone: **writing from intent instead of
  measurement.**
- **A second quiet failure worth recording:** the first post-Green curl probe reported `401` for *every* shape including the
  control — the cookie was empty because the server hadn't finished booting when the login ran, and a probe that never had a
  session measures nothing while still printing a tidy table. The re-run waits on `Now listening`, prints the login status
  and the cookie, and **refuses to print the table** if the cookie is absent.

**`TDD-EXEC-m4-authentication-058`** · `BEHAVIOR-058` (+ AC-13) · `a996d74` · p0 · **Slice 3**
- **Not a Red/Green row, and the substitute is the evidence that matters.** AC-13 asserts a property that already holds — the
  harness has authenticated through `POST /api/auth/login` since `-050`/`-055`, and every run since has reported
  `Skipped: 0`. Writing a "failing test" against a bug that does not exist would be theatre, so this increment was falsified by
  **mutation instead**: break the tree three ways, confirm the guard bites, revert, confirm green. Measured:

  | mutation | guard that failed | message |
  | :--- | :--- | :--- |
  | `Skip = "…"` on one M3 test | `Nothing_in_the_API_test_assembly_is_skipped_or_disabled` | `1 test(s) carry a Skip reason…` |
  | rename one M3 method | manifest test | `AC-13 regression net: 1 M3 test method(s) are gone… A_rejected_write_publishes_nothing` |
  | drop one `[InlineData]` row | case-count test | **`AC-13 counts 65 M3 assertions; the assembly now yields 64`** |

  Each probe was reverted with `git checkout --` and the net re-run to 4/4. **A regression net nobody has proven catches
  anything is a comment with an `Assert` in it.**
- **AC-13's "65" resolves exactly — but only once you know its composition.** From `dotnet test --list-tests` (the runner is
  the only thing that knows how data rows expand; my first attempt counted attributes with a regex and reported
  `ValidationContractTests` as **0** tests while the runner listed 36):

  `ValidationContractTests` 1 method/**36** · `ApplicationsCommandTests` 6/**7** · `ApplicationsQueryTests` 5/**6** ·
  `CorsContractTests` 5/**5** · `EventStreamTests` 4/**4** · `PostgresHarnessTests` 4/**4** · `ApplicationConstraintTests` 3/**3**
  = **28 methods, 65 cases**. `CorsContractTests` is M3's (`BEHAVIOR-m3-backend-api-042`) and `PostgresHarnessTests` is M3's
  harness asserting its own container; drop either and the total is 60 or 56, so the *headline* number is less informative
  than the breakdown — which is why the manifest is class-and-method, not an integer.
- **Cases, not methods, because method-level equality has exactly the hole this row exists for.** Deleting one row from a
  36-row table removes 35 assertions while every method name stays put. `CaseCount` therefore reads the data attributes — one
  per `InlineData`, a `MemberData` member enumerated on its declaring type — and asserts the total is 65.
- **Two framework findings that taught more than a green run would have.** `TestServer`'s `ClientHandler` throws
  `NotSupportedException` on `HttpClient.Send` — *"risk of threadpool exhaustion when running multiple tests in parallel"* —
  so the first draft died on its own synchronous convenience, not on the product. And `MemberDataAttribute.Type` /
  `ClassDataAttribute.ClassType` are **not public** in this xUnit version, so a case counter cannot resolve a member declared
  elsewhere; M3 never does that, and where the counter cannot measure it, it now **throws** rather than returning `1`.
  A counter that undercounts would make AC-13 look wrong in the safe direction — the worst possible failure mode for a net.
- **Stated limit, not implied coverage:** assertion *weakening* (`Assert.Equal(404, x)` → `Assert.True(status < 500)`) keeps
  every name and every count and passes all four guards; that is a review problem. What the fourth guard does close is
  **vacuous passing** — it drives `GET`/`POST`/`PUT`/`DELETE` with no cookie (`401` + `code: unauthorized`) and with the
  fixture's login-derived session (`200`), so if the harness ever went back to injecting a session row by SQL, AC-13 cannot
  read green while lying about what "under auth" meant.
- **Suite:** focused **4/4**, full **`Failed: 0, Passed: 137, Skipped: 0`**, 0 warnings. AC-13 checked →
  **10 verified + 7 open = 17**, asserted.

**`TDD-EXEC-m4-authentication-059`** · `BEHAVIOR-059` (+ AC-10) · `acf34a5` · p1 · **Slice 3** (seam **DB**, no product change)
- **AC-10 predicted this row's failure mode and I hit both variants of it.** The AC says this is *"the target most likely to be
  'verified' by reading the SQL and nodding"*. Reading the SQL would also have produced the **wrong** assertion: AC-10's wording
  is "asserts an index scan", and the plan is a **Bitmap Heap Scan** — the predicate matches dozens of rows across many pages
  and the select list needs every column, so Postgres prefers a bitmap. `Assert.Equal("Index Scan", node)` would have reported
  the index as unused *while it was being used*. The assertion is on a node whose `Index Name` is ours, which covers both shapes.
- **Baseline measured before any assertion existed** (pk:perf order), on a disposable `postgres:18.6` container — created via
  `dotnet run`-free `dotnet ef database update`, seeded with `generate_series`, and `docker rm`'d afterwards; the dev database
  was never touched:

  | rows | owners | matched | WITH index | WITHOUT index |
  | ---: | ---: | ---: | :--- | :--- |
  | 1,000 | 2 | 500 | Seq Scan · 0.17 ms | Seq Scan · 0.17 ms |
  | 20,000 | 2 | 10,000 | Bitmap Heap Scan · 2.25 ms | Seq Scan · 3.18 ms |
  | 100,000 | 2 | 50,000 | Bitmap Heap Scan · 11.69 ms | Seq Scan · 13.77 ms |
  | **100,000** | **500** | 200 | **Bitmap Heap Scan · 1.48 ms** | **Seq Scan · 11.61 ms** |
  | 20,000 | 500 | 40 | Bitmap Heap Scan · 0.41 ms | Seq Scan · 2.04 ms |

  **The index earns its keep on account count, not row count** — 15 % at two owners, **7.8×** at five hundred. `-059`'s row
  reads "at 100 k it is the incident"; the accurate statement is "at 100 k *and* a real user population", which is exactly what
  M4 introduces. And at low counts the planner is *right* to ignore the index, so "whatever the fixture happens to have" would
  not have weakened the file — it would have asserted a false premise. Hence an explicit 20,000 / 500 seed.
- **No Red available, so mutation again** (`-058`'s pattern). **Valid probe:** owners 500 → 2 at 20,000 rows → **all four tests
  failed** (index node gone; counter stayed 0 for the whole two-second window; scoped cost stopped beating whole-table cost) —
  finding above reproduced inside the suite. **Invalid probe, reported as invalid:** rows 20,000 → 36 → **all four passed**,
  because the filler insert (ten rows for every ownerless user) became most of the dataset, so "36 rows" was really ≈4,700.
  It proved nothing about the seed being load-bearing; the premise it reached for is carried by the baseline table instead,
  where the count genuinely was 1,000. **A probe that agrees with you is not evidence.**
- **The restore itself was a lesson:** `git checkout -- <file>` cannot restore an **untracked** file, so the mutation sat in the
  tree (`SeededRows = 36`) until a later grep caught it, and one in-between "4/4 green" run had been measuring the filler
  dataset. The constants are now restored, asserted in the final run (141/141), and the file says so at the line.
- **Three framework lessons, each earned by a failure rather than by documentation:**
  * EF 10's `ToQueryString()` is a *debug rendering*, not the wire text — it emits `-- @owner='guid'` as a leading comment and
    leaves `@owner` live in the statement, so `EXPLAIN` died with `42703: column "owner" does not exist`. Comment lines are
    stripped and the placeholder bound as a real parameter, which incidentally makes the test closer to the endpoint: a
    parameterised query planned against a parameter value.
  * `EXPLAIN (ANALYZE)` can report `Actual Rows` as a **fractional** number (`40.5`) when parallel workers split the count;
    `JsonElement.GetInt64()` throws `FormatException`. Read it as a double.
  * `idx_scan` is delivered to the cumulative statistics system **asynchronously**, so reading it the moment the response lands
    asserts on a race — the guard passed alone and failed in the full suite. It now polls for two seconds and fails at the
    deadline with the numbers in the message. Same family as `-049`: the scheduler and the stats collector are not the product.
- **Guards:** plan shape (index node present, no `Seq Scan`); counterfactual with the index dropped and restored in a `finally`;
  `pg_stat_user_indexes.idx_scan` incremented by a **real authenticated request through the endpoint** (so the claim is about
  the app, not about this file's SQL); and scoped-vs-whole-table cost so a regression that drops the predicate can't hide
  behind a still-indexed reconstruction.
- **Suite:** focused **4/4**; full **`Failed: 0, Passed: 141, Skipped: 0`**, 44 s, 0 warnings; two additional full runs green
  before the mutation cleanup. **AC-10 checked → 11 verified + 6 open = 17**, asserted.
- **Limits stated, not implied:** this file cannot see the *handler's* predicate (that is `-056`'s behaviour and its tests), and
  it does not defend against assertion weakening inside M3's suite (`-058`'s known limit).

**`TDD-EXEC-m4-authentication-060`** · `BEHAVIOR-060` (no AC of its own) · Red `583c160` → Green `993d5f8` · p0 · **Slice 4** (seam Unit FE, with a companion guard in the API suite)
- **Red:** `npx vitest run src/data/problemCodeContract.test.ts` → **`Test Files 1 failed`** (the table module did not exist).
  **Green:** focused **7/7**; **`npm run verify` exit 0** — typecheck, lint, lint:css, **204 FE tests / 21 files**, build; API
  **`Failed: 0, Passed: 144, Skipped: 0`**, 46 s, 0 warnings.
- **The defect was silent, not loud.** The adapter's `switch` had no branch for `unauthorized`, which the API has emitted since
  `-052`, so a 401 fell to `default` → **`corrupt-data`** — the variant meaning *"the server sent something my contract does not
  describe"*. "You are signed out" and "the response was garbage" became the same user-facing state, and nothing in the build
  compared the server's code list with the client's because the two lists live in different languages.
- **`contracts/problem-codes.json` is the increment.** One artifact both suites read, following M3's precedent
  (`fixtures/validation-cases.json`, copied to output by a csproj `Content` entry) rather than inventing a mechanism. Guarded in
  **both** directions: the FE test fails if a declared code has no row; `ProblemCodeContractTests` fails if the API emits a code
  that isn't in the contract. The FE side uses a **typed JSON import**, so the contract's *shape* is in the type system
  (`resolveJsonModule` was already on; `@types/node` is deliberately not).
- **The API guard enumerates rather than restating.** It reflects over every static method on `Problems`, invokes it with inert
  arguments, and reads the `code` extension off the result — so it contains no hand-written list to go stale, and a new
  `Problems.SessionExpired(…)` reddens the suite that shipped it. Measured emissions:
  `not-found`/404 · `conflict`/409 · `validation`/400 · `unauthorized`/401.
- **Two of its three tests passed VACUOUSLY on the way to green** — my reflection enumerated nothing, and an empty set is a very
  comfortable thing to assert against. The file now asserts `emitted.Count > 0` *with the skipped method names in the message*,
  and the same class of hole is what `-058`'s no-`Skip` guard defends. Three reflection findings, all mine: the factories return
  `ProblemHttpResult` (document behind an **internal** `ProblemDetails` property typed `Http.ProblemDetails`, runtime value the
  `Mvc` subclass), and one `GetValue(result)` should have been `GetValue(document)` → `TargetException`.
- **`unauthorized` has an explicit row that maps to `corrupt-data`, and that is deliberate.** DECISION-m4-auth-005 asks the owner
  for a **ninth** `RepositoryError` variant; M2a froze the union at seven and `DECISION-006` widened it to eight **on an explicit
  owner yes**, and the spec says so precisely so the next widening cannot ride along. A merge is not that yes for a change the
  spec labels *"Owner decision required"*, so behaviour is unchanged and the approval becomes one line in the table plus the
  provider behaviour in `-061`. The test asserts the provisional value, so **approving the variant fails a test** — the surprise
  arrives at the moment of the change instead of after a deploy.
- **`-060` verifies no AC.** It is the client half of every criterion that answers with a problem document, which is why the
  count stays **11 verified + 6 open = 17** (asserted) while STATE moves to 15 behaviours executed.
- **Also found by the gate, not by me:** `import.meta.url` is not a `file:` URL under Vitest (the module graph is served over
  http), so `readFileSync(new URL(...))` dies with *"The URL must be of scheme file"*; and `node:fs`/`process` don't typecheck in
  this project at all, because `tsconfig.app.json` lists `types` without `@types/node` — a deliberate constraint, respected rather
  than widened for a test's convenience.

**`TDD-EXEC-m4-authentication-061`** · `BEHAVIOR-061` (verifies no AC — it is Slice 4's frontend seam) · Red `72cd779` → Green `0dd0661` · p0 · **Slice 4** (seam Unit FE)
- **Red:** `npx vitest run src/data/problemCodeContract.test.ts` → **`Failed 1, Tests 1 failed | 6 passed`**, with
  `expected { code: 'corrupt-data', … } to deeply equal { code: 'unauthorized' }` — the assertion `-060` left failing on
  purpose; and `npx vitest run src/pages/loginPage.test.tsx` → **`Test Files 1 failed`** with
  `Failed to resolve import "./LoginPage"`.
  **Green:** focused **10/10**; **`npm run verify` exit 0** — typecheck, lint, lint:css, **214 FE tests / 22 files**, build;
  API **`Failed: 0, Passed: 144, Skipped: 0`**, 38 s, 0 warnings.
- **DECISION-m4-auth-005 — APPROVED by the owner on 2026-09-12**, explicitly, when asked: `{ code: 'unauthorized' }` is the
  **ninth** `RepositoryError` variant, the second widening of M2a's frozen union, made on the same footing as `DECISION-006`'s
  first. A merge was deliberately *not* read as this yes, which is why `-060` shipped a provisional row with a test watching
  it; the test broke on approval, exactly as designed.
- **Four pieces.** The variant, in the union. The table row, now returning its own value. `sessionEnded` in the provider —
  a 401 from **any** seam (first read, re-read, `create`, `update`, `remove`) drops the snapshot. `RequireSession` + `LoginPage`
  + the `/login` route, so the redirect has somewhere to go.
- **Why the write paths are included.** A tab usually discovers a dead session on a *write*: the read that filled the screen
  happened while the session was alive. Without the write paths the 401 stops at a page's toast and the board keeps showing
  rows that belonged to someone's ended session. Only the session state clears; the in-progress form draft does not — that is
  what "draft-free" means in the decision's wording.
- **`?next=` is validated, not trusted.** `safeDestination` refuses anything not root-relative, including `//evil.test`
  (protocol-relative) and `/\evil.test` (the same trick in a parser's face), because honouring a stranger's URL right after a
  password is the credential hand-off. Both are tested; the `%2F%2F` form is the one a URL-decoding reader would miss. The
  destination is also **visible** on the screen — a redirect a user cannot see is one they cannot check.
- **Login success does `window.location.assign`, not `useNavigate`** — a consequence of the clear above: the snapshot was
  dropped and the provider re-reads only at boot, so an in-SPA redirect lands on a board that still believes it failed.
  Injected as a prop so the test watches a call instead of fighting jsdom's unimplemented navigation.
- **`eslint` earned its keep twice:** `no-base-to-string` on `String(init.body)`, and three `useCallback` dep arrays missing
  `sessionEnded`, which I would otherwise have shipped as a stale-closure bug. Typecheck passed *unchanged* by the widening,
  because `ApplicationFormPage`'s switch has a `default`: **the compiler enforces a missing table row, not a missing prose
  case** — so the `unauthorized` sentence added there is a choice, and `default` would have rendered a retry invitation for a
  401, the precise failure the variant exists to prevent.
- **Three failures of mine, all found before committing:** a missing close-paren in my own test made the first Red a parse
  error rather than the missing-module Red I described (fixed, re-measured); an open-redirect case that rendered **two** login
  screens in one test produced "multiple elements named /sign in/i" — assertion right, fixture wrong, split into two; and two
  scripted edits anchored on prose I had reconstructed instead of read (`helper anchor`, `repository.delete(id)` where the
  method is `remove`) — each aborted before writing, so nothing half-applied. **`npm run verify` is the reason none of these
  reached `main`.**
- **Note for whoever re-runs AC-15** (static analysis, test-plan row 15: dependency count + credential literals): this
  increment adds `src/pages/loginPage.test.tsx`, which contains **fake** credentials (`hunter2`, `correct horse`) as form
  input. They are test fixtures, not source secrets — the check's scope is product source and config, and `package.json`
  dependencies remain **3** (react, react-dom, react-router-dom — all already present; no library was added to build a
  login screen).
- **Verifies no AC**, so the count stays **11 verified + 6 open = 17** (asserted). STATE moves to 16 behaviours executed.
  **Next:** `-062` (an `EventSource` failure probes the session once rather than retrying forever), which the ninth variant
  finally makes expressible.

**`TDD-EXEC-m4-authentication-062`** · `BEHAVIOR-062` (verifies no AC) · Red `d762473` → Green `4aebfbd` · p1 · **Slice 4** (seam Unit FE)
- **Red:** `npx vitest run src/data/streamSessionProbe.test.tsx` → **`Failed 5, Tests 5 failed | 2 passed (7)`** — the five
  new-behaviour cases, and two that already pass because they assert M3 behaviour (a change event reloads; unmount closes the
  stream). Those two stay: `-062` edits exactly those functions, so the regression is the likely casualty.
  **Green:** focused **7/7**; **`npm run verify` exit 0** — **221 FE tests / 23 files**; API **`Failed: 0, Passed: 144, Skipped: 0`**, 0 warnings.
- **The loop:** `EventSource` retries forever and fires `error` on every attempt. Pre-M4 a stream that wouldn't open was an
  optimisation lost; post-M4 a **401 is a permanently failing connection** — one `GET /api/applications/events` per backoff
  tick per tab, while the UI shows rows from a dead session under a spinner that never resolves.
- **This narrowed an M3 decision, and the decision was right.** `subscribe`'s own comment refuses to re-read on `error`
  because *"one outage turns into a request per retry tick per tab"* — sound, and it predates auth. So the bound moved from
  **zero reports per subscription to one**, counted **in the adapter** (where the ticks land), and the provider acts only on
  the verdict. `-040`'s unbounded-probing concern survives intact.
- **`probeSession` deliberately bypasses `applyListResult`.** Only `unauthorized` is a verdict; `unavailable` or
  `storage-error` from a probe must not blank a board of readable rows or close a stream that was never the problem. Without
  that separation, "stop the retry loop" is one `if` away from "any network hiccup breaks the app" — and a suite that asserted
  only the 401 path would have allowed it silently. Two counterfactual cases hold the line.
- **Why the handle moved to a ref:** `sessionEnded` now closes the stream. Before `-061` that was unreachable; after it,
  nothing unmounts on a redirect — `ApplicationsProvider` sits above the router on purpose — so spec B-1's unmount-only
  teardown could not end the loop.
- **The callback widening is additive by construction:** `subscribe(onExternalChange: (reason?: 'change' | 'error') => void)`
  — a function taking fewer parameters is assignable to one taking more, so the localStorage adapter and every in-memory test
  repository remain valid untouched.
- **Three existing assertions rewritten with provenance, not loosened** (`eventStream.test.tsx`): `error` was removed from
  *"ignores events the stream was not asked about"* (it is now asked about; the case keeps its title's truth),
  *"does not re-read when a connection drops, only when it comes back"* became *"reports a dropped connection once, then
  re-reads"* with `-040`'s reasoning quoted in place, and the interleaving total moved **3 → 4** with its arithmetic spelled out.
- **Mutation-proved:** deleting the adapter's `if (errorReported) return` makes the boundary case report **2 where it expects
  1** and the probe case **6 reads where it expects 2** — `expected 6 to be 2` is the stampede `-040` was written to prevent.
- **Three findings, all mine, all disclosed at the time:** (1) the file's first Red failed **all seven** cases because my
  `beforeEach` stubbed `EventSource` and never installed the fetch mock, so every case hit Node's real fetch
  (`storage-error`, `TypeError: fetch failed`) — one throwaway diagnostic case printing `same-as-mock=false` ended two rounds
  of reasoning about wire shapes; **a stub that is never installed looks like a fixture and behaves like the network**.
  (2) the mutation probe cost real work: `git checkout -- <file>` reverted my **uncommitted** adapter edit along with the
  mutation — the same trap family I warned about in the previous round's practice task, sprung on me one round later; caught by
  the confirmation grep returning 0, re-applied and re-verified. Habit to keep: snapshot to `/tmp` and restore from that, or
  mutate only after committing. (3) the gate caught `.at(-1)` (TS2550: the app targets **ES2020**; "the runtime has it" is not
  the contract) and an unused prop under `--max-warnings 0`.
- **Verifies no AC** → count stays **11 verified + 6 open = 17** (asserted). STATE moves to 17 behaviours executed.
  **Next:** `-063` (a cross-site write without `X-CSRF-Token` is rejected, and the same write *with* it succeeds — AC-11), which
  needs the browser harness and is therefore gated on the Q4 harness-origin question.

  *(**Superseded twice over, in the same day.** Q4 was answered — `DECISION-m4-auth-007 (a′)`, implemented by this very
  row's parent — and `-063` then ran and moved AC-11 to verified. The gating clause is kept because it records what was
  true at the `-071` boundary: an unanswerable harness question was genuinely between this row and its evidence. What is
  **not** kept is the habit: **two** records in this file ended by pointing at `-063` (`grep -n 'Next:\*\* \`-063'` → lines
  863 and 1241), and both read as pending until marked, which is how a delivered row can outlive its own completion in the
  document that delivered it. The count was re-run after this sentence was written, because the last time I quoted a
  figure like this from memory it was wrong in the same direction.)*

**`TDD-EXEC-m4-authentication-066`** · `BEHAVIOR-066` (+ **AC-17**) · Red `1028bf6` → Green `a1f82f6` · p0 · seams Integration + DB
- **Red:** `dotnet test --filter ~SessionExpiryTests` → **`Failed: 5, Passed: 1, Skipped: 0`**, five distinct missing
  behaviours (no slide, no cap, no idle enforcement, no prune, no retention) and one **control that passes today**:
  `Pruning_never_touches_a_live_session`. The control is reported as pre-existing rather than as a Red — nothing prunes
  anything yet, so of course a live row survives; it is in the file because it is the assertion that stays interesting
  *after* the prune exists.
  **Green:** focused **7/7**; full API **`Failed: 0, Passed: 151, Skipped: 0`, 38 s, 0 errors, 0 warnings**. No FE file
  changed, so `npm run verify` was not re-run; its last measurement is `-062`'s (221 tests / 23 files, exit 0).
- **The seam was promised in the product's own comment.** `SessionGate` said "UtcNow rather than TimeProvider: -066 is the
  behaviour that needs it" since `-055`, and spec §3.2 named the cost of refusing: *"expiry is a column nobody reads."*
  Without an injectable clock the only honest options were sleeping 31 minutes (slow, flaky on a container the whole suite
  shares) or setting the system clock (leaks into every other test in the `postgres` collection). Neither was chosen; the
  clock became a dependency, registered explicitly in `Program.cs` so that "who may read the time" has a visible production
  answer and a test answer.
- **Two lifetimes, asserted separately, because they answer different attacks.** The **idle window** (30 min, inherited from
  the const `AuthCatalog` called "provisional") answers a laptop asleep on a train and *must* slide with activity. The **hard
  cap** (12 h) answers a copied cookie and *must not*. A suite with only idle-window cases passes unchanged while no cap
  exists — sliding is indistinguishable from immortality until you keep using a session for 33 hours — which is why the cap
  test loops 200 advances and asserts only a *range* (`8 h … 2 d`) for where it dies: the test proves a cap exists without
  freezing the number.
- **`SessionPolicy` exists because two files had to agree by accident.** `AuthCatalog` minted `expires_at = UtcNow + 30min`
  and `SessionGate` read the column without knowing where the window came from. Drift between them is a session that never
  expires, or one that dies on the request after it is minted, and nothing in the type system can see it.
- **The slide is bounded by a threshold (15 min), not by taste.** Without it every authenticated request writes to
  `sessions` — a read-heavy board becomes a write-amplification machine, against the whole argument `-059` made for the
  owner index. `A_use_early_in_the_window_does_not_move_the_expiry_and_a_use_late_does` asserts the boundary from both sides
  on a session nowhere near expiring, so a 401 cannot be hiding it.
- **⚠️ MUTATION EVIDENCE THAT ARRIVED BY BEING WRONG FIRST (the prune ate the audit trail).** The first predicate was
  `expires_at < now || created_at < cap || revoked_at < retention`. A revoked session stops sliding the instant it is
  revoked, so by the time retention matters it is *always* expired too — the first arm deleted every revoked row at the next
  login and `RevokedRetention` was decoration. Caught by the case named for the property it broke: *"a revoked session was
  pruned inside its retention window: -052's audit question just became unanswerable."* Revocation now takes precedence over
  expiry inside the predicate. This is the retention window's counterfactual, produced honestly rather than staged.
- **⚠️ A TEST THAT PERFORMED THE THING IT WAS EXCLUDING.** The idle case read the API at 29 min, asserted 200, advanced 2
  more, and expected 401. The product answered 200 and **was right**: one minute of a 30-minute window is inside the slide
  threshold, so the read slid the window. Silence is the only stimulus that tests an idle expiry. The case now advances
  31 min with no reads at all, and the boundary moved to its own test — the suite went 6 → 7 during Green, disclosed rather
  than buried in the diffstat. Root cause is this repo's standing one: writing from intent instead of measurement.
- **Three false alarms before the Red was a Red**, each named because the same mistake is cheap to repeat: the class shipped
  without `[Collection(PostgresCollection.Name)]`, so all six failures were xUnit refusing to bind the fixture;
  `count(*)::int` returns `Int32`, so `QueryAsync<long>` threw `InvalidCastException` in four of six cases; and the fake
  clock's first draft started at a tidy date, which would have made the cap test "fail" because PostgreSQL — not the app —
  fills `created_at` (`HasDefaultValueSql`), putting the session's birth months in the future. Anchoring the fake clock at
  real `UtcNow` forecloses that whole class.
- **⚠️ DEVIATION FROM SPEC §3.2, flagged for review rather than silently taken.** §3.2 describes sessions storing
  "`expires_at`/`last_seen` computed from an injected `TimeProvider`". This row injects the `TimeProvider` and slides
  `expires_at`, but adds **no `last_seen` column** — so no migration. Reasons, in short: expiry stays one predicate on one
  column that the gate, the prune, and `pg_stat` all agree about; `last_seen` would be a second source of truth for the same
  fact and would cost the *same* write per use, buying ops visibility rather than correctness. The cap is derived from
  `created_at`, which already exists and is immovable — the property a cap needs. If the owner wants last-activity for its
  own sake, it is an expand-only migration and `-066`'s tests keep passing unchanged.
- **Practice task taken up next (AGENTS.md step 9):** the four `SessionPolicy` constants are still constants.
  `AuthCatalog`'s deleted comment asked for "a configuration value with asserted boundaries"; this row moved the number into
  one file but did not make it configurable. Small increment: read idle window / hard cap from configuration with defaults,
  assert one boundary per value through the existing `SessionExpiryTests` harness (`UseSetting` on a factory, no new
  seams), and name which of the two the owner would actually want to change per deployment.

**Next:** `-067` (`pk:auth` — credentialed CORS: the API accepts an `Origin` only from the configured client and reflects
`Access-Control-Allow-Credentials`; AC-16, decision-free server-side), unless the Q4 harness-origin answer lands first, which
unblocks `-063`/`-064`/`-065`.

**`TDD-EXEC-m4-authentication-067`** · `BEHAVIOR-067` (+ **AC-16**) · Red `eef2811` → Green `fa6bc32` · p0 · **half delivered: Integration closed, Browser deferred**
- **Red:** `dotnet test --filter ~CorsCredentialsTests` → **`Failed: 2, Passed: 3`** — the two failures are the missing
  `Access-Control-Allow-Credentials: true` on the preflight and on the actual response. All three passing cases are named as
  **controls** in the commit message, including `Vary: Origin`, which **already passes**: ASP.NET Core emits it for a
  `WithOrigins` policy unprompted. That case stays as the guard for whoever replaces the policy with hand-written
  middleware, and it matters in M5, where a CDN is the plan — a cache that ignores `Origin` will happily serve
  `Access-Control-Allow-Origin: <someone else>` to a browser that then believes it.
  **Green:** focused **5/5** (4 s); full API **`Failed: 0, Passed: 156, Skipped: 0`**, 0 errors / 0 warnings.
- **Why the pair has to appear twice.** A preflight grants permission to *send*; the response still has to be *readable*. The
  second assertion is written against a **401** (`-055` gates the route) because a refusal without CORS headers arrives as a
  network error with no status and no body — which collapses exactly the distinction `-062`'s probe depends on: "signed out"
  (clear rows, go to login) versus "server unreachable" (keep rows, offer retry).
- **⚠️ THE MEASURED ANSWER TO AC-16'S PREMISE.** AC-16 is written against "the server emits `*` together with credentials".
  Probed directly (`STATUS=204 HEADERS[]`, probe deleted): a literal `*` in `Cors:AllowedOrigins` goes to `WithOrigins`,
  which treats it as an origin **string** to match, and no browser sends `Origin: *`. So the operator error does not produce
  the forbidden pair — it produces a policy that matches nothing, with every cross-origin client silently refused and no
  diagnostic. **That is worse than the failure AC-16 named**, because it reads as browsers being mysterious rather than as
  config being wrong. The Green moved the guard to where the harm is: the app **refuses to start**, and the message names
  the key and the fix.
- **⚠️ A VACUOUS TEST, CAUGHT AND REPLACED.** The first wildcard case asserted "not (`*` AND credentials)" and passed —
  because there was no credentials header at all to pair with. Green could have shipped with that case sitting in the file
  looking like coverage while being incapable of failing. It was replaced by the boot-refusal assertion, which the guard can
  actually break, and which asserts **two** substrings (`Cors:AllowedOrigins`, `AC-16`) so an unrelated startup failure
  cannot satisfy it.
- **⚠️ AN INVENTED CAUSE, WITHDRAWN IN PLACE.** Five undisposed `WebApplicationFactory` hosts were blamed for the full suite
  moving 38 s → 74 s, and that claim was written into the test file *before being checked*. Disposing them made the suite
  **1 m 36 s**; the class runs in 4–7 s focused. The durations are real, the cause is not established, and the comment now
  says so in the file rather than being left as a plausible-sounding record. Root cause is this repo's standing one: two
  observations plus an assumption about which caused which. Hosts are still disposed — for the honest reason (each owns a
  process that ran `Migrate()` against the container the whole `postgres` collection shares).
- **Scope, stated as half.** The ladder row names Integration **+ Browser**. No `WebApplicationFactory` test can show a real
  Chromium accepting the pair and attaching the `__Host-` cookie cross-origin, because the factory answers a request with no
  same-origin policy to violate. That half is `-064`'s harness and remains gated on the **Q4 harness-origin answer** — it is
  recorded as open here rather than folded into a green checkmark. AC-16's own wording ("preflight echoes… never `*`") is
  satisfied and ticked; the behaviour row is not closed.
- **Practice task taken up next (AGENTS.md step 9):** the origin list is now fail-fast on `*` but still silent on the
  adjacent mistake — an empty `Cors:AllowedOrigins` (`appsettings.json` has no `Cors` section at all; only Development
  does) produces a policy with no rules, i.e. the same "matches nothing" outcome by a different route. Extend the guard to
  distinguish "no cross-origin clients expected" from "I forgot to configure any", and assert both through the harness this
  file already has.

**Next:** the Q4 harness-origin answer (owner) unblocks `-063`/`-064`/`-065` *and* `-067`'s Browser half — they are one
question, not four.

**`TDD-EXEC-m4-authentication-068`** · `BEHAVIOR-068` (extends **AC-7**'s boundary; verifies no AC of its own) · Red `ebe8f24` → Green `fc400c1` · p0 · **ratified into the ladder at execution time (see the spec's second §6 amendment)**
- **⚠️ The Red was almost lost to history, and the repair is itself a claim.** The first commit for this row carried the
  failing tests *and* the fix together — precisely what the "Red, Green and Refactor are separate commits" rule forbids, and
  it happened because I measured the Red, then went straight to the implementation. Split afterwards with `git reset --soft`,
  which preserves the tree exactly and makes history match the order of measurement — but a *reconstructed* Red is a claim
  rather than a fact, so it was verified in an isolated worktree at `ebe8f24` (Red's tests, `src` reverted):
  **`Failed: 2, Passed: 2`**, failing on exactly the two scoping cases with `DoesNotContain() Failure: Sub-string found`.
  The Green commit's message asserted that reproduction before it had been run; the run happened next and agreed. Recording
  which of the two orders happened is the difference between a repair and a fabrication.
- **Where it came from:** `-057`'s probing note — "⚠️ SIBLINGS FOUND BY PROBING, NOT FIXED — proposed `-069`" carried `-068`
  too, and the reason it was not fixed there is the reason it is a row at all: *"a fix nobody was asked for is a second change
  riding inside this one."*
- **Red:** `dotnet test --filter ~EventStreamScopingTests` → **`Failed: 2, Passed: 2`**, and the two failures say the
  disturbing thing plainly — `Assert.DoesNotContain() Failure: Sub-string found`: another user's record id was present in my
  open stream. The two passing cases are controls that must stay passing: A receives A's own change, and **both of A's
  streams** receive it (M3's reason for one-channel-per-subscriber).
  **Green:** focused **4/4** (9 s); full API **`Failed: 0, Passed: 160, Skipped: 0`**, 0 errors / 0 warnings, 53 s.
- **The severity, stated precisely.** The payload is a re-read hint carrying a GUID, and `-056` returns `404` for a row that
  is not yours, so nothing readable leaks. What leaks is *existence and identity* across tenants, and the client's own
  behaviour turns it into noise: `-062`'s handler re-reads on every frame, so a busy B makes A's board re-read on A's bill.
  "Unauthenticated single-tenant dev app" was a true justification in M3 and stopped being true at `-050`.
- **`OwnerId` comes from the record, not from the caller.** They coincide today because `-056` makes a row unreachable to
  anyone else — but they are different concepts, and an admin action or a bulk import is exactly where conflating them would
  start leaking. Named at the call site so the next person to write that path sees it.
- **Two mutation probes, both non-vacuous — after one round of them being void.** (1) Drop the owner filter → `Failed: 2`,
  caught by exactly the two scoping cases. (2) Write to only the FIRST matching subscriber (a work-queue regression) →
  `Failed: 1`, caught by `Both_of_one_users_streams_receive_the_change` and nothing else, which is precisely what that test
  exists to guard. **The first attempt at both probes was invalid**: the shell helper read `$2` instead of `$1`, so no
  mutation was applied and the suite ran against unmutated code — it reported "NOTHING -- assertion is vacuous", which was a
  true statement about an experiment that never happened. Caught because the probe now prints `MUTATION APPLIED` and asserts
  its own anchor; a mutation harness that cannot fail silently is the point of writing it down.
- **Two harness traps, both of which looked like the product bug.** (a) The first draft built a fresh
  `WebApplicationFactory` per call, and the bus is a per-process singleton — no stream could ever see a write, indistinguishable
  from the defect under test. Fixed by one shared host for the class. (b) **You cannot peek at an SSE stream by cancelling a
  read**: a `CancellationToken` on `Stream.ReadAsync` of an HTTP response aborts the response, so the next read threw
  `IOException` — my silence window was destroying the pipe it was about to prove live. Replaced with a listener whose pump
  starts in the constructor (a listener that could exist un-pumped would "prove" silence by forgetting to read).
- **Counts:** no AC checkbox moves — AC-7 was already verified and this extends its boundary to the push channel; AC-12
  (cross-tab SSE **in real Chromium**) stays open behind the same Q4 gate as `-063`/`-064`/`-065`. 13 verified + 4 open = 17.
- **Practice task taken up next (AGENTS.md step 9):** the fan-out filter is a linear scan over subscribers, which is right at
  this size; the row's sibling **`-069`** (wrong-typed wire members → `500` with no `code`) is the next unratified proposal
  and is entirely server-side, so it can be ratified and delivered the same way without waiting on Q4.

**Next:** `-069` (ratify and fix: a wrong-typed member such as `companyName: 42` currently dies in the binder as a `500` with
no `code`, where AC-8's contract says `400` + `code: "validation"`), unless Q4 lands first.

**`TDD-EXEC-m4-authentication-069`** · `BEHAVIOR-069` (extends **AC-8**'s contract; verifies no AC of its own) · Red `b7d0f6c` → Green `5655800` · p0 · **ratified into the ladder at execution time (third §6 amendment)**
- **Where it came from:** `-057`'s "⚠️ SIBLINGS FOUND BY PROBING, NOT FIXED — proposed `-069`", left as a proposal because
  "a sweep across the contract is the human's call to rate, not a change that should ride inside this commit."
- **Red:** `--filter ~WrongTypeBindingTests` → **`Failed: 8, Passed: 1`** — the single pass is the live control (a correctly
  typed body still creates, `201`), which is what makes the other eight a measurement rather than a broken harness.
  **Green:** focused **9/9** (6 s); full API **`Failed: 0, Passed: 169, Skipped: 0`**, 0 errors / 0 warnings, 1 m 8 s.
- **The cause, probed against this repository's own dev server rather than reasoned about.** The captured chain
  (`BadHttpRequestException` → `JsonException` → `InvalidOperationException: Cannot get the value of a token type 'Number' as a
  string`, logged by `RequestDelegateFactory.Log.InvalidJsonRequestBody`) shows the framework assigning **400** and this app's
  error pipeline overriding that with a generic 500. So the increment is attention, not cleverness.
- **Why `code` and not just the status:** DECISION-m3-backend-api-004 requires a discriminator, `-060` built the client's
  table on it, and `-062`'s adapter files an unrecognised document as `corrupt-data` — so the browser was being told "the
  server sent something we cannot parse" while the server's log said "invalid JSON request body". Two true statements,
  opposite directions. The last case asserts the emitted code against `contracts/problem-codes.json` rather than a literal
  here, so a fix that invented a fifth code (`binding-error`) fails instead of shipping a discriminator the client cannot map.
- **`Problems.InvalidRequestBody` shares the code and type URI with `Problems.Validation`** rather than repeating them: the
  client's answer is identical (show it on the field, do not retry, do not sign out) and two literals in two files is how
  contract members drift apart.
- **⚠️ `JsonException.Path` does not match its own documentation.** Documented as `$.companyName`; for a member failure on
  the **root** object it arrives as `.jobTitle` with no `$` — so the first implementation emitted `#.jobTitle` on the wire,
  and five cases failed on it. `PointerFromJsonPath` parses segments from either form, because `toFieldErrors` matches on the
  validator's `#/field` shape and a pointer the client cannot resolve to a field is a message that goes nowhere.
- **⚠️ A CLAIM THIS ROW COULD NOT SUPPORT, AND PROBE B PROVED IT.** The Green commit says the handler's two refusals are
  "both load-bearing". **Probe B removed the guard — `exception as BadHttpRequestException ?? new(...)` — and the full suite
  stayed `Passed: 169, Failed: 0`.** The refusals may be correct reasoning (a real defect must stay a 500 with a traceId;
  payload-too-large must not be relabelled as field validation) but **no test covers either one**, and that is now recorded
  where the claim was made rather than quietly kept. Probe A, on the part that *is* covered: replacing the derived pointer
  with a constant → `Failed: 5, Passed: 4`, caught by all four member cases plus the update case.
  - Probe A's first attempt was **void**: mutating the condition to `if (false)` broke compilation, so no measurement
    happened — the same failure family as `-068`'s `$2`/`$1` helper. It reported "caught by: 0" about an experiment that
    never ran. A probe that cannot tell you it did not run is not a probe.
- **Two test bugs of mine, both found by running them:** an unnamed `AddWithValue` never binds to `@id` ("operator does not
  exist: @ uuid"), and one case "wrong-typed" `notes` with a string — `notes` *is* a string column, so the server was right to
  accept it and the case failed for the wrong reason.
- **Seam lesson, in the same family as `-066`:** the first attempt customised `ProblemDetailsOptions`, and neither
  `MapProblems` nor `OnCreatingProblemDetails` exists on that type (the compiler said so twice; the reference pack named
  `CustomizeProblemDetails`). `IExceptionHandler` is the better seam because it is handed the **exception** — a problem-details
  hook only sees a document already flattened to "an error occurred". Put the seam where the fact still exists.
- **Practice task (AGENTS.md step 9) — closing what probe B exposed:** give the handler's declinations their own tests.
  Cheapest honest shape, one Unit case per refusal, is calling `TryHandleAsync` directly with a `DefaultHttpContext` and (a) an
  `InvalidOperationException` → must return `false` and write no body, (b) a `BadHttpRequestException` with `413` → must return
  `false`. Both are named as implementation-detail tests, which is why they were not written blind here: the row's seam is
  Integration, and the alternative — a route that fails on purpose — is test-only code in production. Pick one and say why.

**Next:** `-068`/`-069` were the last two unratified probe proposals, so the decision-free server-side queue is empty;
`-063`/`-064`/`-065` and the Browser halves of `-067`/`-068` all wait on the same **Q4 harness-origin** answer.

> **Q4 ANSWERED — 2026-09-12 16:46 UTC, option (a)**: serve the harness page from the API's own origin, ratified as
> `DECISION-m4-auth-007` in the spec. The two lines above stay as what was believed when written, and the answer exposed
> **three** errors in them rather than resolving one dependency:
>
> 1. **`-068` never had a Browser half** — its ladder seam is Integration only. The phrase was carried forward from
>    `-067`'s row, then repeated by a checkpoint, then a handoff, then a resume prompt: **a list propagating instead of
>    being re-derived**, which is this task's own root cause wearing a different hat.
> 2. **"all wait on the same answer" was false in kind, not degree.** (a) unblocks **`-065`** (AC-14), and `-065` must additionally **state which
>    URL literal it built with**: Slice 0 measured a 14-character IP versus a 9-character `localhost` moving the gzip sum by
>    **9 B**, and choosing (a) is what fixes which spelling the harness now ships.
> 3. **It converts `-063`/AC-11 from an execution into a restatement.** Same-origin means a **cross-site** POST under
>    `SameSite=Lax` carries **no cookie at all**, so it is refused at the session gate (`401`) and the `X-CSRF-Token` check
>    the row exists to prove **is never reached** — executed as written it goes green proving the wrong property. The honest
>    shape is **two cases, each with its own positive control**: same-site write *without* the token (header check bites)
>    and cross-site write (cookie absent → `401`). **Editing a ratified row's promise is an owner decision, so it is asked
>    next, not implemented.** The accepted cost of (a) is recorded in `DECISION-007` rather than discovered later: with no
>    preflight ever occurring, **`-067`'s credentialed-CORS property stops being exercised in the browser entirely** and
>    survives at its Integration seam only (`CorsCredentialsTests`, real `WebApplicationFactory` host). **AC-12's truth was
>    bought with AC-16's browser redundancy** — a trade, not an oversight.
>
> **⚠️ And point 2 overstated what (a) unblocks — measured 17:37 UTC the same day, before any `-064` code was written.**
> Same-origin fixes the *site* problem and walks straight into a harder one: **Chromium discards `__Host-JTSession` over plain
> `http` at every address, loopback included.** The `__Host-`/`__Secure-` **prefix** demands a cryptographic **scheme**; the
> loopback secure-**context** allowance stops at the `Secure` *attribute*, which is accepted at the very same address. That
> half-step is what made `ASSUMPTION-m4-auth-001` read as true, and its conclusion is false. **So `-064` needs `(a′)`:
> same-origin *and* HTTPS in dev** — measured accepted over `https://127.0.0.1` with a self-signed cert.
> Measurement, the isolated variable matrix, and three harness traps found on the way:
> [`docs/spikes/2026-09-12-host-prefix-cookie-jar.md`](../spikes/2026-09-12-host-prefix-cookie-jar.md).
> **This closes `-053`'s parked practice task** — "browser cookie jar over plain HTTP … gates `-063`/`-064`" — **answered:
> no.** It also means **a plain-`http` dev profile cannot log in from a browser at all** (`204`, then no session), which is a
> product-facing consequence of a question that was only about test topology; stated there as **inference from an identical
> `Set-Cookie` string, not yet run against the real endpoint**.
>
> Also corrected here, from the same read: the four **open** ACs are **AC-3, AC-11, AC-12, AC-14**. AC-3 sits behind its own
> restrike-or-restate decision and **never sat behind Q4**; a `checkpoint-001` §2/§5 and `docs/STATE.md` §3A claim naming
> "AC-12/13/14/18" as the open set was doubly false (AC-13 is verified, AC-18 does not exist) — see the correction appended
> there for why the `13 + 4 = 17` counter could not catch it.

**`TDD-EXEC-m4-authentication-071`** · **new behaviour, no `BEHAVIOR-*` of its own** (it is the enabling half of
`DECISION-m4-auth-007` (a′) and the precondition of `-064`; it verifies **no AC**) · Red `964fdc6` → Green `77ab86d` · p0 ·
**the ladder row was written before the code, and that is what caught the hazard**

- **Where it came from:** the owner's answer to Q4 (`(a)`, then `(a′)` after measurement). `(a′)` says *serve the harness page
  from the API's own origin, over TLS*, and nothing in this product served a page. **TLS needed no code** — Kestrel reads a
  PEM from configuration, which the spike measured — so the code half is exactly the serving half, and that made it easy to
  skip. Skipping it would have put the browser's page on one origin and its API on another, which is the failure this whole
  detour exists to end.
- **Row `-071`, written first:** *"the shell at `/`, a real asset as **itself**, a deep client route as the **fallback** — and
  `/api/**` still answers as the API."* **`-070` was skipped deliberately:** it is a **phantom**, referenced as
  "`-070`'s scoped-mutation-gate verdict" by `checkpoint-001` and `STATE.md` §3A and defined **nowhere** — `grep -rln
  "scoped.mutation" docs/` returns those two files and nothing else. A number cited as though assigned, propagated rather
  than re-derived, the same class as the false AC list. **Left unassigned and asked**, because quietly reusing it would make
  those two records read as satisfied.
- **Red:** `--filter ~SpaHostingTests` → **`Failed: 3, Passed: 5, Total: 8`**. The three failures are the positive cases. The
  five that passed *before any code existed* are not noise and are not a problem — they are **negative guarantees**, true
  precisely because nothing serves anything yet, and they are the half of the row that constrains the implementation rather
  than the half it satisfies. Writing them as assertions is why both failure modes below were visible at all.
- **Green:** focused **10/10**; full API **`Failed: 0, Passed: 179, Skipped: 0`** (1 m), 0 errors / 0 warnings.
  `169 + 10 = 179` ✓ derived from the run, not recalled.
- **Two shapes were tried and rejected, each by an observed failure — and the second is the one to remember.**
  1. **Middleware that awaits the pipeline and checks for a 404** does not work in this pipeline: `app.UseStatusCodePages()`
     is registered *above* it and defers the status write, so the 404 was not observable where the check asked for it.
     **Eight of nine cases still passed**, because only the deep-route case reads that status. A nearly-green file hiding a
     dead branch. Mechanism stated only as far as proven — the fix removes the dependency on the read rather than trusting an
     explanation that was never pinned down.
  2. **`MapFallback("{*path:nonfile}")` passed all ten of `-071`'s cases and broke a ratified row.** `ApplicationsCommandTests.Non_json_body_rejected`
     went **`415 → 404`**, in isolation, with the SPA block as the only difference. A *fallback* is consulted for requests the
     API should decide for itself, so the `/api` clause converted the framework's 415 into a 404 — **silently destroying
     `-045`'s guard**, the one that makes Content-Type the thing that refuses a simple cross-origin write. **Only the full
     suite saw it. This row being green was not evidence.** A catch-all `MapGet` ranks below a literal route, so the API's
     endpoint keeps its own status, and the verb discipline arrives free (a POST to a page path matches nothing → routing
     answers 405, now asserted rather than assumed).
- **A default that no test touches is a default that rots.** The first real boot served **`404` at `/` while the file was
  green**: `appsettings.Development.json` shipped `../../dist`, which from `api/src/JobTracker.Api` resolves to `api/dist` —
  two levels short of the repository root. Every existing case handed the app an **absolute** path, so the one shape a
  developer actually gets had no test. **`Relative_SpaRoot_resolves_against_the_content_root` now pins it**, and the
  `LogWarning` naming the resolved path is what made this a 20-second diagnosis instead of an afternoon — the reason the
  warning is in the code rather than a silent fall-through.
- **Proved on the real bundle, which no synthetic fixture can claim:**
  `/` → `200 text/html`, **byte-identical to `dist/index.html`** (`cmp`), carrying `<title>Job Tracker`;
  `/records/8f31/edit` → `200` shell for a route the server has never heard of;
  `/assets/index-D3LZqJ4m.js` → `200 text/javascript`, **190,906 bytes served by the API**;
  `/assets/typo.js` → `404 application/problem+json`; `/api/nope` → `404 application/problem+json`.
- **Why Development-only is a behaviour, not a detail:** `docs/aws-deployment.md` puts the front end and the API on
  **different origins** when deployed — which is what makes AC-16's credentialed CORS load-bearing. A bundle served by the API
  in production would be a second copy of the app on the API's own origin, where its requests bypass the boundary everyone
  believes in. So the Production case runs **with the bundle present on disk**; a gate that only passes when the directory is
  missing is not a gate.
- **One trap this row leaves standing, recorded rather than hidden:** the served bundle talks to the API through
  `VITE_API_BASE_URL`, and `applicationStore.ts` treats an **empty** value as *"use the localStorage adapter"* — so a
  same-origin build made **without** that variable serves a fully-working-looking app that never touches the API. The harness
  must build with `VITE_API_BASE_URL` set to the API's own origin. **Not fixed here**: changing what "unset" means would move
  a `BEHAVIOR-*` boundary in a row that has none, so it is `-064`'s setup problem, stated before it is discovered in a green
  browser run.
- **Hygiene:** `jobtracker_spa071` created for the real-boot probe and **dropped afterwards**; the dev database was never
  pointed at. **No orphan** — `pgrep -x dotnet` (**not** `-f`, which matches the checking shell's own command line and
  reported a phantom orphan twice today) returns nothing, port `5099` closed.

**Next:** **`-064`** (AC-12) — `httpCrossTab.mjs` → **7/7 while authenticated** in real Chromium, now that an authenticated
session is reachable. Its setup owes three things this record names: the harness must be built with `VITE_API_BASE_URL`
pointing at the API's own origin (above), it must **clear cookies per case** or it fails green (the spike's finding), and it
must address the host at a **non-loopback** IP over **TLS**, which is the only shape the container can reach.

**`TDD-EXEC-m4-authentication-064`** · **verifies AC-12** · Browser seam · p0 · delivered 2026-09-12 19:43 UTC,
**after `-071`**, which is why it is appended out of numeric order — the IDs are stable identifiers, not a sequence, and
renumbering to keep the file tidy is exactly how M3's phantom cross-references were made.

- **The row as ratified:** *"`httpCrossTab.mjs` → **7/7 while authenticated**, in real Chromium. **jsdom cannot attempt
  this**: no `EventSource`, no origin policy."* **AC-12's evidence clause** is `docker exec … node /srv/httpCrossTab.mjs`
  → 7/7 **with cookies**, no test-only shims — so the copy path inside the container is the one the AC names, verbatim,
  rather than a `harness064.mjs` of my own devising.
- **Result: 7/7, twice.** First passing run completed **19:40:49 UTC**, the second **19:42:51 UTC** — **`mtime` of the two
  run logs**, taken from the files rather than from memory, which is what makes them citable. The second was after
  changing only the destination path, so the identical output is a reproducibility result, not a re-echo. An earlier
  attempt aborted at **19:39:22** on a CDP scoping error, recorded below rather than hidden.
  ```
  AUTHENTICATED  __Host-JTSession present (secure=true httpOnly=true sameSite=Lax domain=172.23.124.252)
                 after a form login at https://172.23.124.252:5443
  PASS  both tabs rendered the HTTP-backed build   — page and api both https://172.23.124.252:5443, tab B "list"
  PASS  the first screen is an HTTP read …         — GETs seen: [200]
  PASS  no application data was written to localStorage — keys: []
  PASS  the create form opens in a real browser
  PASS  a write through the real form reaches the API    — POST status: 201
  PASS  the other tab learned without reloading — the SSE join is real — tab B navigation entries: 1
  PASS  the marker record is removed again      — deleted 204
  ```
  **The URL literal used, stated because `-065` and any later re-run need it:** `https://172.23.124.252:5443` — the
  sandbox's routable host address, **not** `localhost`. Two independent reasons, both measured: Chromium runs inside a
  bridge-networked container whose loopback is its own, and the certificate's SAN is that IP.
- **`sessions=1` in the scratch database afterwards** is the proof the login was server-side and not a client illusion;
  `applications` rows matching `Crosstab %` **= 0** proves check 7 cleaned up rather than reporting that it did.
- **This row's Red equivalent is the negative control, and it is run by the script, not by me.** `E2E_SKIP_LOGIN=1`
  repeats all seven checks with no session: **4 fail** — `both tabs rendered` (tab B lands on `/login`),
  `first screen is an HTTP read` (`GETs seen: [401,401]`, the gate in BEHAVIOR-055 answering in the browser), the create
  form never opening, and the cleanup. `run-064.sh` **aborts the row if the control passes**.
  **The control's one PASS is a real limitation and is stated, not smoothed:** *no application data was written to
  localStorage* cannot fail when nothing was written anywhere, so it is a check on sufficiency, not on authentication.
  A row whose control passes 7/7 would be a row proving nothing, and the only reason that is visible here is that the
  control exists.
- **Why the topology had to change with the authentication.** `-042` ran a page on `:4173` against an API on `:5080`
  because two origins *was* the deployment shape and the point was to expose CORS. Under M4 that shape cannot
  authenticate at all: `SameSite=Lax` does not carry `__Host-JTSession` on a cross-origin subresource request, and plain
  `http` discards the prefix before it ever reaches the jar. So `APP` and `API` now default to the **same origin** and
  the harness runs against the TLS endpoint `-071` built. `-042`'s 7/7 is not lost — it is simply no longer reachable
  without a login, which is what `BEHAVIOR-055` was for.
- **The jar-clearing rule, earned twice over.** CDP's cookie jar survives closed tabs and new targets in this image, so
  `Network.clearBrowserCookies` runs **only on the tab that logs in**. Clearing per-tab would delete the session between
  A and B, tab B would render `/login`, and the headline check would fail looking like a broken push channel. Its first
  attempt was also the row's first failure: sent on the browser-level connection the call returns
  `-32601 'Network.clearBrowserCookies' wasn't found` — `Network.*` is session-scoped (probe 5 passes a `sessionId`; I
  did not, and found out at runtime).
- **Login goes through the real form** (`#login-email`, `#login-password`, `form button[type="submit"]`) because
  `-061`'s page, its redirect to `?next=`, and whatever the client actually puts on the wire are all things AC-12
  depends on. A harness that posted to `/api/auth/login` itself would certify a login no user can perform.
- **Check 1's predicate was widened deliberately, and the change is narrow.** It previously demanded *the first GET
  ever* be a 200; under authentication the first GET is legitimately a 401 — that 401 is what produces the redirect to
  `/login`. It now waits for an authenticated 200 and still asserts the same thing the row promised: the screen's data
  came from the network, not from disk.
- **No product code changed, and no shim was added** — AC-12's other clause. The only capability flag is
  `--experimental-websocket`, because the container's Node is 20 and global `WebSocket` is flagged there; the host's
  Node 24 needs nothing. The harness is still dependency-free raw CDP.
- **A defect this row is positioned to see and did not fix, recorded as a proposal:**
  `src/data/httpApplicationRepository.ts:211` opens `new EventSource(url)` **with no `withCredentials`**. That is correct
  for the origin this run used and **silently wrong for the two-origin topology in `docs/aws-deployment.md`**, where an
  authenticated stream would arrive uncooked as a 401 and every dev check here would stay green. Widening further:
  `Lax` blocks the credentialed cross-origin case for `fetch` as well as for `EventSource`, so AC-16's
  *credentials-on CORS* configuration and `DECISION-006`'s *`SameSite=Lax`* cannot both be load-bearing in the deployed
  shape — the pair needs `SameSite=None; Secure`. **Stated as inference from cookie semantics, not as a measurement**:
  `-064` runs same-origin and cannot observe it. Raising it as a row of its own rather than fixing it here, for the
  standing reason that a fix nobody asked for is a second change riding inside this one.

**Next:** `-063` (AC-11) — the two-case split the owner approved, each case with its own positive control, case 1 aimed
at a state-changing verb because login takes no `X-CSRF-Token`. It needs the antiforgery gate to exist first: there is
**no server-side check and no client-side header in the tree today**, which is also why `-064`'s cleanup `DELETE` sailed
through unopposed.

*(**Superseded at the next record, which follows.** `-063` ran the same day: the gate exists, the client sends the
header, and AC-11 is verified at the Browser seam. Kept rather than edited, because "there is no server-side check
today" is a measurement with a timestamp, and the sentence that was true at 19:50 UTC is the thing that makes 20:56 UTC
legible. What was **not** kept: the habit of leaving a forward reference live after the thing it points at has landed —
that is how `-070`'s phantom verdict survived five documents.)*

---

**`TDD-EXEC-m4-authentication-063`** · **verifies AC-11** · Browser seam (with an API seam added beneath it) · p0 · delivered 2026-09-12

**Red** `cd72bbe` (7 cases, **4 failed / 3 passed**) → **Green** `91f38be` (implementation + the ~80 existing tests
the gate forced to adapt) → **evidence** `28056b6` (the two browser cases, 11 checks) → this record.

**What shipped.** `Antiforgery` + `AntiforgeryMiddleware` (`api/src/JobTracker.Api/Auth/AntiforgeryGate.cs`), a
second cookie issued at login and cleared at logout (`__Host-JTCsrf`, `Secure; SameSite=Lax; Path=/`, no `Domain`,
**no `HttpOnly`** because a token the client cannot read cannot be echoed), registration strictly after
`SessionGate` so a missing session stays `401` and only an authenticated tokenless write becomes `403`, and a new
wire code `antiforgery` paid for in all three places `-060`'s contract demands: `Problems.cs`,
`contracts/problem-codes.json`, `PROBLEM_CODE_TABLE`. API suite **186/186, 0 warnings**; `npm run verify` **exit 0,
227/227**.

**What the two-case split bought, measured rather than argued.** Case 1 (same-site, tokenless) is the only shape in
which the header check is what fails; the ratified one-sentence version could only have run case 2, and case 2 fails
for a reason the header does not explain. Case 1e/f adds something neither seam could otherwise show: after a real
logout + form login in the same browser, the **old** token is refused and the **new** one is served, so the value is
bound to a session rather than to an account — `A_token_belonging_to_another_session_is_refused` says the same thing
at the API, and only the browser can say it about a jar.

**Four defects found on the way, all of them the same mistake — writing from intent:**
  · `ApplicationsApiFixture` took the session cookie with **`values.First()`**, correct only while login set one
    cookie. Six test files had their own version, all pinning **`Assert.Single`** — a *count*, standing in for a claim
    about attributes, so a second cookie broke 85 tests while proving nothing about any of them. They now share
    `TestCookies`, which selects by name; the attribute assertions stayed where they were, because those are the
    ratified promises.
  · The fixture then set `X-CSRF-Token` to the whole `name=value` pair instead of the value, and ~80 tests answered
    `403` while `AntiforgeryGateTests` sat green at 7/7 — the two files disagreed because only one stripped the
    prefix. Found by printing the header and calling `Antiforgery.Matches` from a throwaway test, not by rereading
    the code. The throwaway file was deleted before commit; the diagnosis is why the fixture now says so in place.
  · `OwnershipTests`' per-request host **cannot validate its own tokens** (ephemeral ring). The code was right, the
    test topology was the bug, and the consequence for deployment is `-074` — a row, not a comment, because a comment
    in `AntiforgeryGate.cs` is read only by someone already in that file.
  · `-064`'s cleanup asserted `'deleted ' + res.status`, which **passes on a 403**. The gate made that check load-
    bearing by accident, and it was fixed in the same commit that had to touch it. Same for the JS `SyntaxError` a
    stray quote produced in `csrfGate.mjs`'s cleanup, reported as "returned nothing" until the fallback printed the
    raw CDP reply: **an assertion that swallows an exception is indistinguishable from a pass at a glance.**

**Agent-decided, recorded as such (standing instruction, not owner ratification):** data protection over a
hand-rolled HMAC (deviation from DECISION-006's literal wording, noted under that decision); `antiforgery` mapped to
the existing `unauthorized` client variant instead of a tenth `RepositoryError`; logout deliberately **exempt** from
the gate (a forgery there costs the victim only their own session, and a user whose token is unreadable must still be
able to sign out); `OPTIONS`/`HEAD` excluded by an allow-list of unsafe verbs rather than "everything but GET", so a
preflight never becomes a `403` that hides a CORS problem; and `-074`/`-075` written into the register.

**Also changed, because the row made it true:** `run-063.sh` and `run-064.sh` now `DROP DATABASE … WITH (FORCE)` and
`pkill -x JobTracker.Api` — `dotnet run` does not forward SIGTERM to the app it spawns, and an aborted run's server
kept the scratch database, which is how the next run learned it (exit 127 en route, from a comment line of mine that
started `--` instead of `#`).

**Coupling discharged:** `-064`'s harness now sends the header on its cleanup `DELETE`, and `run-064.sh` was re-run
after the gate landed: **7/7**, negative control still failing 4 of 5 as required, `deleted 204`
(20:57:05 UTC). The row is still verified by its own evidence, not by this record's say-so.

**Next:** `-065` (AC-14, the login-route bundle delta) — the last unblocked ladder row, and it must **name the URL
literal it built with**, because an empty `VITE_API_BASE_URL` selects the localStorage adapter and a 0 kB delta
measured that way would be meaningless.

---

**`TDD-EXEC-m4-authentication-065`** · **verifies AC-14** · Build seam · p1 · delivered 2026-09-13

**Harness** `ddaf7f2` → **its own defect fixed** `b489f18` → **measurement at `b489f18`** → this record. No Red/Green
pair: this row produces a number, not a behaviour, and the assertion is the harness's exit code.

**The measurement.** Both trees built in one run, same dependency tree, same literal, Slice 0's command verbatim:

| configuration | baseline `41b32af` | head `b489f18` | delta |
| :--- | ---: | ---: | ---: |
| flag-off (`VITE_API_BASE_URL` unset) | **60,118 B** | 61,140 B | **+1,022 B** |
| flag-on (`http://172.23.124.252:5080`) | **61,368 B** | 62,610 B | **+1,242 B** ← AC-14's figure |

**1,242 B against a 3,072 B gate: PASS, at 40 % of budget.** The reproduced baseline matches Slice 0's record to the
byte (`+0 / +0`), and the run refuses to proceed unless `package.json` **and** `package-lock.json` are identical
between the two commits — a dependency that moved would make this number about npm rather than about the login route.

**Four things the numbers say that the AC's single figure does not:**

  · **The flag-off delta is 1,022 B, not 0.** Slice 0 anticipated that flag-off would produce "a flattering,
  meaningless 0 kB" — it does not, because only the **HTTP adapter** is dead-code-eliminated by the `import.meta.env`
  substitution. `LoginPage`, its route in `App.tsx`, and the session-facing store ship **regardless**. So the honest
  decomposition of M4's client cost is: **1,022 B** of always-present auth UI + **220 B** that appears only when the
  API is configured. (`-063`'s `csrfHeaders` and jar reads are inside that 220 B: the same-config comparison grew from
  Slice 0's 1,250 B to 1,470 B across M4.)
  · **`/login` is not a chunk.** The AC says "the login route's bundle", which implies a lazy boundary; there is none
  — `src/App.tsx:4` imports `LoginPage` statically, so its bytes are in `index-*.js`. The measurement is the whole-JS
  gzip sum, which happens to be exactly the command Slice 0 recorded. If anyone adds `lazy()`, what AC-14 *means*
  changes and this record must be re-read rather than re-run.
  · **One chunk only**: `index-CUYRe9uD.js`, 62,628 B gzipped on its own against 62,610 B for the concatenated stream.
  The 18 B difference is the shared gzip dictionary across `cat`, and it is recorded because a per-chunk table and a
  concatenated total are not the same metric and the AC names neither.
  · **Reproduction stability is the finding worth keeping.** Byte-identical output across five hours and seven merged
  PRs is what makes this seam usable as a gate at all — and it is now known to depend on the lockfile, since that is
  the thing checked to make it true.

**A defect in my own harness, found by reading its output instead of its exit code.** The run reported **exit 0** while
step 3's report was shredded:

    flag-off delta non-lazy B — what M4s  flag-off delta auth B — what M4s  flag-off delta code B — what M4s
    # Reported beside the number, never inside it: Slice 0s B — what M4s  flag-off delta figures, B — what M4s

An apostrophe inside a single-quoted `printf` format (`M4's`) closed the string, so the rest of the sentence became
**extra printf arguments** — which printf re-uses the format for — and the unterminated quote ran on through a comment
line whose own `Slice 0's` apostrophe was the next delimiter, swallowing the format string below it. Every number
above that damage printed correctly; **the line that reports whether the reproduced baseline still agrees with
Slice 0's did not**, and that is the one line whose job is to say whether the comparison is sound. `tail -1` would
have shown a clean green. Fixed in `b489f18` by rephrasing the sentences (not by escaping: `'\''` in a log line is
how the next person reintroduces it).

**Two claims the harness makes about itself, verified rather than assumed:** `shellcheck` is **not installed** here, so
the only static check that ran was `bash -n` — an early version of mine read an empty `shellcheck … | head` result as
"clean", which was the pipe-`$?` trap again in a command I wrote to catch it. And the teardown safety note is load:
dependencies are hard-linked rather than symlinked, and the final step asserts the repository's own `node_modules` is
still populated (**376 entries, vite present**), because `git worktree remove --force` is not `rm -rf` and nothing in
this run proves how it treats a `node_modules` pointing at the main repository.

**Agent-decided (standing instruction, not owner ratification):** comparing against a **rebuilt** baseline rather than
the quoted 61,368; reporting the flag-off figure and the single-chunk breakdown though AC-14 asks for one number; and
adding `-076`, because the gate this row establishes is **not wired into CI** — measured,
`grep -c bundleDelta .github/workflows/ci.yml` → **0** — so nothing re-checks it and a dependency bump could breach
3 kB with every job still green.

**Coupling:** none in code. The only file outside `tests/build/` this row touches is the register (`-076`).

**Next:** the ladder has no unexecuted behaviour rows left. What remains is the four practice tasks carried from
`-066`…`-069` (`grep -c 'AGENTS.md step 9' docs/tasks/TASK-m4-authentication.md` → **4**, each under its own record:
`SessionPolicy` constants, empty-vs-absent `AllowedOrigins`, the linear-scan fan-out, the handler's untested
declinations), the two rows `-063` produced (`-074` key ring, `-075` harness recipe), and `-076`. AC-3 is the only
open acceptance criterion left, and it is gated on an owner decision, not on work.

### §6a. Durable checkpoint and handoff records (Level 2 gate)

| Sequence | File | Trigger | State |
| :--- | :--- | :--- | :--- |
| `checkpoint-001` | [`TASK-m4-authentication.checkpoint-001.md`](TASK-m4-authentication.checkpoint-001.md) | context compaction + milestone boundary, 2026-09-12 15:55 UTC (`-069` delivered) | `in_progress` — deliberately not a stop state |
| `handoff-001` | [`TASK-m4-authentication.handoff-001.md`](TASK-m4-authentication.handoff-001.md) | same boundary; receiver runs its §2 (H1–H10) before editing | receiver must validate Task ID, revision, ACs, invariants, blockers, and the one next action |

This file (§6) remains the **Local Task Source**: the checkpoint and handoff are projections of it, and any mismatch leaves
execution reconciling rather than advancing. Neither record authorises a tag, release, publication, remote operation,
deployment or rollback.





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
`007` **browser harness origin: serve the harness from the API's own origin (grill Q4, option a)** — the **only** one of
these given by direct owner answer rather than by merge-of-the-PR-that-asked, 2026-09-12 16:46 UTC ·
`004` **`owner_id` + client ids retained, 404-not-403** · `005` **ninth variant `unauthorized`** ·
`006` **`SameSite=Lax` + antiforgery header**.
All six are **the spec's recommendations, approved by the merge of PR #34**, not by ticked boxes — **spec §7's eight
checkboxes remain physically unchecked in `main` (`fa4ed7d`), and six of them are owner-only.** The two that are not:
the **grilling** item is agent work and **not yet done** (it precedes Slice 1), and the **"every §2 command was run"**
item is **partly unmeetable at spec stage** — several commands target behaviour that does not exist yet, so it is
re-scoped to *"every target carries a command"* now and *"every command was run"* at the §6 gate. **Stating that
distinction is the gap 10b lesson applied to my own checklist rather than waiting to discover it later.**
