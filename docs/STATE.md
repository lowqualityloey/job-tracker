# Project State & Living Execution Tracker

> Rewritten by `pk:onboard` on **2026-09-10**. The previous content of this file was the unfilled
> PromptKit template and described a system that does not exist here (relational schema migrations
> "complete", blocked external API, RLS invariants, UUIDv7 ADRs). Nothing in this file is a placeholder.

## 1. Executive Summary & Current Position

- **Project Name**: Job Tracker (`job-tracker` v0.2.0)
- **Current Milestone / Epic**: M0 **done** · M1 **MERGED** (PR #1 → `63d769d`) · **M2 Pipeline Interactivity — planning now (Level 2)**
- **Overall Status**: ACTIVE <!-- ACTIVE | PAUSED | STABILIZING | RELEASE_CANDIDATE -->
- **Target Release / Deadline**: none. No version tag, no remote release, no deadline. `v0.2.0` in `package.json` is nominal only.
- **Current Working Branch**: `main` @ `63d769d` (== `origin/main`, clean `--ff-only` pull after merge). M2 will branch `feat/m2-*` from here. **Phase advances via PR only** — see `AGENTS.md` §Branch & PR workflow
- **Last Updated**: 2026-09-10 15:55 UTC — `pk:pr` opened M1 for review; `pk:onboard` pass 2 (re-entrant scan, +3 debt items, 9 lost rules recovered) then `pk:fix` shipped **DEBT-11 · 12 · 13** as commits `bd8a8b6` + `f128376`
- **Intake passes**: pass 1 scanned manifests/config/src and wrote the profiles; **pass 2 swept the directories pass 1 never opened** (`.agents/`, `.kilo/`, `.fallow/`, `.git/info/exclude`) and audited this file's own claims. Two P1 findings came out of it: DEBT-12, DEBT-13.
- **Baseline at intake**: `npx tsc -p tsconfig.app.json --noEmit` → **exit 0** · `npm run test:run` → **2/2 passed (1.10s, 1 file)** · no known defect, no broken state, no active blocker
- **Shape of the app**: single-package React 18 SPA, 12 TS/TSX files in `src/`, 3 routes, **in-memory mock data only** — no persistence, no backend, no auth.

---

## 2. Milestone & Task Progress

### Milestone Roadmap

- [x] **M0 — Frontend Foundation**: routing, layout shell, typed domain model, mock data, status badges, empty states, dark UI, Vitest + Testing Library wiring, WCAG AA contrast baseline
- [x] **M1 — Engineering OS Integration**: **MERGED as PR #1 (`63d769d`, 2026-09-10)**. Submodule vendored, rule sets consolidated, intake profiles + this tracker written, DEBT-11/12/13 fixed, lockfile tracked
- [/] **M2 — Pipeline Interactivity**: **M2a in progress** — spec + task record landed, domain validation and the localStorage repository done (4/11 ACs, 7/17 behaviours, 24 tests). Remaining: M2a.3 failure modes, M2a.4 CRUD UI, M2a.5 contract sweep, then PR #2. Filters = M2b
- [ ] **M3 — Backend**: ASP.NET Core Web API, database, replace mock with HTTP (**Level 2**: `pk:api` + `pk:data`)
- [ ] **M4 — Authentication** (**Level 2**: `pk:auth`)
- [ ] **M5 — AWS Deployment** (**Level 3**: `pk:ship` + human approval)

Legend: `[x]` Done · `[/]` In Progress · `[ ]` Queued · `[!]` Blocked

### Active Task Breakdown

- [x] `TASK-2026-09-10-onboard`: Brownfield intake — scan manifests, extract commands, populate `PROMPTKIT.md`, generate `DESIGN.md`, reset `STATE.md`
- [x] `TASK-2026-09-10-onboard-pass2`: Re-entrant intake — sweep the directories pass 1 never opened (`.agents/`, `.kilo/`, `.fallow/`, `.git/info/exclude`), audit this file's own numeric claims, and log DEBT-12/13/14
- [x] `TASK-2026-09-10-recover-rules`: Return the 9 doctrine rules that existed only in `.agents/rules/` back into `AGENTS.md` (working style → 9 steps, new *Testing principles* + *Avoid* sections)
- [x] `TASK-2026-09-10-dedup-agents`: Merge `AGENT.md` learning-first rules into `AGENTS.md`, delete the stale `AGENT.md` (its PromptKit block pointed at the removed non-submodule `promptkit/` path)
- [x] `TASK-2026-09-10-repo-hygiene`: Fix DEBT-11 + DEBT-13 in one commit (`noEmit` in `tsconfig.node.json`; `.kilo/`, `.fallow/`, `*.tsbuildinfo` in shared `.gitignore`) — **done in `bd8a8b6`; `git add .` is now safe for build artifacts**
- [x] `TASK-2026-09-10-rule-source`: DEBT-12 resolved: kept as a corrected Kilo mirror, commands fixed in `f128376`
- [x] `TASK-2026-09-10-bootstrap-commit`: Landed as 4 atomic commits on PR #1, merged in `63d769d` (previously 8 pending paths, incl. `package-lock.json`)
- [/] `TASK-M2-persistence`: Spec the persistence seam (`pk:plan` → `pk:data` → `pk:tasks`) — **active now**

---

## 3. Active Working Set

- **Target Workspace / Package**: N/A — standalone repository, no workspaces
- **Active RFC / Spec**: none yet (M2 will create `docs/specs/`)
- **Active Task Spec**: none — no Level 2 Controlled Work is open
- **Key Source Files in Flight**: none. Working tree changes are documentation + tooling only; **zero `src/` files were modified by `pk:onboard`**
- **Verification Commands (correct for this repo)**:
  - Typecheck: `npx tsc -p tsconfig.app.json --noEmit` *(verified: exit 0, emits nothing. There is **no** `typecheck` script — the previous file advertised one that does not exist. Do **not** use bare `npx tsc -b`: see DEBT-11)*
  - Tests: `npm run test:run` · Build: `npm run build` · Dev: `npm run dev`
  - Lint: **none configured** · CI: **none configured** · E2E: **not installed**

### 3A. Execution-Control Projection

> Synchronised projection. Canonical authority is `docs/tasks/TASK-m2-persistence-seam.md`.

- **Local Task Source**: `docs/tasks/TASK-m2-persistence-seam.md`
- **Task ID**: `TASK-m2-persistence-seam`
- **Task Record**: `docs/tasks/TASK-m2-persistence-seam.md`
- **Specification**: `docs/specs/2026-09-10-spec-m2-persistence-seam.md` (`PLAN-m2-persistence-seam`, Full)
- **Execution Scope**: repo `job-tracker`, branch `feat/m2-persistence-seam`, paths `src/**` + `docs/**`
- **Execution State**: `in_progress`
- **Mapped `pk:tasks` Status**: `In Progress`
- **Active Task Pointer**: `TASK-m2-persistence-seam`
- **Owner / Current Actor**: Lead Engineer (accountable) · Assistant (executing)
- **Start Time**: 2026-09-10 21:14 UTC
- **Current Branch**: `feat/m2-persistence-seam`
- **Current Revision**: `63d769d` at branch creation (PR #1 merge commit)
- **Checkpoint Policy**: soft ~60 min / hard ≤90 min **estimates only — no wall-clock timer is available to
  the agent**; enforceable triggers are event-driven (behaviour complete, scope change, handoff, human request)
- **Blockers and Resume Condition**: None. Resume condition if interrupted: read §5 of the Task Record and
  continue at the next unfinished `BEHAVIOR-` id
- **Verification Status**: at `c67b04f` — typecheck exit 0 across the `id: string` swap;
  `npm run test:run` **24/24** in 3 files (was 2/2 at branch start); `npm run build` exit 0 at M2a.1 with no
  `vite.config.js` emitted. Task-owned evidence lives in Task Record §6
- **CI Evidence**: `N/A` — no CI exists in this repository (DEBT-03)
- **Changed-File Summary**: M2a.1 domain validation. M2a.2 the repository seam (`domain/applicationRepository`,
  `data/localStorageApplicationRepository`), the seed module replacing `data/mockApplications`, and the
  **`id: number → string`** contract swap through three pages. No new UI surface yet — nothing is creatable
  from the app itself; only tests drive the repository
- **Latest Checkpoint**: 2026-09-10 21:22 UTC — M2a.1 validation complete (AC-2 met, AC-1 partial) · **Latest Handoff**: None
- **Next Action**: Red for `BEHAVIOR-m2-persistence-seam-008` — storage unavailable → in-memory fallback (M2a.3)

### 3B. Release-Evaluation Handoff

**N/A.** No release evaluation, candidate, or tag work exists in this repository. (The PromptKit
release-evidence overlay applies to Better-PromptKit itself, not to this consumer repo.)

---

## 4. Locked Technical Invariants (Do Not Undo)

Agreed decisions that survive any refactor. Deviating requires a new ADR.

1. **npm only.** `package-lock.json` is the sole lockfile; never introduce pnpm/yarn/bun manifests.
2. **Standalone repo.** No workspaces, no Turborepo/Nx, no `--filter` commands. `.promptkit/` is a read-only submodule.
3. **TypeScript `strict: true` stays on**, `isolatedModules` stays on. No `any`, no `@ts-ignore`; the single
   permitted non-null assertion is `document.getElementById('root')!` in `src/main.tsx`.
4. **`ApplicationStatus` is a public contract**, and is string-coupled to `.badge-*` CSS class names via
   `StatusBadge.tsx`. Changing it is Level 2 work touching 4 sites (union, CSS, `DESIGN.md` §2, dashboard filters).
5. **`src/data/` is the only module that knows where records come from.** Pages never touch storage directly.
   Derivations live in a future `src/domain/` module, not in components.
6. **Import direction**: `pages → components | data | domain → types`; `types/` imports nothing.
7. **A11y floor**: ≥44×44px hit targets, visible `:focus-visible` ring (3px `#93c5fd`, offset 3px), AA text
   contrast ≥4.5:1 (11/11 pairs pass today), status never conveyed by colour alone.
8. **Definition of Done**: `npx tsc -p tsconfig.app.json --noEmit` exit 0 **and** `npm run test:run` green
   **and** the change is interview-explainable (`AGENTS.md`). Report the commands you actually ran.
9. **No stale compiled config in the tree.** `vite.config.js`, `vite.config.d.ts`, and `*.tsbuildinfo` must
   never be committed — until DEBT-11 is fixed, `npm run build` creates them; delete them before staging.

---

## 5. Known Blockers, Risks & Open Questions

- **Blockers**: **None.**
- **M1 shipped.** All 6 commits are on `origin/main` via PR #1 (`63d769d`); local `main` fast-forwarded
  cleanly, so the stale `AGENT.md` and its hanging `npm run test` no longer exist for anyone cloning the repo.
- **Submodule note for the reviewer**: `.promptkit` is a gitlink (mode `160000`) at `a1eb608`, with
  `.gitmodules` committed in the same commit (DEBT-14). GitHub's file view will show it as a link, not a
  folder; a clone needs `git submodule update --init --recursive` before `pk:*` workflow paths resolve.
- **Architectural open questions** (answer in `pk:plan` / `pk:spike`, not by coding):
  1. Where does durable state live first — `localStorage`, IndexedDB (`idb`), or wait for the ASP.NET Core API?
     This decides M2's shape and whether M3 is a drop-in or a rewrite.
  2. Does `id: number` survive contact with a backend, or become a string UUID now while data is fake and free to change?
  3. Is `appliedAt?: string` re-typed as an ISO-8601 branded string or a real `Date` at the domain boundary?
  4. Is the status set a fixed union or a data-driven list the UI derives from?
- **Technical debt register** (severity P1 = before M2, P2 = during M2, P3 = polish):

| ID | Sev | Finding | Evidence | Suggested workflow |
| :--- | :--- | :--- | :--- | :--- |
| DEBT-01 | P1 | No persistence: every reload discards all edits; nothing can be created or edited at all | `src/data/mockApplications.ts` is the only source; no write path exists | `pk:plan` (M2) |
| DEBT-02 | P1 | Only 1 test file / 2 tests, and it covers a pure presentational leaf. Zero page, route, or derivation coverage | `src/components/StatusBadge.test.tsx` is the sole test | `pk:test` |
| DEBT-03 | P1 | No lint/format tooling and no CI — the DoD is unenforceable on another machine or human | no ESLint/Prettier/Biome config, no `.github/workflows/` | `pk:fix` (bootstrap config) |
| DEBT-04 | P1 | `package-lock.json` untracked → non-reproducible installs | `git status` | `pk:commit` |
| DEBT-05 | P2 | Business logic in UI: three `filter()` passes at module scope in `DashboardPage`; will re-run per import, not per data change | `src/pages/DashboardPage.tsx:3-5` | `pk:fix` → `src/domain/pipeline.ts` |
| DEBT-06 | P2 | Three pages import `mockApplications` directly — no repository seam to swap for HTTP later | `DashboardPage`, `ApplicationsPage`, `ApplicationDetailsPage` | `pk:plan` (M2) |
| DEBT-07 | P2 | Route param coerced with `Number(id)` and compared unvalidated; `/applications/abc` yields `NaN` → silent "not found" rather than a handled error | `src/pages/ApplicationDetailsPage.tsx:6` | `pk:fix` |
| DEBT-08 | P2 | No design-token layer: colours/radii/gaps are ~20 hardcoded literals; `Interview`/`Offer`/active-nav contrast have <0.4 ratio of AA headroom | `src/styles.css`, `DESIGN.md` §2, §6 | `pk:design` |
| DEBT-09 | P3 | `100vh` instead of `dvh`; no `min-width: 0` / truncation on children at 320px; no `:active` state, no motion or reduced-motion block | `src/styles.css:14-19`, `DESIGN.md` §5, §7 | `pk:design` |
| DEBT-10 | P3 | Catch-all `<Route path="*">` silently redirects to `/`, so typo'd and deep links are indistinguishable from home; no skip link; `<title>` never changes per route | `src/App.tsx:14`; `index.html:6` | `pk:fix` |
| DEBT-11 | **P1** | ✅ **FIXED in `bd8a8b6`.** **`tsc -b` emits a `vite.config.js` that shadows the real `vite.config.ts`.** Verified by probe on 2026-09-10: with a throwing `vite.config.js` present, `npx vite build` failed with *my* error (`error during build: Error: PROBE: vite.config.js WAS LOADED`) while `npx vitest run` stayed green — Vite resolves `.js` **before** `.ts` (`DEFAULT_CONFIG_FILES`, `node_modules/vite/dist/node/constants.js:33`), Vitest prefers `.ts`. So any future edit to `vite.config.ts` is silently ignored by dev/build but obeyed by tests, and if the stale file is ever committed every clone and CI run inherits it. `npm run build` produces this file on every run, and `vite.config.d.ts` + `*.tsbuildinfo` land beside it — none gitignored. **Fix (both halves verified this session):** add `"noEmit": true` to `tsconfig.node.json` (accepted alongside `composite: true` by TS 5.5.4, `tsc -b` exits 0, emission stops) and add `*.tsbuildinfo` to `.gitignore` (still written by composite builds) | `tsconfig.node.json` (no `noEmit`), `package.json:9` build script, `node_modules/vite/dist/node/constants.js:33` | `pk:fix` (2-line Level 1 chore, do before M2) |
| DEBT-12 | **P1** | ✅ **FIXED in `f128376`** (kept as a corrected mirror, per decision). **A second, git-tracked copy of the agent doctrine was missed at pass 1.** `.agents/rules/job-tracker-learning.md` (committed in `d78f663`, 51 lines) is the Kilo Code rules file. It held **9 rules that existed nowhere else** — inspect-before-changing, restate-the-goal, one follow-up practice task, behaviour-focused tests, loading/empty/error/success coverage, no unnecessary libraries, no unrelated refactors, no premature abstraction, no huge code dumps (verified: each phrase present in `.agents/`, absent from `AGENTS.md` at pass 1). Those 9 are now mirrored into `AGENTS.md`. **Remaining conflict:** line 41 tells agents to run `npm run test`, which is `"vitest"` → **watch mode, never exits** — any obeying agent or scripted step hangs. Line 40 also routes all verification through `npm run build`, i.e. through the DEBT-11 polluting command | `.agents/rules/job-tracker-learning.md:40-41`; `package.json:8` (`"test": "vitest"`) | Decide + `pk:fix`: either delete it and let Kilo read `AGENTS.md`, or keep it as the IDE mirror and change line 40-41 to `npx tsc -p tsconfig.app.json --noEmit` + `npm run test:run` |
| DEBT-13 | P2 | ✅ **FIXED in `bd8a8b6`** (shared `.gitignore` now names them; the 12 local-only `.git/info/exclude` lines are harmless leftovers). **63 MB of tool state is invisible to git.** `.kilo/` (`@kilocode/plugin` 7.4.23 + its own `node_modules`) hides behind `.kilo/.gitignore`, and `.fallow/` behind `.fallow/.gitignore` (`*`). Nothing in the **shared** `.gitignore` names either directory, so the protection lives inside the tool, not in the repo. Tree-walking tools (`find`, glob, greps, subagents) see them: pass 2's debt-marker scan returned 8 `TODO:` hits from `.kilo/node_modules/**` — pure noise. Also `.git/info/exclude` carries 12 Kilo worktree entries, which are **local-only and vanish in every other clone** | `du -sh .kilo` = 63M; `.kilo/.gitignore`; `.fallow/.gitignore`; `.git/info/exclude:9-45` | `pk:fix` — add `.kilo/`, `.fallow/`, `*.tsbuildinfo` to shared `.gitignore`; never `git add .` |
| DEBT-14 | P2 | ✅ **CLOSED by merge.** `.promptkit` sits at `a1eb608` (`v1.1.1-11-ga1eb608`, clean, detached HEAD as expected) `23ceefd` landed `.gitmodules` and the `160000` gitlink for `a1eb608` in one commit; both are now on `origin/main` (verified `git ls-tree HEAD .promptkit`). The entry pins no branch — acceptable for a read-only tooling submodule, since the gitlink is the pin | `git submodule status`; `.gitmodules` | `pk:commit` → done in `23ceefd` |

---

## 6. Recent Architectural Decisions (ADR Log)

| Date | Title & Scope | Decision Summary | ADR File |
| :--- | :--- | :--- | :--- |
| — | — | No ADRs recorded yet. Candidates worth writing: persistence medium (DEBT-01), `id` strategy (`number` → UUID), `.promptkit` submodule vs vendored copy | `docs/adrs/` |

---

## 7. Next Immediate Actions (Queued, exactly one is "next")

1. ✅ **Shipped this session** — `pk:fix` DEBT-11 + DEBT-13 → **`bd8a8b6`** (`noEmit: true`; `*.tsbuildinfo`,
   `.kilo/`, `.fallow/` now in shared `.gitignore`; `vite.config.js` deliberately left *visible* if it ever
   returns). Then DEBT-12 → **`f128376`** (Kilo mirror kept, both broken commands corrected).
   Post-commit re-verification: `tsc -b` exits 0 and emits **0** source files; `npm run build` 41 modules,
   no `vite.config.js`; `npm run test:run` 2/2. **`git add .` is now safe from build artifacts.**
2. ✅ **`pk:commit` + `pk:pr`** — all 8 paths were split into atomic commits on
   `chore/promptkit-engineering-os` (submodule / rule-set consolidation / intake docs / npm lockfile) and
   opened as a PR with verification evidence. DEBT-14 satisfied: `.gitmodules` + gitlink in one commit.
3. **HUMAN: review + merge the M1 PR** (← next action, and it is not mine). Then tell me it is merged and I
   will `git pull --ff-only` on `main` and open M2. Merging, pushing to `main`, tagging, deploying stay
   human-only.
4. **`pk:test`** — before any feature, decide seams: `src/domain` pure unit tests, `StatusBadge` ↔
   status-contract table test (this one directly guards DEBT-08's CSS coupling), page-level rendering tests
   with an injected repository, and a route test. Cheap insurance while the surface is 12 TS files.
5. **`pk:plan` (M2)** — spec search/filter + create/edit + persistence as Level 2 with a Task Record; answer
   open questions 1–4 in §5 first (a `pk:spike` on `localStorage` vs IndexedDB is 2 hours, not a day).
6. **`pk:fix` DEBT-05/07** — small, safe, Level 1 cleanups that make M2 easier. Do not bundle them with the M2 feature commit.
7. `pk:design` — token extraction (`DESIGN.md` §8 steps 1–4) as its own zero-visual-change PR.

---

## 8. Session Continuity Log

| Date | Engineer / Agent | Milestone / Focus | Key Changes & Artifacts |
| :--- | :--- | :--- | :--- |
| 2026-08-19 | Lead Engineer | M0 Project Inception | `d78f663 feat: Initialize job tracker React starter app` — SPA, routes, mock data, Vitest wiring |
| 2026-09-11 | Assistant (`pk:route`) | Workflow orientation | Scanned repo, verified green baseline (`tsc -b` exit 0, 2/2 tests), classified ceremony Level 0–3 for this repo, flagged template drift; routed to `pk:onboard` |
| 2026-09-10 | Assistant (`pk:onboard`) | M1 Brownfield Codebase Intake | Rewrote `PROMPTKIT.md` (real npm/Vite/React 18 stack, §4 → N/A, tailored invariants); generated `DESIGN.md` (extracted tokens + 11 measured contrast ratios, 8 anti-slop deviations, 5-step remediation); rewrote `docs/STATE.md` (9 invariants, 11 debt items — 14 after pass 2, 4 open questions); merged `AGENT.md` → `AGENTS.md` and deleted `AGENT.md`. **Incidental discovery**: verification runs of `tsc -b` created `vite.config.js`/`.d.ts`/`*.tsbuildinfo`, traced to DEBT-11 and confirmed by a throwing-config probe; all four artifacts deleted, `vite.config.ts` and `tsconfig.node.json` confirmed byte-identical to `HEAD`. **No `src/`, config, or `package.json` file was intentionally changed.** |
| 2026-09-10 | Assistant (`pk:onboard` pass 2) | Re-entrant intake + self-audit | **Swept what pass 1 skipped.** New: `.agents/rules/job-tracker-learning.md` (tracked, second doctrine copy, 9 unique rules, prescribes hanging `npm run test`) → DEBT-12; `.kilo/` 63 MB + `.fallow/` hidden by nested self-ignores with nothing in shared `.gitignore`, and 12 Kilo lines in local-only `.git/info/exclude` → DEBT-13; `.promptkit` gitlink+`.gitmodules` staged-but-uncommitted at `a1eb608` (`v1.1.1-11`) → DEBT-14. **Recovered** the 9 lost rules into `AGENTS.md` (working style 1→9 steps, new Testing principles + Avoid sections, knowledge-map warning). **Self-audit**: pass-1 counts re-verified (9 invariants, 8 deviations, 5 steps, 11 pairs, 187 CSS lines); 4 numeric errors corrected (#93c5fd ×3 not ×4; 13 src files not 12; 9 uncommitted paths not 8; `11 measurements/10 rows`). Zero `src/` or config changes; DoD re-run green. |
| 2026-09-10 | Assistant (`pk:fix`) | Remediation: DEBT-11 · 12 · 13 | **`bd8a8b6` `chore(repo)`** — `tsconfig.node.json: noEmit true` (+ explanatory comment), `.gitignore` += `*.tsbuildinfo`, `.kilo/`, `.fallow/`; `vite.config.js` intentionally *not* ignored so a recurrence stays loud. **`f128376` `docs(rules)`** — Kilo mirror kept as corrected second copy: `npm run test`→`test:run`, `npm run build`→`tsc -p … --noEmit`, canonical-source header. Both commits made with **pathspec staging** so the pending bootstrap index survived untouched. Evidence: `tsc -b` exit 0 / 0 source files emitted; `npm run build` ✓ 41 modules, no `vite.config.js`; `npm run test:run` 2/2; secret scan 0 findings; `dist/` removed after the build test. Still open: DEBT-01…10, DEBT-14 (8 paths uncommitted, 2 commits unpushed). |
| 2026-09-10 | Assistant (`pk:pr`) | M1 submission | Branch `chore/promptkit-engineering-os` off `main`; 8 pending paths split into atomic commits; `docs/` empty dirs preserved with 13 `.gitkeep` files (git does not track empty directories, so post-merge the `pk:*` artifact paths would otherwise vanish). Fresh-clone + `submodule update --init` rehearsed. **Awaiting human merge — M2 is gated on it.** |
| 2026-09-10 | Lead Engineer (human) | M1 merge | Merged PR #1 into `main` as `63d769d` (merge commit, not squash — so local `main` fast-forwarded). Verified: 0 divergence from `origin/main`, 43 tracked files, `.promptkit/workflows/plan.md` resolves. M1 closed; **M2 planning opened**. |
| 2026-09-10 21:22 UTC | Assistant (`pk:plan` → TDD) | M2 planning + M2a.1 | Routed M2 as **Level 2**, wrote `PLAN-m2-persistence-seam` (388-line RFC) + `TASK-m2-persistence-seam` before any code. Four architectural forks put to the human and all four answered: **localStorage behind an async repository interface**, **vertical CRUD slice** (filters → M2b), **`id: number → string` UUIDs now**, **TDD Enforcement Mode enabled**. Spec is source-grounded — MDN cited for localStorage's protocol-scoped areas, inadequate feature detection, `SecurityError` vs `QuotaExceededError`, and the mandate to use `setItem` over property access; the "~5MB quota" is recorded as UNCERTAINTY rather than asserted, because no primary source states it. jsdom capabilities were **measured** by a throwaway probe (randomUUID works, localStorage round-trips) so the real adapter is testable without mocks. Then M2a.1 in Red→Green→Refactor: `src/domain/validation.ts`, 13 tests passing (from 2). **Self-caught process violation**: first ladder committed Red with its implementation for two behaviours; branch was unpushed, so history was rebuilt with `git diff` proving the tree unchanged, and each state re-run to reproduce its original failure counts. DEBT-01 in progress (was open). |
| 2026-09-10 21:41 UTC | Assistant (TDD, M2a.1–M2a.2) | Validation + repository seam | **7 of 17 behaviours, 4 of 11 ACs.** `src/domain/validation.ts` (11 tests) and the `ApplicationRepository` seam with a localStorage adapter (11 tests); suite 2 → 24, typecheck clean. Contract swap `id: number → string` landed **with** its ripple: seed module replaces `mockApplications` using fixed literal UUIDs, 3 imports renamed, and the details page's `=== Number(id)` comparison corrected — a string-vs-number comparison that no test would have caught, found by reading the consumers first. Two test-design lessons came out of failures in my own tests, not the code: a `beforeEach` scoped to one `describe` did not apply to its sibling (order-dependent suite), and `list()` handing back the seed module array let a caller `pop()` the shipped demo data (found by writing the Red first, which is the entire argument for TDD). **Commit hygiene rebuilt twice** on the unpushed branch — once for Red/Green commits merged together, once when `git add <paths>` swept a stray uncommitted `remove()` into an unrelated Green commit and left two messages false about their contents; both verified with `git diff` empty against the pre-split tip. Root cause named in Task Record §6: check `git status --porcelain`, not just the paths you add. DEBT-01 still open — nothing is creatable from the UI yet. |
