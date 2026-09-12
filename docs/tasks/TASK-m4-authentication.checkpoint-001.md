# Checkpoint Record 001 — TASK-m4-authentication

> **⚠️ AMENDED 2026-09-12 16:46 UTC, after it was consumed.** No original sentence below was deleted or rewritten; every
> correction is **appended at its own site**, dated, so the record still reads as history. Four things changed:
> **PR #58 was found merged**, not open (§2's table, §8) · **Q4 was answered, option (a)** (§5, §6) · **H4's `169/0` did not
> reproduce on the first re-run** (§3a) · **this record's open-AC list was false**, and the `13 + 4 = 17` counter that
> vouched for it could not detect that (§2). The two metadata bullets below the banner — **Execution State** and
> **Branch / audited revision** — were also overtaken, so they carry their own notes rather than being edited here.
> **Read §3a, §5, §6 and §8's amendment notes before acting on any number in this file.**
>
> *This banner's first draft said "nothing in the body **above this line**" — written before the banner was moved to the
> top, so it described a position it did not have. Self-referential location claims belong in the same suspect class as
> the commit-hash line below them.*

- **Task ID**: `TASK-m4-authentication` · **Canonical record**: [`TASK-m4-authentication.md`](TASK-m4-authentication.md) (§6 carries the per-slice `TDD-EXEC` records; this file is a projection, not a replacement — the Task Record stays authoritative)
- **Specification / planning**: [`docs/specs/2026-09-12-spec-m4-authentication.md`](../specs/2026-09-12-spec-m4-authentication.md) (approved by the merge of PR #34, `fa4ed7d`) · ladder [`docs/tests/2026-09-12-test-m4-authentication.md`](../tests/2026-09-12-test-m4-authentication.md) · grill [`docs/reviews/2026-09-12-m4-plan-grill.md`](../reviews/2026-09-12-m4-plan-grill.md)
- **Checkpoint at**: 2026-09-12 15:55 UTC · **Trigger**: context compaction + milestone boundary (`-069` delivered, decision-free queue empty). Level 2, so this record is the durable gate, not the chat summary.
- **Execution State**: `in_progress` — **not** `handoff_ready`. Nothing is blocked *for the agent*; the queue is empty because the next four rows need one owner answer, and PR #58 is with the human. No resume condition is unmet, so no stop state is warranted, and implementation edits, commits and PR actions remain permitted by this record's own terms.
  > **Amended 16:46 UTC**: still `in_progress`, still not `handoff_ready`, but the *reason* inverted. PR #58 is **merged**; Q4
  > **was asked and answered**. The queue is no longer empty — it now leads with **one further owner question** (`-063`/AC-11's
  > restatement) followed by executable rows `-064` and `-065`. "Implementation edits, commits and PR actions remain
  > permitted" still holds, and was used: `docs/m4-q4-decision-ac-fix` → **PR #59**.
- **Branch / audited revision**: `feat/m4-069-wrongtype-binding` @ `38ff6d2` — three commits over `main` (`fea085c`, the `-068` merge), `git status --porcelain` empty, `git merge-base --is-ancestor fea085c HEAD` **OK**.
  > **Amended 16:46 UTC**: that branch's tip was **`f9482a5`**, not `38ff6d2` — the fourth commit being the one carrying these
  > two records, which the line below this one says cannot be named inside its own file. `main` has since moved to
  > **`47852a4`** (`-069` merged, ff-reconciled), and the branch is **fully absorbed** into it
  > (`git merge-base --is-ancestor f9482a5 main` → OK).
- **Which commit carries these records**: not nameable inside its own file — a hash cannot contain its own commit's hash. Find it with `git log --grep='docs(m4): checkpoint' -1 --format='%h %s'` and validate **ancestry**, not equality (the same trap that made M2 checkpoint-002 reword this line).
- **Scope changes since checkpoint-000 (there is no prior M4 checkpoint)**: three. `-066`, `-067`, `-068` and `-069` were all **ratified into the ladder at execution time** by dated §6 amendments to the spec (three amendment blocks, appended, never renumbered). `DECISION-m4-auth-005`'s ninth variant was approved by the owner explicitly; every other deviation was approved by merge-of-the-PR-that-asked.
- **Release-evaluation handoff fragment**: **N/A** — no candidate, tag, QA gate or release decision exists for M4. Nothing in this file authorises a tag, release, publication, remote operation, deployment or rollback; those stay `pk:ship` + the human.

## 1. Objective (unchanged)

Give Job Tracker a real identity boundary: email + password accounts, a server-side session in a `__Host-` cookie, per-user data ownership, and a login/session UI that behaves correctly at the four states (empty, loading, error, success) — so that M3's single-tenant API becomes a multi-user one **without** any page learning what authentication is.

## 2. Completed

| Slice group | Behaviours | Merged |
| :--- | :--- | :--- |
| Accounts, login, cookie, logout | `-047`…`-054` | PRs #37–#44 |
| Ownership, session rotation, enumeration resistance | `-052`, `-055`…`-059` | PRs #45–#49 |
| Error-contract net + client auth states | `-060`…`-062` | PRs #50–#52 |
| Sliding/expiring sessions with an injectable clock | `-066` | PR #55 (`ee994f5`) |
| CORS: credentials, explicit origins, wildcard refused at boot | `-067` (**half**: Browser seam open) | PR #56 (`538c77e`) |
| Owner-scoped SSE fan-out | `-068` | PR #57 (`fea085c`) |
| Wrong-typed body → `400` + `code` + pointer | `-069` | **PR #58 — merged `47852a4`, 2026-09-12 16:09 UTC** |

**23 ladder rows (`-047`…`-069`), 21 `TDD-EXEC` records, 13 ACs verified / 4 open (= 17, asserted by the edit script at every write).** `-063`, `-064`, `-065` remain unexecuted and gated.

> **⚠️ Correction, 2026-09-12 16:46 UTC — this record's AC list was wrong, and the counts are why nobody noticed.**
> The sentence that followed read: *"AC-12/13/14/18 are the four open ACs and all four sit behind the same owner answer (§5)."*
> Measured against the task record's own checkbox lines, **the open ACs are AC-3, AC-11, AC-12 and AC-14.** Two independent
> falses: **AC-13 is verified** (`-058`, the 65-case M3 net — its `[x]` is at `TASK-m4-authentication.md:121`), and **AC-18
> does not exist anywhere in `docs/`** (`grep -rn "AC-18" docs/` → no matches; the ledger stops at AC-17).
> **Why it survived the edit script:** that script asserts `verified + open == 17`, and **13 + 4 = 17 is true no matter which
> four are open.** A counter cannot detect a mislabelled identity, only a miscount — same family as `Assert.Equal(65…`
> matching nothing (§4.4). **The check that passes is not the check that proves.**
> And the consequence is not cosmetic: *"all four sit behind Q4"* was false too. **Three** (AC-11, AC-12, AC-14) sit behind
> it; **AC-3 sits behind its own restrike-or-restate decision**, which §5 listed separately as owner-side residue one line
> later — the record contradicted itself, each line individually plausible.

## 3. Verification evidence — every row re-executed for this checkpoint, none carried over

| # | Check | Command | Measured at `38ff6d2` |
| :--- | :--- | :--- | :--- |
| V1 | API suite, clean rebuild | `cd api && dotnet test` | **`Failed: 0, Passed: 169, Skipped: 0`**, 1 m 8 s; `grep -cE 'error CS\|warning CS'` → **0** |
| V2 | Frontend gate | `npm run verify` | **exit 0** — 23 files / **221 tests** passed, `built in 1.86s` |
| V3 | PR carries the local tip | `gh pr view 58 --json headRefOid` vs `git rev-parse HEAD` | both `38ff6d2d2c250943fece5e06a885e9b9ff92f15e` |
| V4 | M3 regression net intact | `M3RegressionNetTests.cs` | `private const int Ac13ExpectedCases = 65;` + `Assert.Equal(M3Manifest.Length, M3TestMethods(assembly).Count())` — **note**: `grep "Assert.Equal(65"` finds **nothing**; a naive grep reads this live guard as absent (see §7) |
| V5 | Ladder / EXEC / AC counters | counted from the files by the edit script | **23** behaviour rows · **21** EXEC records · AC **13 + 4 = 17** · **3** §6 amendments |
| V6 | Cookie name unchanged at both sites | `grep -c __Host-JTSession api/src -r` | **2** (gate + auth catalog) |
| V7 | Clock seam still one consumer pair | file scan | `SessionGate.cs`, `AuthCatalog.cs` are the only `TimeProvider` readers |
| V8 | Ancestry | `git merge-base --is-ancestor fea085c HEAD` | **OK** (nothing was lost in the `-068` merge) |

### 3a. Post-merge re-execution (2026-09-12 16:46 UTC, at `f9482a5`) — one row did not reproduce cleanly

`-069` was found **merged** (`47852a4`, 16:09 UTC) when H3 was run, not open as the resume prompt stated, so H1–H10 were
re-executed against the merged tree. Two rows moved:

| # | Result on re-execution |
| :--- | :--- |
| V1 | **First full run: `Failed: 1, Passed: 168`, not 169/0.** `ApplicationsCommandTests.Stale_if_match_conflicts_without_writing:127` — the SQL probe `select (updated_at > created_at)::int` returned **0** where the `applications_set_updated_at` trigger should make 1 unconditional. **Reruns: two full suites `169/0` exit 0, and the test itself passed 3/3 in isolation.** So it is a real, attributed, **non-reproducing** single. **Cause deliberately not named** — candidates are the trigger's `now()` (transaction-start instant) resolving equal to the insert's `DEFAULT now()`, the probe reading mid-flight, or shared-fixture interaction; the last unattributed `dotnet test` failure in this project (M3, 1/64) stayed undiagnosable precisely because its log was gone. **Next sighting: `dotnet test > /tmp/dt.log 2>&1` and pull that row's two timestamps verbatim before reading any summary line.** It lives inside AC-13's own net, so its credibility is the stake, not this run's colour. |
| V2 | **Reproduced exactly**: `npm run verify` exit 0, 23 files / **221 tests**, build 1.58 s. |

**Two process faults of mine, disclosed in the same shape as the six the handoff counts.** (1) I first ran V1 as
`dotnet test 2>&1 | tail -40`, so the pipeline reported **exit 0 on a failing run** — AGENTS.md's `pipefail` rule, hit while
*verifying the record that warns about it*; the later runs redirect to a file and read `$?`. (2) I invented the
`ConnectionStrings__Default` value rather than looking it up, because H4 elides it (`…`). It was harmless — the suite starts
its **own** Testcontainers PostgreSQL (`api/README.md` §"Tests do not use `docker-compose.yml`"), and the env var is read by
exactly one thing, `JobTrackerDbFactory`, which is design-time only — but **harmless-by-luck is still the guess I have been
making this milestone.** The real value is `…Password=jobtracker-dev-only`, from `api/docker-compose.yml`.

## 4. Decisions and invariants this checkpoint locks

1. **`TimeProvider` is the only clock the auth path reads.** `SessionPolicy` holds the four numbers (idle 30 min, hard cap 12 h, slide threshold 15 min, revoked retention 30 d). Expiry stays **one predicate over one column** because the hard cap is derived from immovable `created_at` — do not reintroduce a second timestamp to express the cap.
2. **Sliding is bounded by a threshold, not by every request**: a write happens only when fewer than half the window remains, so a hot client cannot turn reads into writes.
3. **Prune arms are ordered by what a row IS, not by which predicate it satisfies.** A revoked row always also satisfies `expires_at < now`; revocation must take precedence or `-052`'s audit retention is decoration. Verified organically — the first version ate the audit trail.
4. **CORS is credentials-on with explicit origins, and `"*"` in `Cors:AllowedOrigins` refuses to boot the app.** `WithOrigins("*")` treats `*` as a *literal* origin string: no browser ever sends `Origin: *`, so the policy matches nothing (measured `204`, zero headers) while looking permissive.
5. **Every problem document carries a `code` from `contracts/problem-codes.json` — including failures produced by the framework rather than by an endpoint.** `-069` added the binding path to that rule; the discriminator is what `-060`'s client table keys on, so a code that is not in the contract is a client that cannot map it.
6. **The fan-out is scoped by the record's `OwnerId`, not by the requester's identity.** They coincide for every route in this app (`-056`); conflating them is how an admin or bulk-import path would leak. Bus state remains **per-process** under `ASSUMPTION-m3-backend-api-002` (single instance through M5).
7. **Red / Green / record are three commits, and a Red that had to be rebuilt must be *proven* in a worktree before anyone calls it a Red.** `-068`'s was; the reproduction (`Failed: 2, Passed: 2`) is in that record.
8. **A mutation probe must assert its own anchor and print an applied-marker.** Two probes in this span were void — one silent (`$2` vs `$1`), one non-compiling (`if (false)`) — and both reported confident "vacuous" verdicts about experiments that never ran.

## 5. Blockers, risks, open questions

- **The one owner answer that gates four rows: Q4's harness origin.** `-063`, `-064`, `-065` and the Browser halves of `-067`/`-068` cannot be executed honestly until the spec's open question is settled — the harness's page origin (`127.0.0.1:4173`) and the API origin (`<sandbox-ip>:5080`) are **different sites** under Chromium's model, so a `SameSite=Lax` cookie is never sent and AC-12 would report a product failure that is only topology.
  > **SUPERSEDED 2026-09-12 16:46 UTC** — answered (a), and "gates four rows" overstated it: gates **two** (`-064`, `-065`), converts **one** into a restatement (`-063`, whose cross-site case cannot reach the antiforgery check under `Lax`), and makes **`-067`'s Browser half unachievable** in a same-origin topology. See §6's answer note.
- **Uncovered guard, found by this span's own probing**: `InvalidRequestBodyHandler` declines non-`BadHttpRequestException` failures and non-400 statuses. Probe B deleted the guard and **the full suite stayed 169/0** — so the reasoning is recorded but untested. It is `-069`'s practice task, and the record says so where the claim was made.
- **`GET /api/auth/session` still has no ladder row** although spec §4.3 promises the endpoint. Someone must either add the row or strike the clause.
- **Owner-side residue unchanged**: `-070`'s scoped-mutation-gate verdict · AC-3 restrike-or-restate (`0 unhandled exceptions` passes as written, failed as intended until `-057`) · the coverage audit · `-047`'s probe list · DECISION-001's paperwork box.
- **Frontend unchanged since `-062`**: 221 tests / 23 files, 3 runtime dependencies. AC-15's re-run will flag fake credential literals in `src/pages/loginPage.test.tsx`.

## 6. Pre-written owner question (the only thing that unblocks the queue)

> **ANSWERED — 2026-09-12 16:46 UTC: option (a)**, serve the harness page from the API's origin. Ratified as `DECISION-m4-auth-007` in the spec, and §5's blocker line below is superseded by the note appended to it.
>
> Two things the answer surfaced that the question did not ask, recorded here because they change what the queue actually holds:
>
> 1. **"(a)" does not unblock all four rows — it unblocks two and converts a third into a restatement.** Same-origin means **no CORS preflight ever occurs**, so `-067`'s Browser half is not deferred any more, it is **unachievable in this topology** and the property survives only at `-067`'s Integration seam (`CorsCredentialsTests`). And a **cross-site** POST under `SameSite=Lax` carries **no cookie at all**, so `-063`/AC-11's antiforgery check is never reached — the request dies at the session gate as `401`, which is the Lax defence, not the header defence. Run as written it would pass for the wrong reason. `-063` needs **two cases with separate positive controls**: same-site write without `X-CSRF-Token` → the header check bites; cross-site write → cookie absent → `401`. **That is an edit to a ratified row's promise, so it is the next owner question, not a code decision.** Unblocked as written: `-064` (AC-12) and `-065` (AC-14 — which must state which URL literal it built with, since the baseline is literal-dependent).
> 2. **The option text quoted a false AC list.** "`AC-12/13/14/18`" — AC-13 is verified and AC-18 does not exist. See the correction appended to §2; the question was still posted verbatim above because that is what was actually asked.

> **Q4, decided once, unblocks four rows.** The Playwright/CDP harness reaches the API at a different *site* than the page it loads, so `SameSite=Lax` will not carry the session cookie and AC-12 looks like a product defect. Which do you want: **(a)** serve the harness page from the API's origin (same-site, no topology lie, one dev-server change), **(b)** add a Development-only reverse proxy so both are same-origin, **(c)** accept `SameSite=None; Secure` in dev only, with an explicit note that production topology differs, or **(d)** leave AC-12/13/14/18 open and let them ship as "not verified in this milestone"? **Recommendation: (a)** — it keeps the cookie attributes the product will actually ship with, and (c) would make the test environment more permissive than production.

## 7. Hygiene scan (Phase 3), including what it caught

- **No secrets tracked.** No `.env`/key material in `git ls-files`; the only tracked config is `api/src/JobTracker.Api/appsettings.Development.json` (dev-only literals, already disclosed in the spec's threat model).
- **⚠️ A stale dev server was found running.** `-069`'s probe script hit a bash syntax error *after* its measurements and **before its own `kill` line**, so Kestrel (`pid 1080373`) and its still-live `dotnet run` parent (`1080317`) held `127.0.0.1:5199` for ~16 minutes. Both stopped; the port no longer answers. Consequence recorded: the probe run that printed `HTTP 400 … probs/validation` spoke to a build **older than the pointer fix**, so that output is cited only for status/type/title and never for the pointer value — the pointer evidence comes from the tests. `lsof`/`fuser` could not see the socket (the sandbox's `/proc/net/tcp` is empty of it), so it was found with `ps -eo pid,etimes,args`.
- **Dev database cleaned of this session's residue, and only that.** `applications` **4 → 2**, `users` **2 → 1**, `sessions` **2 → 0**. Removed: the two `Real Co` probe rows and `probe069@example.test`. **Left alone**: the two pre-existing `X` rows and `dev-login@example.test` — not mine to delete. Measured side-effect worth keeping: deleting the user **cascaded** its sessions, which is `-052`'s retention question seen from the other end.
- **No debug markers left** (`DEBUG-069`/`REMOVEME`): none in `api/src` or `src`. The Roslyn `VBCSCompiler` build server is intentionally still running.
- **Nothing uncommitted**: `git status --porcelain` empty before this record, empty after the docs commit that carries it.

## 8. Exactly one prioritised next action

**When the human says #58 is merged: reconcile `main`, verify the merge landed by both checks (PR state *and* the `TDD-EXEC-m4-authentication-069` marker in `main`), then post §6's Q4 question and stop.** The decision-free server-side queue is empty after `-069`, so asking is the work — and none of the four owed practice tasks (`-066` idle-window config · `-067` empty-vs-absent `AllowedOrigins` · `-068` the fan-out's linear scan · `-069` the untested guard) may be started uninvited.

> **DONE + SUPERSEDED 2026-09-12 16:46 UTC.** The merge was **found already done** (H3 measured `state:MERGED`,
> `47852a4`, 16:09 UTC) rather than announced, so the two-way verification ran against it: PR state **and** the
> `TDD-EXEC-m4-authentication-069` marker in `main` (count 1), with `main` ff-reconciled to `47852a4`, clean tree, and
> `f9482a5` proven an ancestor. **Q4 was posted and answered (a)** — see §6. **The next action is now the one §6's answer
> created: put the `-063`/AC-11 two-case restatement to the owner before Slice 5 writes it**, because option (a) makes a
> cross-site probe prove the Lax cookie rule rather than the antiforgery header, and silently "fixing" that in code would
> change a ratified row's promise. Then implement (a) and run `-064`. The four practice tasks remain unstarted and
> uninvited. *(The commit carrying these corrections is **PR #59**, branch `docs/m4-q4-decision-ac-fix` — published and
> awaiting review, because review, merge, tags and releases are not agent duties.)*

*End of checkpoint-001. Companion: [`handoff-001`](TASK-m4-authentication.handoff-001.md).*
