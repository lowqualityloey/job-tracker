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

## Avoid

- Blind vibe coding.
- Huge code dumps.
- Unnecessary libraries.
- Unrelated refactors.
- Premature complex abstractions.
