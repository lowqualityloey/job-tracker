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

`-069` (PR #58) is **open, awaiting human review**. It is the last decision-free server-side row: after it, `-063`/`-064`/`-065` and the Browser halves of `-067`/`-068` all wait on **one owner answer (Q4, harness origin)**.

The discipline does not relax under budget pressure, and this session is far over budget: the count of *disclosed* process failures in this span is **six** (two false Reds from harness holes, an invented timing cause withdrawn, a void mutation probe, a swallowed Red commit that had to be rebuilt and proven, and a probe script that died before its own cleanup line). Each is in the record where it happened. **The root cause of every one is writing from intent instead of measurement** — which is the thing to watch in yourself, not just in the previous session.

## 2. Mandatory validation pass — every command below was executed before being written here

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

## 5. Exactly one next action

**When the human says "#58 merged": reconcile `main`, prove the merge landed both ways (`gh pr view 58 --json state,mergedAt,mergeCommit` **and** the `TDD-EXEC-m4-authentication-069` marker in `main`), then post checkpoint §6's Q4 question verbatim and stop.**

Do **not** start any of the four owed practice tasks uninvited (`-066` idle window / hard cap from configuration · `-067` empty vs absent `Cors:AllowedOrigins` · `-068` the fan-out's linear scan · `-069` the untested handler guard), and do not invent a fifth ladder row: after `-069` the decision-free server-side queue is genuinely empty. Publishing the branch and opening PRs are agent duties; review, merge, tags, releases, deploys and any push to `main` are not.

## 6. Prewritten resume prompt

```markdown
# Session Resume: M4 Authentication, after -069 (PR #58 awaiting review)

Read `docs/tasks/TASK-m4-authentication.checkpoint-001.md` and
`docs/tasks/TASK-m4-authentication.handoff-001.md`, then run that file's §2 validation pass (H1–H10)
before editing anything. Work on `feat/m4-069-wrongtype-binding` @ `38ff6d2`, three commits over
`main@fea085c`. Ladder: 23 rows `-047`…`-069`, 21 EXEC records, AC 13 verified + 4 open = 17.
Gates: `source /tmp/dotenv.sh` for dotnet; ConnectionStrings__Default for the dev DB;
`cd api && dotnet test` (169 expected) and `npm run verify` (221 tests / 23 files).
Next action is §5's, and it is one action: on "merged", verify both ways and ask the owner Q4.
```

*End of handoff-001.*
