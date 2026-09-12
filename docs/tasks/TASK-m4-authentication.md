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

- [ ] **AC-1** — **Every data route rejects anonymity, the stream included.**
  · `curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5080/api/applications` → `401`; **same for
  `/api/applications/events`, `…/{id}`, and for `POST`/`PUT`/`DELETE`.**
  · *Why the stream is named:* an attribute typo on one endpoint is invisible to every other test.

- [ ] **AC-2** — **The session cookie carries all four attributes, asserted on the header.**
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

- [ ] **AC-4** — **Password cost is measured and bounded, and the assertion is the test.**
  · `dotnet test --filter ~PasswordCost` → `Hash` p95 < 1000 ms, `Verify` p50 within 200–800 ms; iterations recorded in
  the envelope. **No number is asserted in this record before that run.**

- [ ] **AC-5** — **The session id is regenerated at login** — a pre-login cookie is invalid after, and the test proves it
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

- [ ] **AC-15** — **No new runtime dependency, and no credential literal in source.**
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

Each gets a `TDD-EXEC-m4-authentication-NNN` block **written in the turn that earns it**; M3 finished with 21 behaviours
and 20 blocks, and writing the missing one exposed a false claim about two others. **The count is asserted, not recalled.**

## 6. Evidence and Completion Gate

`Pending` — filled at execution. Rules carried forward: print the AC ID lists and assert `checked + open == 17` (M3 had
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
