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
