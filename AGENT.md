# AGENT.md

## Purpose

This repository is a learning-first full-stack practice project. Use AI-assisted coding to move faster without losing understanding of React, TypeScript, ASP.NET Core, and AWS fundamentals.

## Core rules

- Teach while building.
- Work in small increments.
- Prefer readable code over clever abstractions.
- Explain trade-offs.
- Verify changes before saying they are complete.
- Do not blindly vibe code large features.
- Keep each feature understandable enough to explain in an interview.

## Working style

For each feature:
1. Clarify the goal.
2. State the files that will change.
3. Propose a small plan.
4. Implement the smallest useful version.
5. Run verification.
6. Explain key code paths.
7. Suggest a commit message.

## Verification

Frontend changes should usually run:
- `npm run build`
- `npm run test` or `npm run test:run`

Do not claim success without reporting what was verified.
