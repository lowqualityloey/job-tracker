# Handoff Record 001 — TASK-m4-authentication

- **Task ID**: `TASK-m4-authentication` · **Canonical record**: [`TASK-m4-authentication.md`](TASK-m4-authentication.md)
- **Specification**: [`docs/specs/2026-09-12-spec-m4-authentication.md`](../specs/2026-09-12-spec-m4-authentication.md) — approved by the merge of PR #34 (`fa4ed7d`), **not** by ticked boxes (spec §7's eight checkboxes are six-owner-only and still open; `DECISION-001`'s paperwork box remains the owner's)
- **Branch / audited revision**: `feat/m4-069-wrongtype-binding` @ `38ff6d2` (three commits over `main@fea085c`)
- **Handed over at**: 2026-09-12 15:55 UTC · **From**: Assistant, same session, `pk:checkpoint` · **To**: a fresh chat with no memory of this one
- **Checkpoint evidence**: [`checkpoint-001`](TASK-m4-authentication.checkpoint-001.md) (all numbers below come from that run, not from conversation memory)
- **Execution State**: `in_progress`. Deliberately **not** `handoff_ready`: nothing is blocked *for the agent*, and no resume condition is unmet. This document exists so that a context-window restart costs nothing. The receiver still runs §2 before editing anything.
- **Release-evaluation handoff fragment**: N/A. Nothing here authorises a release; `pk:ship` and the human own tags, releases, deploys and any push to `main`.

## 1. What the receiver is picking up

M4 is **21 slices in, one PR short of a milestone review**. Accounts, `__Host-JTSession` sessions, per-user ownership, logout, rotation, enumeration resistance, the error-code contract, the login UI, sliding/hard-capped expiry, CORS with credentials, owner-scoped SSE fan-out and the binding-failure contract are **merged and green**. The last four rows (`-066`…`-069`) were each **ratified into the ladder at execution time** — they were not in the plan, they were found by probing, and the human approved them by merging the PR that said so.

`-069` (PR #58) is **merged — `47852a4`, 2026-09-12 16:09 UTC** (it was described as "open, awaiting human review" when this
record was written; the pointer was simply overtaken between the two turns). It is the last decision-free server-side row.
**Q4 has since been answered — option (a), serve the harness from the API's origin — now `DECISION-m4-auth-007` in the
spec**, and that answer did **not** unblock the four rows §5's original text claimed: it unblocks `-064` (AC-12) and `-065`
(AC-14), **converts `-063`/AC-11 into a restatement** (a cross-site `Lax` POST carries no cookie, so the antiforgery check is
never reached — see §5's replacement below), and makes **`-067`'s Browser half unachievable** in a same-origin topology.
Also corrected here: **`-068` never had a Browser half** — its ladder seam is Integration only, so "the Browser halves of
`-067`/`-068`" overcounted by one. Lists in these records must be re-derived, not carried forward.

The discipline does not relax under budget pressure, and this session is far over budget: the count of *disclosed* process failures in this span is **six** (two false Reds from harness holes, an invented timing cause withdrawn, a void mutation probe, a swallowed Red commit that had to be rebuilt and proven, and a probe script that died before its own cleanup line). Each is in the record where it happened. **The root cause of every one is writing from intent instead of measurement** — which is the thing to watch in yourself, not just in the previous session.

## 2. Mandatory validation pass — every command below was executed before being written here

> **Status when this handoff was actually consumed (2026-09-12 16:46 UTC): two rows were already stale, and §5's trigger
> had fired without being announced.** **H3** — PR #58 was **merged** (`47852a4`, 16:09 UTC), not open: `gh pr view 58
> --json state,mergedAt,mergeCommit` returned `MERGED`. **H1** — HEAD was `f9482a5`, not `38ff6d2`, because the commit that
> carries these two records cannot contain its own hash — checkpoint §3 line 8 says exactly that about itself, one section
> above the table that contradicts it. Both were caught by **running** the commands, which is the only reason either is
> known. **The lesson for the next handoff author: never write a literal SHA for "the commit carrying this file"; derive it
> at read time** (`git log --grep='checkpoint' -1 --format=%h`).
> **H4's elided `…` in the connection string also cost a guess** — see checkpoint §3a's disclosure. The value is in
> `api/docker-compose.yml`, **and the suite does not need it**: Testcontainers starts its own PostgreSQL, which is why the
> guess was harmless rather than load-bearing. **H5 re-ran clean (221 / 23 files, exit 0). H4 re-ran 169/0 twice, but its
> first run failed one test that has not reproduced** — named in checkpoint §3a, cause deliberately unrecorded.

| # | Validate | Command | Measured at `38ff6d2` |
| :--- | :--- | :--- | :--- |
| H1 | You are on the branch you think you are | `git branch --show-current; git rev-parse --short HEAD; git status --porcelain \| wc -l` | `feat/m4-069-wrongtype-binding` · `38ff6d2` · **0** |
| H2 | Ancestry, not equality | `git merge-base --is-ancestor fea085c HEAD && echo OK` | **OK** |
| H3 | The PR carries your tip (a branch having a commit ≠ the PR carrying it) | `gh pr view 58 --json headRefOid --jq .headRefOid; git rev-parse HEAD` | identical, `38ff6d2d2c250943fece5e06a885e9b9ff92f15e` |
| H4 | API suite is green as claimed | `source /tmp/dotenv.sh; export ConnectionStrings__Default='…'; cd api && dotnet test` | **169 passed / 0 failed / 0 skipped**, `grep -cE 'error CS\|warning CS'` → **0** |
| H5 | Frontend gate | `npm run verify` (never bare `npm test` — watch mode never exits) | **exit 0**, 23 files / **221 tests**, build 1.86 s |
| H6 | Doc counters are measured, never recalled | the edit script's asserts (ladder rows, EXEC records, AC verified + open) | **23** rows · **21** records · **13 + 4 = 17** ACs · **3** §6 amendments |
| H7 | The M3 net guard is live | `grep -n "Ac13ExpectedCases" api/tests/JobTracker.Api.Tests/M3RegressionNetTests.cs` | `private const int Ac13ExpectedCases = 65;` |
| H8 | Cookie contract intact | `grep -rc "__Host-JTSession" api/src \| grep -v ':0'` | **2** sites |
| H9 | No orphan server from the last session | `curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:5199/api/applications` | **000 / no listener** (an orphan was found and stopped during this checkpoint) |
| H10 | Docker `api-db-1` (`127.0.0.1:5432`) is this repo's dev DB | `docker inspect api-db-1 --format '{{ index .Config.Labels "com.docker.compose.project.working_dir" }}'` | `/home/heyloey/personal/job-tracker/api` |

## 3. Do not undo these

- **Every problem document carries a `code`, and that code must exist in `contracts/problem-codes.json`.** `-060` reflects over the factories, `-069`'s last case asserts the binding path against the same JSON. A new code invented in `src` is a client that cannot map it.
- **`TimeProvider` is the only clock in the auth path** (`SessionGate`, `AuthCatalog` read it; `SessionPolicy` holds the numbers). A fake clock must be anchored at real `UtcNow`, because `created_at` is filled by PostgreSQL's `HasDefaultValueSql`, not by the app.
- **Never reintroduce a second timestamp to express the hard cap.** The cap is `created_at + 12h` precisely so expiry stays one predicate over one column.
- **Prune arms stay ordered by what a row IS**: revoked before expired, or `-052`'s audit retention silently disappears (this happened, and a test caught it).
- **CORS keeps credentials-on with explicit origins; `"*"` in configuration throws at boot.** Measured fact worth re-learning before arguing: `WithOrigins("*")` matches a literal origin string no browser sends.
- **SSE fan-out stays scoped by the record's `OwnerId`.** Per-process bus state remains acceptable only under `ASSUMPTION-m3-backend-api-002` (single instance through M5) — M5 must revisit it (PG `NOTIFY`, not Redis).
- **`M3RegressionNetTests` guards 65 M3 API tests**: adding classes is safe, deleting or renaming them is a visible decision.
- **Frontend**: `src/data/` owns `localStorage`, pages never touch it; `unauthorized` is the ninth repository variant; the client re-reads the catalog on every SSE frame (`-062`).

## 4. What will trick you here

1. **Testcontainers and `WebApplicationFactory` make a broken harness look like a broken product.** Three times in this span a "Red" was really the harness: a missing `[Collection(PostgresCollection.Name)]` (fixture never injected → every test "fails"), a `QueryAsync<long>` over `count(*)::int` (`InvalidCastException`), and a fresh `WebApplicationFactory` per call site — the bus is per-process, so a *new host per request* means no stream ever sees a write, which imitates the exact defect being tested. **Read the failure text before believing the count.**
2. **You cannot peek at an SSE stream by cancelling a read.** A `CancellationToken` on `Stream.ReadAsync` of an HTTP response aborts the response; the next read throws `IOException`. Use a background pump started in the constructor and sample its buffer.
3. **A mutation probe that fails to compile still reports a result.** Both void probes here printed confident verdicts (`"caught by: 0 distinct cases"`) about experiments that never ran. Assert your anchor, print `MUTATION APPLIED`, and check the build succeeded before interpreting the count.
4. **`grep` returning nothing is not an answer.** `Assert.Equal(65…)` matches nothing (H7 shows why), a `Duration: [0-9]+ s` pattern missed `1 m 14 s`, and `grep -E` filtering out `warning` once let a commit claim "0 warnings" about a tree nobody had rebuilt. Read whole output.
5. **`bash -c` has no state between calls.** `dotnet` is absent unless `source /tmp/dotenv.sh` runs **inside the script** — a probe script that inherits the env from the calling shell dies with `dotnet: command not found`, and a script that dies mid-run skips its own cleanup (H9).
6. **Timing claims need a measurement, and durations in this suite are real.** A 4–7 s class once appeared as 1 m 36 s in a full run; an invented cause (undisposed hosts) was disproved by disposing them. Record durations as measured and leave the cause unnamed if you have not established it.
7. **The `write` tool refuses a file the shell deleted or touched**, and multi-line anchor patches abort before writing when lines are not adjacent. Prefer a heredoc for C# (raw strings survive it) and assert every anchor count.
8. **Any git operation that rewrites the worktree — `checkout`, `pull --ff-only`, `stash` — invalidates every read-cache at once**, so the next `edit` on *any* file fails with "file changed since it was read" even though its bytes are identical (confirmed at `47852a4`: reconciling `main` cost four re-reads on files whose content had not changed). **Re-read after reconciling, before editing — and never let the refusal tempt a `write` of the whole file from memory**, which is how a projection gets "restored" to what it was believed to say.
9. **Replacing a heading anchor silently eats the heading.** An `edit` whose `old_string` is `## 4. …` and whose `new_string` omits it deletes the section title while leaving its body: `grep -n '^## '` on the result is a one-call check that caught this on checkpoint-001. Structural edits deserve a structural re-read.

## 5. Exactly one next action

> **DONE — this section's trigger fired and its action is complete, superseded 2026-09-12 16:46 UTC.** #58 was found
> **merged** (`47852a4`) rather than announced; the merge was proven both ways (`state:MERGED` **and** the
> `TDD-EXEC-m4-authentication-069` marker in `main`, count 1); `main` was ff-reconciled to `47852a4` with a clean tree and
> `f9482a5` proven its ancestor; **Q4 was posted verbatim and answered (a)**. The live action is the one below.

**Ask the owner the one question Q4's answer created, then stop: does `-063`/AC-11 get restated as two cases?** Under
same-origin serving, a cross-site `Lax` POST arrives with **no cookie**, so it is refused by the session gate (`401`) and the
antiforgery header check `-063` exists to prove **is never reached** — executed as written, it would go green proving the
wrong property. The honest shape is **two cases, each with its own positive control**: same-site write *without*
`X-CSRF-Token` → the header check bites; cross-site write → cookie absent → `401`. Changing what a ratified row promises is
an owner decision, not an implementation detail. **After that answer:** implement (a) — serve the harness from the API's
origin — and run `-064` (AC-12), which is the row the whole question existed to unblock.

Do **not** start any of the four owed practice tasks uninvited (`-066` idle window / hard cap from configuration · `-067` empty vs absent `Cors:AllowedOrigins` · `-068` the fan-out's linear scan · `-069` the untested handler guard), and do not invent a fifth ladder row: the decision-free **server-side** queue is genuinely empty after `-069` — everything Q4 unlocked is Browser or Build. Publishing the branch and opening PRs are agent duties; review, merge, tags, releases, deploys and any push to `main` are not.

## 6. Prewritten resume prompt

> **Regenerated in place, 2026-09-12 16:46 UTC.** The previous version of this block — the text this session was actually
> fed, which stated PR #58 was open, HEAD was `38ff6d2`, and the next action was to ask Q4 — is recoverable verbatim with
> `git show 38ff6d2:docs/tasks/TASK-m4-authentication.handoff-001.md`. It is not kept inline because a resume prompt that
> is wrong is worse than no resume prompt: it is the one artifact a fresh session is told to trust without measuring.
> Two fixes baked into the new text: **it no longer names a literal HEAD SHA** (the record commit cannot contain its own
> hash — derive it at read time), and **it no longer lists open ACs by number** (that is how `AC-13`, verified, and
> `AC-18`, nonexistent, survived a whole checkpoint).

```markdown
# Session Resume: M4 Authentication — after -069, Q4 answered (a)

Read `docs/tasks/TASK-m4-authentication.checkpoint-001.md` (§3a, §5 and §6 are the parts that moved since it was
written) and `docs/tasks/TASK-m4-authentication.handoff-001.md`, then run §2's H1–H10 BEFORE editing anything.

Derive your position, do not assume it: `git rev-parse --abbrev-ref HEAD`, `git log --oneline origin/main..HEAD`,
`gh pr view --json state,headRefOid` for whatever PR is open (check `gh pr list`, not this prompt). As of this writing
`main` is `47852a4` (`-069` merged, local `main` reconciled) and the only unmerged work is a docs branch.

Ladder: **23 rows `-047`…`-069`**, **21 EXEC records**, **3 dated §6 amendments**, AC **13 verified + 4 open = 17**.
The open ACs are **AC-3, AC-11, AC-12, AC-14** — read them from `grep -E '^- \[ \] \*\*AC-'
docs/tasks/TASK-m4-authentication.md`, never from a projection.

Gates: `source /tmp/dotenv.sh` for dotnet (a fresh shell has none). The API suite starts its **own** Testcontainers
PostgreSQL, so `ConnectionStrings__Default` is **not** needed for `dotnet test` — only for `dotnet ef` and `dotnet run`.
`cd api && dotnet test` → **169 passed / 0 failed / 0 skipped** (expect ~1 m; read `Skipped` too) and
`npm run verify` → **221 tests / 23 files**, exit 0. **Never pipe a gate into `tail`/`head`** — a piped `dotnet test`
reports the pipe's exit status, and that has already hidden one real failure here. Redirect to a file and read `$?`.

Known flaky, named: `ApplicationsCommandTests.Stale_if_match_conflicts_without_writing:127`
(`(updated_at > created_at)::int` == 0) has been seen **once in four full runs** and not reproduced since; cause is
deliberately unnamed. If it recurs, save the whole log and dump that row's two timestamps before summarising.

**Next action — one, and it is a question, not a commit**: Q4 was answered **(a)** — serve the browser harness from the
**API's own origin** (`DECISION-m4-auth-007` in the spec). That unblocks `-064` (AC-12) and `-065` (AC-14), but it turns
`-063`/AC-11 into a **restatement**: under same-origin serving a cross-site `SameSite=Lax` POST carries **no cookie**, so
the antiforgery check `-063` exists to prove is never reached. Ask the owner whether `-063` splits into two cases
(same-site without the token → header check bites; cross-site → `401` from the session gate), each with its own positive
control. Editing a ratified row's promise is theirs to approve. **Then** implement (a) and run `-064`.

Do not start the four owed practice tasks (`-066` idle-window/hard-cap config · `-067` empty vs absent `AllowedOrigins` ·
`-068` the fan-out's linear scan · `-069` the untested handler guard — that claim is reasoning, not measurement: probe B
deleted the guard and the suite stayed 169/0) uninvited. Push, PR, and stop are agent duties; review, merge, tags,
releases, deploys, and any push to `main` are human-only.
```

*End of handoff-001.*
