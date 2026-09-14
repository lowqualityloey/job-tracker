

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

Eleven rules, each earned by a failure that actually happened on this repository. **The count is the part that goes stale
on its own** — a rule was added today, and for one commit the header still said nine while the list carried ten — so
re-derive it instead of remembering it: `awk '/^### Commit discipline/,/^CI \(/' AGENTS.md | grep -c '^- \*\*'` → **11**.

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
  documentation**: state a branch or SHA after reading it, never before. **And when a section is a projection of
  another record, regenerate it whole at the boundary** — five turns of editing only the bullets each turn was
  about left `docs/STATE.md` §3A asserting `Active Task Pointer: None` three lines below the bullet saying the
  pointer is held. Every individual edit was true; the accumulated block was not.
- **Reproduce the gate's conditions before quoting its verdict.** The first CI run of the `api` job failed with
  `error CS8605: Unboxing a possibly null value` on code I had just reported as clean. Two mistakes compounded:
  my local build was **incremental** over a cached dependency graph, and I filtered the output with
  `grep -E "Passed!|Failed!|error CS"` — **`warning` was not in the pattern**, so the one line that would have
  told me was discarded by the act of reading. "0 warnings" was not an observation; it was a claim about a tree
  I had not rebuilt. Same family as the `pipefail` rule: **filtering a command's output can destroy the only
  evidence that matters.** When a gate is strict and clean, run it strict and clean — delete `bin/ obj/`, pass
  the same flags, and read the whole output.
- **Bound a projection edit by the section, never by "the next bullet."** Regenerating `docs/STATE.md` §3A whole means
  rewriting bullets, and a bullet has no unique terminator. A helper that took a bullet from its first line to the next
  line starting `- ` deleted **51 lines of §4's locked technical invariants** — once, and then again about two hours later
  in the same file, the same way, because §3A's last bullet is followed by the `## 4` heading whose next `- ` item lives in
  §5. Both times the only thing that caught it was reading the diff's hunk headers: **"64 lines removed, 2 added" is not
  what editing a bullet looks like.** Both times the deletion was repaired from `HEAD`, and both times auditing the
  surviving diff turned up *deliberate* losses sitting next to the accidental ones, which is the other half of the rule:
  after a whole-section rewrite, account for every removed line. Verify structure, not only content —
  `grep -c '^## '` plus the numbered-item count, both compared against `HEAD`. The safe forms are an anchor on the
  section heading with the cut taken up to it, or a literal string replacement with an `assert` on the anchor.
- **Never put backticks in `git commit -m`.** Inside double quotes, bash **executes** them as command substitutions, so the
  message silently loses whatever was quoted — and the commit message is the artifact that is supposed to be the permanent
  record. On 2026-09-11 a commit documenting a new defect lost the exception names that *were* its evidence, leaving
  `server log reads  ->  Root cause:` with the substitution errors discarded to stderr. Use `git commit -F <file>`, or
  single quotes. **The damage is invisible in `git log` unless you look for it**: the message still reads as fluent prose
  with a hole in the middle, which is why it survived the commit that carried it. Same family as the `pipefail` and
  grep-pattern rules above — **a tool that swallows stderr turns a loud failure into a quiet one.**
- **Re-measure a PR's state in the same breath as the action that depends on it.** A PR-open claim has a shelf life of
  **seconds**, not of the turn in which it happened to be measured. `gh pr view 87` returned `OPEN` at 07:39; #87 merged at
  `07:39:56Z`, and the push at 07:48 landed five commits on a branch whose PR was already closed. That is the second
  occurrence in 35 minutes — #86 merged while a push was in flight and stranded a commit that never reached `main`. So:
  read `state` **and** `headRefOid` immediately before any push, merge, or claim that depends on them, assert
  `headRefOid` again *after* the push, and never write a PR number into a document before the PR exists — measure, then
  write, in that order. Recorded as DEBT-24, promoted on 2026-09-14 after #88 merged while its own body was still asking
  whether to promote this rule, which is the recursion this list keeps attracting.

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
## PromptKit OS: Engineering Operating System
PromptKit OS is active in this workspace (`./.promptkit`). Follow these protocols, workflows, and quality gates during pair-programming, design, code generation, and review:

### Fast Shorthand Triggers (Collision-Free)
Activate workflows anytime with these namespaced triggers:
- `pk:route`: Engineering lifecycle router and workflow decision matrix.
- `pk:tutor` (or `pk:tutor beginner`, `pk:tutor architect`): Socratic mentorship & 3-tier hints (no unsolicited code dumps).
- `pk:grill`: Intensive Staff Engineer architecture interview and defense drill.
- `pk:plan`: Spec-Driven Architecture & feature planning (domain models, API contracts).
- `pk:onboard`: Brownfield codebase intake: scan repository, extract scripts, scaffold PROMPTKIT.md.
- `pk:tasks` (or `pk:issue`, `pk:kanban`): Decompose RFC specs into atomic GitHub issues with Gherkin AC.
- `pk:review`: Senior multi-dimensional PR & architecture review (Security, Perf, A11y, Clean Code).
- `pk:commit`: Atomic Conventional Commits, single-concern staging, and pre-commit secret leak scan.
- `pk:pr`: High-signal PR descriptions, verification evidence compilation, and GitHub CLI creation.
- `pk:debug`: Hypothesis-driven scientific debugging & root cause analysis (5-Whys).
- `pk:fix`: Surgical remediation for known findings, security-first ordering.
- `pk:refactor`: Structural debt remediation, Golden Master pinning, Mikado method.
- `pk:perf`: Empirical performance profiling, latency SLAs, EXPLAIN ANALYZE.
- `pk:data` (or `pk:db`): Relational modeling, indexing strategies, RLS, and transaction boundaries.
- `pk:auth`: Authentication flows, cookie security, session management, and RBAC matrices.
- `pk:api`: Frontend-backend handshake, unified envelopes, and contract generation.
- `pk:test`: Upfront testing strategy, pyramid seam allocation, and mock boundaries.
- `pk:ship`: Release engineering, migration sequencing, and runtime env checks.
- `pk:spike` (or `pk:research`): Technical spikes, benchmarks, and multi-vector trade-off matrices.
- `pk:design`: Modern UI/UX, Design Tokens, and WCAG 2.2 Level AA accessibility.
- `pk:retro` (or `pk:reflect`): Retrospective log, ADR extraction, and skill matrix alignment.
- `pk:checkpoint` (or `pk:handoff`): Session state compaction, docs/STATE.md update, and handover prompt.
- `pk:sync` (or `pk:update`, `pk:refresh`): Hot-reload protocols, purge stale memory, and synchronize with disk.
- `pk:profile`: Switch Lite/Balanced/Turbo profile at runtime via the idempotent installer re-injection path.

### Smart Auto-Route & Guardrails (Triggers Are Optional)
You do not need to memorize triggers. If a prompt lacks an explicit `pk:` trigger, apply this triage:
- **Fast-Path (Zero Overhead)**: For simple questions, lookups, formatting, or single-line tweaks, answer directly. No heavy ceremony. **Risk-before-size**: 1-line security or data edits escalate immediately.
- **Anti-Slop Output**: Deliver all updates, plans, and diff explanations in structured, scannable markdown (tables, checklists, short bullets). Never output conversational essay walls.
- **Absolute Secret Hygiene**: Never output or request raw secrets/keys; mandate `.env.example` templates and local `.env`.
- **Session Endurance**: After ~12 substantive turns, or whenever you cannot recite the active invariants from `docs/STATE.md` verbatim, run `pk:checkpoint` and recommend a fresh session before continuing.
- **STATE.md Untrusted Until Read**: Quote milestone/task values only from the current turn's read of `docs/STATE.md`; template placeholder fields must be reported as `not tracked`, never as computed-looking facts.
- **Telemetry Card Provenance**: Every number in a status card must trace to a command executed or file read in this turn; otherwise emit `not measured`. Never claim a green Quality Gate without an executed check this turn.
- **Native MCP & Interactive Turn Prompts**: Auto-detect active MCP servers and prioritize structured tools over shell commands. For branching choices or next steps, invoke native selection tools (e.g. `ask_question`) if supported; otherwise format numbered choices under `> [!TIP] ### 💡 Next Steps (Type number & Enter):` with Option 1 prefixed `(Recommended)`. When the developer replies with a single number (`1`), immediately execute that option.
- **Dual-Compatible Telemetry Status Cards**: Display milestone progress, active task, and quality gate health using a monospace fenced block or blockquote card (e.g. ` ```text ` with `📊 Milestone: ...`, `🎯 Active: ...`, `🟢 Quality Gate: ...`, and optional `📈 PRs in flight: ...`).
  Halting for human decisions uses `> [!IMPORTANT]` titled `### 🛑 Action Required From You:`. Blocked states use `> [!WARNING]` titled `### ⚠️ Blocked: Waiting on Human Input:`. Milestone completion / next lifecycle recommendations (e.g. `pk:checkpoint`, `pk:pr`, `pk:tasks`) use `> [!TIP]` titled `### 💡 Next Recommended Step:`. Always prefix callouts with `> ` (never bare `[!TIP]`), zero raw HTML, perfect rendering across all terminal CLIs and IDEs.
- **Disk-First Protocol Loading & Hot-Reload (`pk:sync`)**: Never rely on conversational memory or past turn habits for workflows or quality gates. Always read `.promptkit/workflows/<trigger>.md` freshly from disk. When receiving `pk:sync` or after engine updates, immediately refresh context from disk.
- **Project Database & Harness Isolation**: Integration tests and live database verification must use dedicated project-scoped containers (e.g. `./docker-compose.yml` or project-named instances). Never attach to or run destructive queries against foreign project containers or credentials.
- **Strict Milestone Git Boundaries**: A milestone boundary is the turn after a `pk:plan`/`pk:tasks` milestone or Task Record closes. Never cross it carrying **this task's** uncommitted changes; pre-existing dirt (e.g., fresh `init.sh` scaffold output) is surfaced and recommended for `pk:commit`, never a stall reason. At milestone end: stage atomically (`pk:commit`), update `docs/STATE.md`, request human sign-off (`> [!IMPORTANT]`).
- **Protocol Auto-Route (Substantive Tasks)**: For multi-file changes or architecture, announce briefly (e.g. `[PromptKit OS: Auto-routed to pk:plan]`) and adopt the matching workflow:
  - Defects, bugs, crashes, test failures -> `pk:debug`
  - Known defects, review findings, security patches -> `pk:fix`
  - Code refactoring, structural cleanup -> `pk:refactor`
  - Performance regressions, latency -> `pk:perf`
  - New features, redesigns -> `pk:plan`
  - Repo intake, setup, audit -> `pk:onboard`
  - Task breakdowns, issue creation -> `pk:tasks`
  - DB schema, indexing, migrations -> `pk:data`
  - Auth, sessions, cookies, RBAC -> `pk:auth`
  - Endpoints, contracts, client types -> `pk:api`
  - Test suites, seam allocation, mocking -> `pk:test`
  - Code audits, PR reviews -> `pk:review`
  - Git commits, staging -> `pk:commit`
  - Pull requests, PR descriptions -> `pk:pr`
  - Context bloat, session handover -> `pk:checkpoint`
  - Deployments, env validation, releases -> `pk:ship`

### Workflows & Protocols Reference
Load lazily by convention — never preload:
- Workflow: `.promptkit/workflows/<trigger>.md` (e.g. `pk:plan` -> `workflows/plan.md`, `pk:design` -> `workflows/design-system.md`)
- Trigger-to-file exceptions (the convention alone would misresolve these): `pk:spike` -> `research.md`, `pk:retro` -> `reflect.md`, `pk:grill` -> `tutor.md`, `pk:design` -> `design-system.md`; all other triggers match their file name.
- Protocols: `.promptkit/protocols/{setup,context-sync,code-quality-gate,subagent-delegation}.md`
- Router: load `.promptkit/workflows/route.md` only when routing is ambiguous or Level 3 escalation/downgrade rules are needed
- Project files: `./PROMPTKIT.md`, `./DESIGN.md`, `./docs/STATE.md` (if present)

### Task Ceremony Levels (classify here — do not load route.md to decide)
Declare on line 1 of Turn 1: `[PromptKit OS: Level <0-3> (<Name>) — <1-line reason>]`
- **L0 Direct**: questions, lookups, doc typos, formatting, non-risky 1-line edits. `understand -> change -> verify`. No task record. Risk-before-size: 1-line security/data edits escalate.
- **L1 Standard**: localized bug fix, small self-contained feature, no schema/auth/breaking contract. Inline planning; no Task Record file.
- **L2 Controlled**: schema/migrations, auth, permissions, public contracts, multi-component. Requires `docs/tasks/<task-id>.md` + spec before implementation.
- **L3 Release-Critical**: release, tag, deploy, high-impact contract change. Requires L2 evidence + `pk:ship` + explicit human approval.
- **Escalate** immediately if scope grows into persistent data, auth, public contracts, or multiple components. **Ties take the higher level.** Downgrades must be announced with a one-line reason; silent downgrade is a protocol violation. `workflows/route.md` remains the canonical authority for these rules and for downgrade guardrails.

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
