# Handoff Record 002 — TASK-m4-authentication

**Pairs with**: [`checkpoint-002`](TASK-m4-authentication.checkpoint-002.md) (the state) · **Canonical source**: [`TASK-m4-authentication.md`](TASK-m4-authentication.md) §6 — **if this file and §6 disagree, §6 wins** and execution stays `checkpoint_due` until reconciled.
**Boundary**: session end at 2026-09-13 ≈00:20 UTC, `handoff_ready`. Six rows delivered, five PRs merged, the behaviour ladder empty for the first time in the milestone, one PR (#70) open.

## 1. What the receiver is picking up

M4 authentication is **functionally complete and CI-gated**: sessions behind `__Host-JTSession`, a login-issued antiforgery token in a JS-readable `__Host-JTCsrf` echoed as `X-CSRF-Token`, owner-scoped reads and SSE fan-out, `TimeProvider`-driven expiry with prune, credentialed CORS with `"*"` refusing boot, the API serving the built SPA on its own origin, and AC-14's bundle budget now enforced by a workflow step. What is left is not feature work: **`-075`** (harness recipe), four practice tasks, and **decisions that belong to the human** (AC-3; whether a two-origin deployment is real; the package/fixture trades D-9/D-10).

Open at the boundary: the PR carrying these two records and the STATE sync. **Do not look for it by number:** #70 (the projection this was folded onto) merged at 00:27:51Z before this commit was pushed, so these files ride a later PR — find it with `git log --grep='pk:checkpoint' -1 --format='%h %s'` and confirm with `git cat-file -e origin/main:docs/tasks/TASK-m4-authentication.checkpoint-002.md`. H2/H3 below are written to answer that question mechanically.

## 2. Mandatory validation pass — run every command before editing

| # | Claim to re-establish | Command (run it; do not trust this file) |
| :--- | :--- | :--- |
| H1 | You are on the branch you think you are — **each bash call is a new shell** | `git branch --show-current` · `git rev-parse --short HEAD` |
| H2 | `main` is where checkpoint-002 says, and #70's payload is in it or not | `git fetch origin main && git rev-parse --short origin/main` (was `8fabe48`) · `git merge-base --is-ancestor 38fa07c origin/main && echo merged \|\| echo still-open` |
| H3 | **A merged PR is a closed door on its branch.** Verify a merge by payload, never by `state` alone, and reconcile **before** branching | `gh pr view 70 --json state,mergedAt,mergeCommit` **and** `git show origin/main:docs/tasks/TASK-m4-authentication.checkpoint-002.md \| wc -l` |
| H4 | API suite green as claimed | `source /tmp/dotenv.sh && dotnet test api/JobTracker.slnx --nologo` → **189 / 0 failed / 0 skipped**, exit 0. Read the whole tail, not `tail -1` |
| H5 | Frontend gate green | `npm run verify` → exit 0 · **227 tests / 23 files**. Never bare `npm test` (watch mode, never exits) |
| H6 | Doc counters **measured**, not recalled | ladder `grep -cE '^\| `…-0(4[7-9]\|[5-7][0-9])`' docs/tests/2026-09-12-test-m4-authentication.md` → **27** · EXEC `grep -oE 'TDD-EXEC-m4-authentication-[0-9]+' docs/tasks/TASK-m4-authentication.md \| sort -u \| wc -l` → **26** · ACs `grep -c '^- \[x\] \*\*AC-'` → **16**, `' ^- [ ]'` → **1**, and read the open **id** (AC-3) rather than inferring it |
| H7 | **§4's invariant count is 23, and the wrong command reports 18** | `sed -n '/^## 4\./,/^## 5\./p' docs/STATE.md \| grep -cE '^[[:space:]]*[0-9]+\.'` → **23** (was 20 before I-21…I-23). Do **not** use `grep -cE '^[0-9]+\. \*\*'` — it is bold-only, top-level, and spans every section |
| H8 | Cookie contract intact | `grep -rn "__Host-JTCsrf\|__Host-JTSession" api/src/JobTracker.Api/Auth/ \| grep -c __Host` → present in `AntiforgeryGate.cs` and `AuthCatalog.cs`; **`Secure` and the names never vary by environment**, and `__Host-JTCsrf` is **deliberately not `HttpOnly`** (I-23) |
| H9 | Middleware order still 401-then-403 (I-21) | `grep -n "UseSessionGate\|UseAntiforgeryGate" api/src/JobTracker.Api/Program.cs` → **session first**. Swapping them silently moves `DataRouteAuthTests`' ratified 401s |
| H10 | The key ring is in the database (I-22) | `grep -c "data_protection_keys" api/src/JobTracker.Api/Migrations/*AddDataProtectionKeyRing.cs` → ≥1 · `grep -c "IConfigureOptions<KeyManagementOptions>" api/src/JobTracker.Api/Program.cs` → 1. **A container registration of `IXmlRepository` does nothing** — that was measured, and reverting to it would look green while reproducing the bug |
| H11 | AC-14's gate is live in CI | `grep -c "Bundle gate" .github/workflows/ci.yml` → 1 · `test -x tests/build/bundleGate.sh` · and to prove it can fail: `GATE_BYTES=1 bash tests/build/bundleGate.sh` → **exit 1** |
| H12 | Ran-vs-skipped is read, not guessed — owner/repo is literal on purpose: `$GITHUB_REPOSITORY` is unset in a dev shell (verified), so the parameterised form fails on first use | `gh api "repos/lowqualityloey/job-tracker/actions/runs/<id>/jobs" --jq '.jobs[] \| .name + " " + (.steps[] \| .conclusion + " " + .name)'` → look for `Verify (build with warnings as errors → test against a real PostgreSQL)` **success** vs `skipped`. A `pass` conclusion is **identical** either way |
| H13 | No orphan server or scratch database from the last session | `pgrep -x JobTracker.Api` → none (a leftover `dotnet run` child holds a scratch DB and makes `DROP DATABASE` fail; runners now `pkill -x JobTracker.Api` and drop `WITH (FORCE)`) · `docker exec api-db-1 psql -U jobtracker -d postgres -tAc "select datname from pg_database where datname like 'jobtracker_p%' or datname like 'jobtracker_e2e%'"` → none |
| H15 | **The tree may carry tooling changes that are nobody's business but the owner's** | `git status --porcelain` — as of 00:41 UTC this returns `M .promptkit` (submodule pointer `a1eb608 → eecd77b`), `M AGENTS.md` (the PromptKit OS rewrite) and `?? .github/ISSUE_TEMPLATE/`, `?? .github/pull_request_template.md`. **Do not commit these into a feature or docs PR** (`.promptkit/` is a read-only submodule; an unrelated sweep makes the host PR's message false about its contents). Ask, or land them as their own `chore(promptkit)` PR. `AGENTS.md`'s tenth rule is inside the rewritten file — re-derive the count before assuming it survived |
| H14 | Docker `api-db-1` (`127.0.0.1:5432`) is this repository's dev Postgres, and the browser harness runs in **`jt-bridge`** (host→CDP is dead; container→host works). Dev DB password: read it from `api/README.md`, never from memory | `docker ps --format '{{.Names}}' \| grep -E 'api-db-1\|jt-bridge'` |

## 3. Do not undo these

The ten §4 invariants inherited from M2a/M3 **plus**: `TimeProvider` is the only clock the auth path reads · CORS credentials-on, explicit origins, `"*"` refuses boot · every problem document carries a code listed in `contracts/problem-codes.json` · SSE fan-out scoped by `OwnerId` · no state-changing `GET` · `RepositoryError` gained exactly one variant and `-037`'s table maps row-for-row · `JobApplication` gained no auth field and the client never names its own owner · the session token never appears in a URL, log or localStorage · runtime deps stay 3 · **I-21 gate ordering** · **I-22 ring in Postgres** · **I-23 the CSRF cookie is readable by design**. Red/Green/record are three commits; a measurement row's "test" is its script plus its exit code, and its controls are the evidence.

## 4. What will trick you here

- **A green build proves nothing about whether a file is intact.** `Program.cs` was truncated 274 → 78 lines today — pipeline, gates and endpoints gone — **and it compiled**, because a web app that serves nothing is still valid C#. Diff hunk headers ("64 lines removed" is not a bullet edit) and structure-check against `main`.
- **A silent no-op looks exactly like the bug it fixes.** Registering `IXmlRepository` in DI, or reverting the key ring, produces no warning: `-074`'s test stayed red until the wiring went through `KeyManagementOptions`.
- **A filter can destroy the evidence or manufacture it.** `| tail -1` hid failures; `grep -E "Passed!"` matched echoed script text; a working-tree grep used as an absence proof is falsified by the sentence that quotes it. Name a revision for absence claims.
- **Do not write an identifier before its subject exists.** Three retractions today: a fabricated SHA in a published body, a PR number in a register row, a projection naming its own PR.
- **`;` is not `&&`.** A publish step chained after a failed commit shipped the fabricated SHA. Gate the chain.
- **Never put backticks in `git commit -m`, and never trust an unquoted heredoc** — the shell executes them. Commit messages and PR bodies here are written to files and passed with `-F` / `--body-file`.
- **`pkill -f` / `pgrep -f` can match your own command line**; use `-x` with the exact process name.
- **Numbers cited in prose rot at the next edit.** If a count is not derived by a command quoted next to it, treat it as suspect — including in this file.
- **Docker flakes**: one run died with `DockerContainerNotFoundException` on 11 tests; a re-run was 186/186. Separate flake from fault **by re-running**, not by reasoning.

## 5. The one next action

**`-075`** — the full form, its resume condition, its real cost (two Chromium runs to re-produce `-063`'s 11/11 and `-064`'s 7/7 against the extraction) and what outranks it (the owner decisions) are in [checkpoint-002 §8](TASK-m4-authentication.checkpoint-002.md). Start by validating H1–H14 above; do not begin editing on a mismatch.
