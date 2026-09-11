

# AGENTS.md — Job Tracker

## Purpose

This repository is a learning-first full-stack practice project. Use AI-assisted coding to move
faster **without losing understanding** of React, TypeScript, ASP.NET Core, and AWS fundamentals.
The frontend exists today; the ASP.NET Core API, database, authentication, and AWS deployment are
planned stages (see `README.md`).

## Core rules

- **Teach while building.** Explain the *why*, not only the diff.
- Work in small increments. Prefer readable code over clever abstractions.
- Explain trade-offs whenever more than one option is genuinely viable.
- Verify changes before saying they are complete, and report exactly what was run.
- Do not blindly vibe-code large features.
- Keep every feature understandable enough to explain in an interview.

## Working style (per feature)

1. Clarify the goal and restate it briefly.
2. **Inspect the relevant files before changing anything.**
3. State the files that will change.
4. Propose a small plan.
5. Implement the smallest useful version.
6. Run verification.
7. Explain key code paths.
8. Suggest a commit message.
9. Suggest **one follow-up practice task** — this is a learning project; optimise for learning
   velocity, not just output velocity.

## Testing principles

- Prefer **behaviour-focused** tests over implementation-detail tests. Assert what a user can see or do,
  not internal state shape.
- Cover **loading, empty, error, and success** states whenever you touch data flow or rendering.
- Tests must terminate: run `npm run test:run`, never bare `npm test` (that is `vitest` in watch mode and
  never exits).

## Avoid

- Blind vibe coding, and huge code dumps.
- Unnecessary libraries — the app is 22 non-test TS/TSX files with **3 runtime dependencies**, a number
  that has not moved since M0; justify each addition. Tooling was added once (DEBT-03, 2026-09-11):
  **10 packages, `devDependencies` 9 → 19, runtime dependencies unchanged at 3** — `eslint` (pinned **9**,
  not 10: `eslint-plugin-jsx-a11y`'s peer range stops at 9, and the a11y gate is worth more than a major
  version), `@eslint/js`, `typescript-eslint`, `eslint-plugin-react-hooks`, `eslint-plugin-jsx-a11y`,
  `eslint-plugin-testing-library`, `@vitest/eslint-plugin`, `globals`, `stylelint`,
  `stylelint-config-standard`. Each is named and justified where it is configured, in `eslint.config.js` /
  `stylelint.config.js`. No formatter was added: it would rewrite all 35 TS/TSX files in a commit
  unrelated to any feature.
- Unrelated refactors bundled with a feature or fix.
- Premature complex abstractions — the 5-record mock dataset does not need a framework.

## Verification commands (verified on this machine)

| Check | Command |
| :--- | :--- |
| Install | `npm install` |
| Dev server | `npm run dev` |
| Typecheck (fast inner loop — emits nothing) | `npx tsc -p tsconfig.app.json --noEmit` |
| Typecheck + build | `npm run build` (`tsc -b && vite build`) |
| Tests (watch) | `npm test` |
| Tests (once) | `npm run test:run` |
| Preview build | `npm run preview` |
| Lint (JS/TS, fails on warnings) | `npm run lint` |
| Lint (CSS) | `npm run lint:css` |
| **The full gate, in order** | `npm run verify` (typecheck → lint → lint:css → test:run → build) |

⚠️ **Do not use bare `npx tsc -b` as a typecheck shortcut, and do not remove `"noEmit": true` from
`tsconfig.node.json`.** Without it, `tsc -b` (which `npm run build` runs) emits `vite.config.js`,
`vite.config.d.ts`, and `*.tsbuildinfo` into the repo root — and the emitted `vite.config.js` **shadows your
real `vite.config.ts`** for `vite dev`/`vite build`, while Vitest keeps reading the `.ts`. Verified by probe
and closed in `bd8a8b6`; `vite.config.js` is deliberately not gitignored, so if it ever reappears it should be
loud in `git status`. Details: DEBT-11 in `docs/STATE.md`.

`npm run typecheck` **does** exist now (`tsc -p tsconfig.app.json --noEmit`); so do `lint`, `lint:css`, and
`verify`. CI runs `npm run verify` on every pull request (`.github/workflows/ci.yml`, DEBT-03 closed
2026-09-11). There is still **no formatter** and **no pre-commit hook** — nothing stops an unverified local
commit, only an unverified pushed one. Package manager is **npm** (`package-lock.json`); do not mix in
pnpm/yarn lockfiles.

Do not claim success without reporting what was verified.

### Commit discipline

Eight rules, each earned by a failure that actually happened on this repository:

- **Gate commits on verification.** Never chain `verify && commit` on one shell line without
  `set -e` — a failing `tsc` does not stop the commit that follows it. A commit was once made
  over 17 typecheck errors while its own message claimed passing test counts.
- **Check `git status --porcelain` before committing**, not only `git add <paths>`. Staging
  specific paths guarantees what you stage; it says nothing about what you left uncommitted.
  An unwritten-off `remove()` once rode into an unrelated commit and made two messages false
  about their contents.
- **Assert the anchor in scripted edits.** A `str.replace()` that matches nothing reports
  success and silently half-applies a change; it once inserted a call to a helper that was
  never written.
- **Reset shared globals at file scope in tests.** A `beforeEach` inside one `describe` does
  not apply to its sibling block, so any test asserting on `localStorage` key enumeration or
  module state becomes order-dependent.
- **Red, Green, and Refactor are separate commits.** Committed together, history no longer
  proves the failing test drove the implementation.
- **`set -e` does not cover a pipeline.** `verify | head` reports the exit status of `head`, so a failing
  check inside the pipe sails on and commits anyway. Use `set -o pipefail` (or `&&`) whenever a scripted
  probe's result matters. Hit twice on 2026-09-11: once masking a bad `git status --cached` flag, once
  masking a stylelint run whose exit code was the only assertion being made.
- **Read the ref before you write to it.** Each bash call starts a fresh shell, so a branch created in an
  earlier turn is **not** the branch you are on in the next one. On 2026-09-11 a commit ladder ran on `main`
  because the branch it assumed had been created in a turn that had already ended — two commits landed on
  local `main`, and `docs/STATE.md` described a branch that had never existed, because the document was
  written from *intent* rather than from measurement. Nothing reached the remote only because
  `git push origin <branch>` failed loudly on a nonexistent refspec. `git rev-parse --abbrev-ref HEAD` costs
  one call; the assumption costs the whole "phases advance through pull requests" rule. **Same reflex for
  documentation**: state a branch or SHA after reading it, never before.
- **Reproduce the gate's conditions before quoting its verdict.** The first CI run of the `api` job failed with
  `error CS8605: Unboxing a possibly null value` on code I had just reported as clean. Two mistakes compounded:
  my local build was **incremental** over a cached dependency graph, and I filtered the output with
  `grep -E "Passed!|Failed!|error CS"` — **`warning` was not in the pattern**, so the one line that would have
  told me was discarded by the act of reading. "0 warnings" was not an observation; it was a claim about a tree
  I had not rebuilt. Same family as the `pipefail` rule: **filtering a command's output can destroy the only
  evidence that matters.** When a gate is strict and clean, run it strict and clean — delete `bin/ obj/`, pass
  the same flags, and read the whole output.

CI (`.github/workflows/ci.yml`) now enforces the **code** half of this list — a commit made over typecheck
errors can no longer reach `main` unflagged, and neither can a focused test or an orphaned CSS declaration.
It cannot enforce the **history** half: nothing on a runner can tell that a commit message describes two
behaviours while its diff carries one, or that an `--amend` silently committed nothing. Those stay
agent discipline, which is why each rule above names the failure that produced it.

## Branch & PR workflow (human-gated phase advance)

Phases advance **through pull requests, never by pushing to `main`.**

1. Work on a branch named `<type>/<milestone-or-task-slug>` (e.g. `chore/promptkit-engineering-os`).
2. Commit atomically (Conventional Commits, one concern each) — `pk:commit`.
3. When the task or milestone is ready, run `pk:pr`: full verification evidence, then
   `git push -u origin <branch>` and `gh pr create` with a detailed body (summary by layer, verification
   evidence, rollback plan, reviewer focus, data-safety checklist).

   **Then confirm the PR's head is your commit:** `gh pr view <n> --json headRefOid` must equal
   `git rev-parse HEAD`. Pushing again while GitHub is still processing a previous head can leave a commit
   on the branch and out of the merge — measured on 2026-09-11, where PR #4 merged at `415f819` and the
   branch's own tip `28f3408` (a P1 debt record) never reached `main`. It surfaced as a stale `head.sha` in
   the API, which is the only reason it was noticed. Same family as the `git status` rule: **the branch
   having a commit is not the same as the PR carrying it.**

**Ownership of each step is fixed, and it is not 50/50.** Creating the branch, publishing it
(`git push -u origin <branch>`), pushing commits, and opening the PR are **agent** actions — the human should
never have to ask for them, and a milestone is not "reported complete" until the PR URL exists. Reviewing and
merging are **human** actions. Publishing early is also encouraged independently of the PR: a branch with no
remote head is one disk failure away from being lost. CI runs `npm run verify` on the PR, so a pushed
branch is a checked branch; an unpushed one is checked only as far as the agent's own last command.
Do not describe a branch push as "the human's call" — that phrasing appeared in several 2026-09-10 records and
was corrected the same day.
4. **The human reviews and merges.** Report the PR URL and stop — do not start the next phase.
5. When the human says it is merged, reconcile (`git checkout main && git pull --ff-only`) and only then
   begin the next phase / milestone / task.

Tagging, releasing, deploying, and any `git push` to `main` stay human-only decisions.

## Where project knowledge lives

- **This file (`AGENTS.md`) is the canonical agent rule set.**
- Stack profile & non-negotiable rules: `PROMPTKIT.md`
- Visual identity & measured design tokens: `DESIGN.md`
- Living state, milestones, debt register: `docs/STATE.md`
- Specs `docs/specs/` · task records `docs/tasks/` · ADRs `docs/adrs/` · RCAs `docs/rca/`
- `.agents/rules/job-tracker-learning.md` is a **second, git-tracked copy** for the Kilo Code IDE. Its unique
  rules are mirrored above and its verification commands were corrected in `f128376`. **Edit this file first,
  then re-sync that one in the same commit** — a third copy must never appear (DEBT-12 in `docs/STATE.md`).

<!-- PROMPTKIT_START -->
## Better-PromptKit Engineering Operating System
Better-PromptKit is active in this workspace (`./.promptkit`). Follow these protocols, workflows, and quality gates during pair-programming, design, code generation, and review:

### Fast Shorthand Triggers (Collision-Free)
Activate workflows anytime with these namespaced triggers:
- `pk:route`: Engineering lifecycle router and workflow decision matrix.
- `pk:tutor` (or `pk:tutor beginner`, `pk:tutor architect`): Socratic mentorship & 3-tier progressive hints (never dump unsolicited code).
- `pk:grill`: Intensive Staff Engineer architecture interview and defense drill.
- `pk:plan`: Spec-Driven Architecture & feature planning (domain models, API contracts, failure modes).
- `pk:onboard`: Brownfield codebase intake: scan repository, extract scripts, and auto-populate PROMPTKIT.md.
- `pk:tasks` (or `pk:issue`, `pk:kanban`): Decompose RFC specs into atomic GitHub issues with Gherkin Acceptance Criteria and Kanban sync.
- `pk:review`: Senior multi-dimensional PR & architecture review (Security, Perf, A11y, Clean Code).
- `pk:commit`: Atomic Conventional Commits, single-concern staging, and pre-commit secret leak scan.
- `pk:pr`: High-signal PR descriptions, verification evidence compilation, data safety checklist, and GitHub CLI creation.
- `pk:debug`: Hypothesis-driven scientific debugging & root cause analysis (5-Whys).
- `pk:fix`: Surgical remediation for known findings, security-first ordering, and single-concern scope.
- `pk:perf` (or `pk:profile`): Empirical performance profiling, latency SLAs, EXPLAIN ANALYZE, and delta verification.
- `pk:data` (or `pk:db`): Relational database modeling, indexing strategies, RLS, and transaction boundaries.
- `pk:auth`: Authentication flows, cookie security, session management, and RBAC/ABAC matrices.
- `pk:api`: Frontend-backend handshake, unified error envelopes, and contract generation.
- `pk:test`: Upfront testing strategy, seam allocation, and mock boundaries.
- `pk:ship`: Release engineering, migration sequencing, runtime env checks, and rollbacks.
- `pk:spike` (or `pk:research`): Technical spikes, benchmarks, and multi-vector trade-off matrices.
- `pk:design`: Modern UI/UX, Design Tokens, and WCAG 2.2 Level AA accessibility.
- `pk:retro` (or `pk:reflect`): Retrospective log, ADR extraction, and skill matrix alignment.
- `pk:checkpoint` (or `pk:handoff`): Session state compaction, invariant locking, docs/STATE.md update, and fresh chat handover prompt.

### Smart Auto-Route & Guardrails (Triggers Are Optional)
You do not need to memorize triggers. If a prompt lacks an explicit `pk:` trigger, apply this triage:
- **Fast-Path (Zero Overhead)**: For simple questions, syntax lookups, quick explanations, formatting, or single-line tweaks, answer directly and concisely. Do NOT invoke heavy workflow ceremonies or produce unnecessary documents.
- **Protocol Auto-Route (Substantive Tasks)**: For multi-file changes, architecture, broken code, or production ops, automatically adopt the matching workflow:
  - Defects, bugs, crashes, or test failures (unknown cause) -> `pk:debug` (reproduce before patching)
  - Known defects, review findings, or security patches -> `pk:fix` (remediate known root cause)
  - Performance regressions, slow queries, or latency -> `pk:perf` (measure baseline first)
  - New features, redesigns, or multi-component additions -> `pk:plan` (spec and risk analysis first)
  - Existing repo intake, setup, or codebase audit -> `pk:onboard` (scan repo and scaffold PROMPTKIT.md)
  - Task breakdowns, issue creation, or Kanban cards -> `pk:tasks` (atomic issues and Gherkin AC)
  - Database schema, indexing, or migrations -> `pk:data` (Expand-Contract ordering)
  - Auth, sessions, cookies, or RBAC -> `pk:auth` (threat model and capability matrix)
  - Endpoints, contracts, or client types -> `pk:api` (envelope and schemas)
  - Test suites, seam allocation, or mocking -> `pk:test` (pyramid seam allocation)
  - Code audits or PR reviews -> `pk:review` (two-axis standard review)
  - Git commits or staging -> `pk:commit` (atomic conventional commits)
  - Pull requests or PR descriptions -> `pk:pr` (verification evidence and PR body)
  - Context bloat, chat lag, session handover, or pausing -> `pk:checkpoint` (sync docs/STATE.md & zero-loss handover)
  - Deployments, env validation, or releases -> `pk:ship` (pre-flight checks and rollback)
  When auto-routing a substantive task, announce it briefly in one sentence (e.g., "[Better-PromptKit: Auto-routed to pk:plan]") and enforce its quality gate.

### Workflows & Protocols Reference
- **Route**: .promptkit/workflows/route.md
- **Tutor**: .promptkit/workflows/tutor.md
- **Plan**: .promptkit/workflows/plan.md
- **Onboard**: .promptkit/workflows/onboard.md
- **Tasks**: .promptkit/workflows/tasks.md
- **Review**: .promptkit/workflows/review.md
- **Commit**: .promptkit/workflows/commit.md
- **Pull Request**: .promptkit/workflows/pr.md
- **Debug**: .promptkit/workflows/debug.md
- **Fix**: .promptkit/workflows/fix.md
- **Performance**: .promptkit/workflows/perf.md
- **Data**: .promptkit/workflows/data.md
- **Auth**: .promptkit/workflows/auth.md
- **API**: .promptkit/workflows/api.md
- **Test**: .promptkit/workflows/test.md
- **Ship**: .promptkit/workflows/ship.md
- **Research**: .promptkit/workflows/research.md
- **Design System**: .promptkit/workflows/design-system.md
- **Reflect**: .promptkit/workflows/reflect.md
- **Checkpoint**: .promptkit/workflows/checkpoint.md
- **Quality Gate (DoD)**: .promptkit/protocols/code-quality-gate.md
- **Context Sync**: .promptkit/protocols/context-sync.md
- **Subagent Delegation**: .promptkit/protocols/subagent-delegation.md
- **Project Profile & Rules**: ./PROMPTKIT.md (if present)
- **Visual Identity & Brand**: ./DESIGN.md (if present)
- **Living State & Tracker**: ./docs/STATE.md (if present)

### Project Artifact Output Paths
All generated project documentation must be saved to the host project:
- State Tracker: docs/STATE.md
- ADRs: docs/adrs/
- Technical Specs: docs/specs/
- Task Breakdowns: docs/tasks/
- Post-Mortems: docs/rca/
- Spikes: docs/spikes/
- Design Specs: docs/design/
- Data Models: docs/data/
- Auth Specs: docs/auth/
- API Contracts: docs/api/
- Test Plans: docs/tests/
- Review Reports: docs/reviews/
- Performance Audits: docs/perf/
- Releases: docs/releases/
- CI Triage Evidence: docs/releases/ci-triage/
<!-- PROMPTKIT_END -->
