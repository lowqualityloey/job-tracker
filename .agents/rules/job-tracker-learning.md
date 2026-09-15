# Job Tracker Learning Rules

> **Canonical source: `/AGENTS.md`.** This file is the Kilo Code mirror of that rule set — the full
> project profile lives in `PROMPTKIT.md`, design tokens in `DESIGN.md`, and live state in `docs/STATE.md`.
> If this file and `AGENTS.md` ever disagree, `AGENTS.md` wins and this file is a bug. Update `AGENTS.md`
> first, then re-sync here in the same commit.

## Mission

This is a learning-first full-stack project. Help the developer practise agentic coding while gaining real understanding of React, TypeScript, ASP.NET Core, databases, testing, and AWS.

Optimize for learning velocity, not just output velocity.

## Current stack

- React
- TypeScript
- Vite
- React Router
- Vitest
- React Testing Library

## How to work

For every task:

1. Restate the feature goal briefly.
2. Inspect the relevant files before changing anything.
3. Explain a small implementation plan.
4. Make the smallest useful change.
5. Run the relevant checks.
6. Explain the important code paths.
7. Suggest a focused commit message.
8. Suggest one follow-up practice task.

## Testing and verification

Before suggesting that a task is complete:
- Run the relevant checks.
- Report what passed and what failed.
- If a check cannot be run, say so clearly.
- Do not claim success without verification.

When changing frontend code:
- Typecheck with `npx tsc -p tsconfig.app.json --noEmit` (fast, and it emits no files).
- Run `npm run test:run` when logic, components, routes, or rendering behaviour change — it exits, so an
  agent can actually read the result.
- `npm run test` is exactly `vitest`, i.e. **watch mode**: it never exits, so use it only interactively while
  you develop, and never as the verification step of a task.
- **Never run bare `npx tsc -b` as a typecheck shortcut.** It emits `vite.config.js`, `vite.config.d.ts`,
  and `*.tsbuildinfo` into the repo root, and the emitted `vite.config.js` shadows the real
  `vite.config.ts` for `vite dev`/`vite build` (Vite resolves `.js` before `.ts`) while Vitest keeps using
  the `.ts` — so tests stay green against a config the app is not using. `npm run build` still runs `tsc -b`;
  delete those files after it and never `git add .`.
- Prefer behaviour-focused tests over implementation-detail tests.
- Cover loading, empty, error, and success states when relevant.

## Commit discipline

Mirrored from `AGENTS.md` (canonical) — every rule below came from a failure on this repo:

- Gate commits on verification; never chain `verify && commit` without `set -e`, because a
  failing typecheck does not stop the commit that follows it.
- Run `git status --porcelain` before committing. `git add <paths>` controls what you stage,
  not what you left uncommitted behind it.
- In scripted edits, assert the anchor string was found — a no-match `replace` reports success.
- Put store/global resets at **file** scope in tests; a `beforeEach` in one `describe` does not
  apply to its siblings, which makes the suite order-dependent.
- Keep Red, Green, and Refactor in separate commits so history still shows the test drove the code.
- `set -e` does not cover a **pipeline**: `verify | head` exits with `head`'s status, so a failing check
  inside the pipe never stops what follows. Use `set -o pipefail`, or `&&`.
- **Read the ref before writing to it.** Every bash call is a new shell, so a branch created in an earlier
  turn is not the branch you are on later. On 2026-09-11 a ladder committed to local `main` for exactly that
  reason while STATE.md named a branch that had never been created. Check `git rev-parse --abbrev-ref HEAD`
  before committing, and write a ref into a document only after measuring it. When a section is a *projection*
  of another record (STATE.md §3A), regenerate it whole at each boundary — bullet-wise patching across turns
  left it contradicting itself.
- **Bound a projection edit by the section, not by "the next bullet."** A helper that walked a `STATE.md` §3A bullet to
  the next line starting `- ` deleted 51 lines of §4's locked invariants — twice in one day, because §3A's last bullet is
  followed by a heading whose next bullet lives in §5. Read the diff's hunk headers: 64 lines removed is not a bullet
  edit. Then audit every removed line, because deliberate losses hide among the accidental ones.
- **Reproduce the gate's conditions before quoting its verdict.** CI's first .NET run failed on
  `error CS8605` in code I had called clean: my build was incremental and my grep pattern omitted
  `warning`, so the evidence was thrown away by the reading. Delete `bin/ obj/`, pass the same flags CI
  passes, read the whole output.
- **Never put backticks in `git commit -m`** — inside double quotes bash runs them as command substitutions and the
  message quietly loses the quoted text. Use `-F <file>` or single quotes. Earned 2026-09-11, when a commit recording a
  defect lost the exception names that were its evidence.
- **Re-measure a PR's state in the same breath as the action that depends on it.** #86 and #87 both merged between the
  `gh pr view → OPEN` read and the push that relied on it — 35 minutes apart, same cause. A PR-open claim lasts seconds,
  not a turn: read `state` + `headRefOid` right before the push/merge, assert `headRefOid` again right after, and never
  write a PR number into a doc before the PR exists (DEBT-24, promoted 2026-09-14).
- **Write perishable claims as predicates; never spend a pull request refreshing a value.** Rule above governs a claim an *action* depends
  on; this governs a claim *stored for a reader*. `STATE.md` §3A's "In flight" bullet and §1's branch field go false when their own PR
  merges — three regenerations in eight hours on 2026-09-14/15, and one merge landed 68 seconds after the sentence about it was measured
  `OPEN`. Owner's convention (2026-09-15): write the command that re-derives the fact (`gh pr list --state open`,
  `git rev-parse --abbrev-ref HEAD`) with a dated value beside it, and fix stale values inside the next content PR. (Mirrored from
  `AGENTS.md`, canonical — rule twelve.)

CI (`.github/workflows/ci.yml`) enforces the code half of this list via `npm run verify` on every PR —
typecheck, eslint, stylelint, tests, build, plus a guard that the build emitted no `vite.config.js`
shadow. The history half is still unenforced: no runner can notice a commit message describing two
behaviours whose diff carries one, or an `--amend` that staged nothing.

## Avoid

- Blind vibe coding.
- Huge code dumps.
- Unnecessary libraries (runtime dependencies have stayed at 3 since M0; the 10 tooling packages added by
  DEBT-03 are each justified where they are configured).
- Unrelated refactors.
- Premature complex abstractions.

## Branch & PR workflow
Agent owns: branch, `git push -u origin <branch>`, pushing commits, `gh pr create` (publish early;
a branch with no remote head is one disk failure from lost). Human owns: review and merge. Never call a branch
push "the human's call". Report the PR URL and stop. After pushing, verify `gh pr view <n> --json headRefOid`
equals `git rev-parse HEAD` — a commit pushed while GitHub is mid-processing can end up on the branch but out
of the merge (happened 2026-09-11 on PR #4: tip `28f3408` never reached `main`).
