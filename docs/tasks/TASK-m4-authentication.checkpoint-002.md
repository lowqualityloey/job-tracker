# Checkpoint Record 002 — TASK-m4-authentication

> **Amended at 2026-09-13 00:41 UTC, in the same session, on the human's second `pk:checkpoint` request.** No sentence
> below was deleted or rewritten; this banner and the §8/branch notes are appended at their own sites. Three things the
> original file could not know, in descending order of importance:
>
> 1. **The PR carrying this record is #71 and it is still OPEN** (`state=OPEN`, `main` unchanged at `58daf8a`,
>    `git cat-file -e origin/main:…checkpoint-002.md` → fails, measured 00:41). The "name the artifact, not the ticket"
>    rule in §8 held precisely because it had to: #70 merged mid-push and orphaned the first attempt, and #71 has not
>    landed. **The resume condition in §8 is therefore NOT satisfied** — this is still `handoff_ready`, and the stop state
>    stands for a second reason now: the human asked for a checkpoint, not for `-075`.
> 2. **The working tree is dirty with changes that are not the agent's.** Four paths moved while this record was being
>    written: the `.promptkit` submodule pointer (`a1eb608 → eecd77b`), a 114-line rewrite of `AGENTS.md`'s operating-system
>    block (Better-PromptKit → **PromptKit OS**), and two new untracked files,
>    `.github/ISSUE_TEMPLATE/…` and `.github/pull_request_template.md`. None of it was authored here, none of it is in
>    #71, and per `AGENTS.md`'s own rules it is **not** this PR's to commit: `.promptkit/` is a read-only submodule, and
>    an unrelated tooling sweep folded into a docs PR would make #71's message false about its contents. It is surfaced as
>    an owner decision instead, and it is the reason §7's hygiene scan now has a real finding rather than a clean bill.
> 3. **`AGENTS.md`'s commit-discipline count survived the rewrite at 10** — re-derived, not assumed:
>    `awk '/^### Commit discipline/,/^CI \(/' AGENTS.md | grep -c '^- \*\*'` → **10** — which matters because the tenth
>    rule (bound a projection edit by the section) was added by *this* session and the tooling rewrite touched the same
>    list. One of the ten is now at risk of being upstream-overwritten, so it is worth checking after any future sync that
>    the rule about deleting 51 lines is still there.
>
> **Everything else in this record still measures the same at 00:41:** §4 **23** invariants · ladder **27** rows ·
> **26** EXEC ids · ACs **16 + 1** (open: **AC-3**) · AGENTS rules **10** · CI on #71 `verify` pass / `verify-api` pass
> (skipped internally, steps 4–5) / `bundle-baseline-recheck` skipping.
>
> **Read §5, §7 and §8 alongside this banner, not instead of it.**

- **Task ID**: `TASK-m4-authentication` · **Canonical record**: [`TASK-m4-authentication.md`](TASK-m4-authentication.md) (§6 carries the per-row `TDD-EXEC` records; this file is a projection, not a replacement — the Task Record stays authoritative)
- **Specification / planning**: [`docs/specs/2026-09-12-spec-m4-authentication.md`](../specs/2026-09-12-spec-m4-authentication.md) (approved by PR #34, `fa4ed7d`) · ladder [`docs/tests/2026-09-12-test-m4-authentication.md`](../tests/2026-09-12-test-m4-authentication.md) · grill [`docs/reviews/2026-09-12-m4-plan-grill.md`](../reviews/2026-09-12-m4-plan-grill.md) · predecessor [`checkpoint-001`](TASK-m4-authentication.checkpoint-001.md), whose `handoff-001` was consumed and validated at this session's start
- **Checkpoint at**: 2026-09-13 ≈00:20 UTC · **Trigger**: requested by the human (`pk:checkpoint`) after context compaction, at a milestone boundary — six rows delivered and the behaviour ladder empty for the first time in the milestone.
- **Execution State**: **`handoff_ready`** — a **stop state**. No further implementation edits, commits, pull-request actions or task switches are permitted from here; the resume condition is in §8. The commits that *complete this checkpoint* (its two records plus the `docs/STATE.md` sync, and the one PR-body amendment that names them) are inside this record's own scope, not after it — the distinction is stated because the previous checkpoint was read mid-write by its own session and turned out to be wrong about its own branch.
- **Branch / audited revision**: `docs/m4-076-state-projection` @ `38fa07c` when this file's prose was written, **1 commit ahead of `main`** (`8fabe48`), `git status --porcelain` empty at audit time, `git merge-base --is-ancestor 38fa07c origin/main` = **NO** (correct: not merged yet), and **PR #70 open** against `main`.
  > The value that will be true after this commit exists is **not recorded here**: a hash cannot contain its own commit's hash, and this session has already retracted three identifiers written before their subjects existed. Find this record's carrier with `git log --grep='pk:checkpoint' -1 --format='%h %s'`

> **Amended in the commit that carries this file, because the sentence above it was overtaken mid-push.** #70 merged at
> **00:27:51Z** — after this record was written, before its commit reached `main`. The projection landed (`main` =
> `58daf8a`); the two records here and the §2/§4/§5/§7/§8 sync did **not**: `git cat-file -e main:docs/tasks/…002.md`
> fails and `git merge-base --is-ancestor <this commit> main` said **NO**. That is the **fourth** instance today of an
> identifier or artifact being written while its own future was unsettled, and the first where the *act of checking*
> (`gh pr view`, twice, returning the pre-merge head) looked like a stuck push rather than a closed PR. The rule kept
> working: reconcile by ancestry, verify a merge by payload, and never conclude "the PR is slow" without asking whether
> the door shut. The carrier is therefore a **different PR** than the one this file originally named — which is exactly
> why the line above deliberately does not assert a number. and validate **ancestry, not equality**.
- **Release-evaluation handoff fragment**: **N/A** — no candidate, tag, QA gate or release decision exists for M4. Nothing in this file authorises a tag, release, publication, remote operation, deployment or rollback; those stay `pk:ship` + the human.

---

## 1. Objective (unchanged)

Deliver M4 authentication per the approved spec: server-side sessions behind `__Host-` cookies, an antiforgery gate, owner-scoped data and events, credentialed CORS, and a browser-verifiable proof of each — while keeping every mechanism explainable in an interview (`AGENTS.md`).

## 2. Completed since checkpoint-001

Six rows, five merged PRs, three acceptance criteria moved. Counted from the files at audit time, not recalled:

| row | what it proved | PR | merge SHA |
| :--- | :--- | :--- | :--- |
| `-071` | the API serves the built SPA as its own origin (`DECISION-007 (a′)`'s enabling half) | #62 | `21f1f45` |
| `-064` | cross-tab SSE works **authenticated**, in real Chromium (AC-12) | #63 | `9d4a2f7` |
| `-063` | the antiforgery gate, both ratified cases, each with its positive control (AC-11) | #64 | `893038f` |
| `-065` | the login-route bundle delta **1,242 B < 3,072 B**, baseline rebuilt not quoted (AC-14) | #65 | `e4d1d2f` |
| `-074` | the data-protection key ring survives its process (`data_protection_keys`) | #67 | `e421d5e` |
| `-076` | AC-14 is a CI step, not a recollection (`Bundle gate` in `verify`) | #69 | `8fabe48` |

Plus the spike that unblocked `-063/-064/-065`: PR #60 (`7e13914`) and #61 (`1819bbb`), the cookie-jar host-prefix probes.

**Ledger, re-derived:** ladder **27** rows (`-047`…`-069`, `-071`, `-074`, `-075`, `-076`; **`-070` permanently unassigned**; `-072`/`-073` reserved proposals, not rows) · **26** distinct `TDD-EXEC` ids · ACs **16 verified + 1 open**, the open one **AC-3**, read out by id · **no behaviour row is unexecuted**, first time in the milestone.

**Artifacts added** (all in `main` except the last two, which are in #70): `Auth/AntiforgeryGate.cs`, `Auth/PostgresKeyRing.cs`, `Auth/PostgresKeyRingConfiguration.cs`, `Data/DataProtectionKey.cs`, migration `AddDataProtectionKeyRing`, `tests/…/KeyRingPersistenceTests.cs`, `tests/…/Infrastructure/TestCookies.cs`, `tests/browser/csrfGate.mjs`, `tests/browser/run-063.sh`, `tests/build/bundleDelta.sh`, `tests/build/bundleGate.sh`, `tests/build/bundle-baseline.json`, `tests/build/restartKeyRingProbe.sh`, the `ci.yml` gate step and the `bundle-baseline-recheck` job, and **AGENTS.md's tenth commit-discipline rule** (mirrored into `.agents/rules/job-tracker-learning.md` in the same commit).

## 3. Verification evidence — every row re-executed or read from a run, none carried over

| claim | how it was established | result |
| :--- | :--- | :--- |
| API suite | `dotnet test api/JobTracker.slnx` on merged `main` | **189 / 0 failed / 0 skipped**, exit 0, 1 m 18 s |
| Frontend gate | `npm run verify` | exit 0 · **227 tests / 23 files** · build 1.28–1.46 s |
| AC-11 | `bash tests/browser/run-063.sh` | **11/11**, exit 0; scratch DB counted **0** probe rows and **≥2** session rows |
| AC-12 regression after the gate | `bash tests/browser/run-064.sh` | **7/7**, negative control fails 4 of 5, `deleted 204` |
| AC-14 | `bash tests/build/bundleDelta.sh` (four builds, both trees) | head **62,610 B** vs rebuilt baseline **61,368 B** → **+1,242 B**; baseline reproduced Slice 0's 60,118 / 61,368 **byte-exact, twice** (four PRs apart) |
| AC-14's gate has teeth | `GATE_BYTES=1 bash tests/build/bundleGate.sh` | **exit 1** with the breach text |
| The gate works on a runner | CI step list on #69 and #70 | `Bundle gate (AC-14, -076)` **success**, printing the same **62,610 B** as local |
| The key-ring fix is real | `bash tests/build/restartKeyRingProbe.sh` | 201 → restart → **201** (was 403), session **200**, control 201, **key rows 1 / 1** |
| Merged payloads | `git show <rev>:<path>` + the row's `TDD-EXEC` marker in `main` | verified for #67, #68, #69 — **not** inferred from `state=MERGED` |
| ran-vs-skipped | `gh api …/actions/jobs/<id>` step conclusions | #67: step 5 **success**, `verify-api` 2 m 22 s · #70: steps 4–5 **skipped**, 5 s |
| CI on `main` | `gh run list --branch main` | `completed success` at each reconciliation |

## 4. Decisions and invariants this checkpoint locks

**Ratified invariants already in `STATE.md` §4 and still holding:** `TimeProvider` is the only clock the auth path reads · CORS credentials-on with explicit origins, `"*"` refuses boot · every problem document carries a code present in `contracts/problem-codes.json` · SSE fan-out scoped by `OwnerId` · no state-changing `GET`.

**Three new §4 entries added by this checkpoint** (which moves §4's accurate count from **20 to 23** — see §7's note on that number):

- **I-21 — `UseAntiforgeryGate()` registers strictly after `UseSessionGate()`.** The 401/403 asymmetry *is* the diagnosis: anonymous ⇒ 401, authenticated-without-token ⇒ 403. Reversing the two lines silently moves `DataRouteAuthTests`' ratified 401s and makes every refusal unattributable.
- **I-22 — the data-protection key ring lives in `data_protection_keys`.** Reverting it to the framework default is not a neutral change: it restores a fault with **no error message to mark it** — a 403 in somebody else's request, days after the deploy that caused it.
- **I-23 — `__Host-JTCsrf` is readable from JavaScript on purpose.** A future security sweep will try to add `HttpOnly` and thereby make the gate unreachable by design. The prefix still forbids `Domain`, `Path=/`, and `Secure`-required; only `HttpOnly` is declined, and the reason is in `AntiforgeryGate.cs`.

**Agent-decided, recorded as such and *not* owner ratification** (standing instruction: *"decide your own recommendation from now on"*): **D-4** data protection over a hand-rolled HMAC (deviation from DECISION-m4-auth-006's literal mechanism, noted under that decision twice) · **D-5** the `antiforgery` wire code maps to the existing `unauthorized` client variant, no tenth `RepositoryError` · **D-6** logout exempt from the gate; `OPTIONS`/`HEAD` outside the unsafe-verb list · **D-7** `-065` compares against a rebuilt baseline and publishes the flag-off figure alongside the gated one · **D-8** `-076` written as a row instead of silently extending CI's contract inside `-065` · **D-9** the shipped EF Core DataProtection package **declined** because AC-15's evidence counts API packages, with the swap costed and the ~60 lines' weakness named (no revocation) · **D-10** the per-PR gate quotes a provenance-carrying fixture and a dispatch-only job re-proves it; **no `schedule:` invented**, so a drifted fixture will not nag.

## 5. Blockers, risks, open questions

**Nothing is blocked for the agent.** What waits on the human, each with its condition:

1. **AC-3** — the only open acceptance criterion, gated on a **restrike-or-restate** decision, not on work.
2. **Whether a two-origin deployment is actually the plan.** `docs/aws-deployment.md` — cited as a source in **six places across four documents** — **has never existed in this repository's history** (`git log --all --diff-filter=A -- docs/aws-deployment.md` → nothing; no `docs/` file names ECS/Fargate/Beanstalk/App Runner; README's AWS plan is six words). It decides `-073` (a proposed row that exists only to probe a topology nobody has chosen) and partially re-scopes DECISION-m4-auth-007's rationale. Phantoms found this session, all now annotated in place: that file, the `TDD-PRACTICE-*` ID series, `PersistKeysTo*` in the shared framework, and `-070`'s "scoped-mutation-gate verdict".
3. **D-9 / D-10** — keep the hand-rolled key store and the quoted fixture, or take the package / a schedule.
4. Owed paperwork: spec §7's six owner-only boxes · `DECISION-m4-auth-001`'s paperwork box · M3's `[/]`→`[x]` · the coverage audit · `-047`'s probe list · **`GET /api/auth/session` has spec §4.3 promising it and no ladder row**.
5. **Known non-reproduction, still open**: `ApplicationsCommandTests.Stale_if_match_conflicts_without_writing` failed **once** at `f9482a5` (the `updated_at > created_at` probe returned 0) and passed in every run since; cause **not** established. Two sightings, one attributed.
6. Accepted and recorded: same-origin serving means **no CORS preflight ever occurs**, so AC-16's browser-half is unachievable and lives only in `CorsCredentialsTests`; and all browser evidence bypasses certificate validation by CDP, so a human still meets the interstitial (`-072`, the `https` launch profile, is the unstarted answer).
7. **Environment residue, deliberately not touched**: `git worktree list` shows a pre-existing `/tmp/baseline` @ `84bf560` that this session did not create, and `127.0.0.1:4173` is held by another process (it blocked `-063`'s cross-site server once; the runner moved to 4179).

## 6. Scope changes since checkpoint-001

- **Ladder grew 24 → 27 rows** by three dated §6 amendments, appended and never renumbered: `-074`, `-075`, `-076`. Two of the three are rows this milestone produced **about its own tooling and gates**, which is worth naming as a shift in character rather than a quiet addition.
- **`STATE.md` §3A grew measurably at every one of six boundaries**, and §4 gained three invariants. The open question in §7 of #70's review list — split state from incident log? — is unresolved and is now a checkpoint item rather than an aside.
- **AGENTS.md gained a tenth commit-discipline rule** (*bound a projection edit by the section, never by "the next bullet"*), and its own header count was corrected to ten **with the command that derives it**.
- **No new runtime dependency anywhere**: frontend 3 / dev 19 unchanged; API `PackageReference` **4** (test project **6**). `-074` explicitly declined the package that would have changed that number, and `-076`'s `dotnet-ef` is a tool, not a dependency.
- **No scope drift into M5**: `ASSUMPTION-m3-backend-api-002` (single instance) still holds for the in-process bus; `-074` made *auth tokens* instance-independent and nothing else.

## 7. Hygiene scan (Phase 3)

- `git status --porcelain` empty at every commit; `git diff --stat` clean; no `.env`, credential file or `.pem` untracked (the harness certs are generated into `/tmp` with a 2-day validity and none survive there now: `ls /tmp/*.pem` → **0**).
- **No orphan processes or databases**: `pgrep -x JobTracker.Api` → 0; scratch DBs matching `jobtracker_p%`/`jobtracker_e2e%` → **0**; probe ports unbound. The one `dotnet` process found was **identified before being left alone** (`/home/heyloey/.dotnet/sdk/…/MSBuild.dll`, the persistent build node — killing it would have cost the next session build time for no gain).
- `console.log` calls in `tests/browser/renderLatency.mjs` are the M3 harness printing measured samples, **not** debug probes; no `[DEBUG-*]` markers anywhere.
- Uncommitted work at checkpoint time: **none**. Everything delivered sits in merged commits or in PR #70.

### And what the hygiene of *checking* caught, which is the part worth keeping

**A number I cited four times today as "the invariants check" was not measuring invariants.** `grep -cE '^[0-9]+\. \*\*' docs/STATE.md` → **18** is *top-level, bold-only, across every section*; §4's items 10–16 begin with a backtick rather than `**`, so they were invisible to it, and items from §7 were silently included. The accurate check is scoped and formatting-agnostic:

```
sed -n '/^## 4\./,/^## 5\./p' docs/STATE.md | grep -cE '^[[:space:]]*[0-9]+\.'   # → 20 now, 23 after I-21…I-23
```

**A canary can fire correctly while measuring the wrong thing.** It did its job — 18 → 5 exposed the 51-line deletion — and every conclusion drawn from it ("both equal to `HEAD`") survived, because equality against a baseline does not require the number to mean what its label claimed. The label did, and four commit messages repeated it. Related, same family, all caught in one session: a working-tree grep used as evidence about documents is **destroyed by the act of documenting** (`-070`'s `scoped.mutation` disproof: 2 files → 5; the practice-task count 4 → 5 because a sentence quoted the command; the `TDD-PRACTICE` absence check falsified by its own correction). Absence and count claims either name a **revision** or name nothing.

## 8. Exactly one prioritised next action

> **`-075` — extract the shared browser-harness recipe, and prove the extraction by re-running `-063` (11/11) and `-064` (7/7) against it.**

- **Resume condition for this stop state**: the human merges **the PR that carries these two records** (identified by
  `git log --grep='pk:checkpoint' -1` on the remote branch, not by a number guessed at write time — #70 was open when that
  condition was first written and merged before this commit landed, so the condition now names the *artifact*, not the
  ticket). Then reconcile both ways — `gh pr view <n> --json state,mergeCommit` **and** `git cat-file -e
  main:docs/tasks/TASK-m4-authentication.checkpoint-002.md` — **or** the human names a different target: the two owner
  decisions in §5.2 and §5.3 would each legitimately outrank `-075`.
- **Why this row and not the practice tasks**: `-075` is the last open `◆` row, and it is the only one that prevents a *repetition cost* already paid today — two CDP harnesses and three boot scripts each re-implement the same recipe, and two fixes (DROP DATABASE `WITH (FORCE)`; killing `JobTracker.Api` by exact name because `dotnet run` does not forward signals) were applied twice in one day.
- **What it genuinely costs**, stated so it is not discovered late: the extraction is only verified by **two real Chromium runs** (~5 minutes each, Docker in the loop; both have flaked at least once this session, including one transient `DockerContainerNotFoundException` that a re-run resolved). Re-running the evidence is the row; refactoring without it is editing.
- **Status of this action at the 00:41 amendment**: **not started, and not permitted to start.** #71 is open, so these records are not in `main`; the dirty tree (banner item 2) also bars a clean start under `AGENTS.md`'s milestone-boundary rule. Nothing about `-075` itself changed.
- **Order after it**: the four practice tasks (`grep -c '^- \*\*Practice task' docs/tasks/TASK-m4-authentication.md` → **4**; the natural `AGENTS.md step 9` command reads **5** because a sentence quotes it — see §7), then whatever the §5 owner decisions unblock.
- **The command that makes `-076`'s gate honest, if the fixture is ever doubted**: `RESULT_FILE=/tmp/x.env bash tests/build/bundleDelta.sh`, then compare `$REPRODUCED_BASELINE_ON_B` against `tests/build/bundle-baseline.json`.

*One further note this record owes its predecessor: `handoff-002` carries the receiver's validation pass. This file is a projection of §6 of the Task Record; if the two disagree, **the Task Record wins** and execution stays `checkpoint_due` until reconciled.*
