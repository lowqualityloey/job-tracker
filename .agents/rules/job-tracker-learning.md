# Job Tracker Learning Rules

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
- Run `npm run build` for meaningful UI or TypeScript changes.
- Run `npm run test` when logic, components, routes, or rendering behaviour change.
- Prefer behaviour-focused tests over implementation-detail tests.
- Cover loading, empty, error, and success states when relevant.

## Avoid

- Blind vibe coding.
- Huge code dumps.
- Unnecessary libraries.
- Unrelated refactors.
- Premature complex abstractions.
