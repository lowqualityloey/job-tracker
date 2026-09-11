# Project State & Living Execution Tracker

> Rewritten by `pk:onboard` on **2026-09-10**. The previous content of this file was the unfilled
> PromptKit template and described a system that does not exist here (relational schema migrations
> "complete", blocked external API, RLS invariants, UUIDv7 ADRs). Nothing in this file is a placeholder.

## 1. Executive Summary & Current Position

- **Project Name**: Job Tracker (`job-tracker` v0.2.0)
- **Current Milestone / Epic**: M0 **done** · M1 **MERGED** (PR #1) · M2a **MERGED** (PR #2 → `1ec8fe8`) · **M2b implemented, awaiting PR #3 review (Level 2)**
- **Overall Status**: ACTIVE <!-- ACTIVE | PAUSED | STABILIZING | RELEASE_CANDIDATE -->
- **Target Release / Deadline**: none. No version tag, no remote release, no deadline. `v0.2.0` in `package.json` is nominal only.
- **Current Working Branch**: `feat/m2b-filters-cross-tab` @ `f31a70b` (== `origin/…`, **published**). Base `main` @ `1ec8fe8`. **Phase advances via PR only** — see `AGENTS.md` §Branch & PR workflow
- **Last Updated**: 2026-09-11 00:41 UTC — **M2b complete**: behaviours 018–026 landed, AC-1…AC-10 met, **100 tests / 13 files**, PR #3 opened. `date -u`, measured not inferred
- **Intake passes**: pass 1 scanned manifests/config/src and wrote the profiles; **pass 2 swept the directories pass 1 never opened** (`.agents/`, `.kilo/`, `.fallow/`, `.git/info/exclude`) and audited this file's own claims. Two P1 findings came out of it: DEBT-12, DEBT-13.
- **Baseline at intake**: `npx tsc -p tsconfig.app.json --noEmit` → **exit 0** · `npm run test:run` → **2/2 passed (1.10s, 1 file)** · no known defect, no broken state, no active blocker
- **Shape of the app**: single-package React 18 SPA, 12 TS/TSX files in `src/`, 3 routes, **in-memory mock data only** — no persistence, no backend, no auth.

---

## 2. Milestone & Task Progress

### Milestone Roadmap

- [x] **M0 — Frontend Foundation**: routing, layout shell, typed domain model, mock data, status badges, empty states, dark UI, Vitest + Testing Library wiring, WCAG AA contrast baseline
- [x] **M1 — Engineering OS Integration**: **MERGED as PR #1 (`63d769d`, 2026-09-10)**. Submodule vendored, rule sets consolidated, intake profiles + this tracker written, DEBT-11/12/13 fixed, lockfile tracked
- [x] **M2a — Persistence seam**: **MERGED as PR #2 (`1ec8fe8`)**. 17 behaviours, AC-1…AC-11, repository seam + failure modes + CRUD UI + error-code contract sweep
- [/] **M2b — Filters, search, cross-tab reconciliation**: **implemented, PR #3 open — human review pending**. Behaviours 018–026 (9/9), AC-1…AC-10 (10/10), 77 → **100 tests**. Spec `docs/specs/2026-09-10-spec-m2b-filters-cross-tab.md`, record `docs/tasks/TASK-m2b-filters-cross-tab.md`
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
- [x] `TASK-m2-persistence-seam`: M2a.1–M2a.5, merged as PR #2
- [x] `TASK-m2b-filters-cross-tab`: status chips + cross-field search + `StorageEvent` reconciliation + fail-closed mid-session version gate — **complete, PR #3 open**

---

## 3. Active Working Set

- **Target Workspace / Package**: N/A — standalone repository, no workspaces
- **Active RFC / Spec**: `docs/specs/2026-09-10-spec-m2b-filters-cross-tab.md` (`PLAN-m2b-filters-cross-tab`, Level 2)
- **Active Task Spec**: `docs/tasks/TASK-m2b-filters-cross-tab.md` — Code Work, Gated, TDD Enforcement Mode **enabled**
- **Key Source Files in Flight**: `src/domain/filters.ts` (new: the predicate), `src/components/ApplicationFilters.tsx` (new: chips + search, controlled), `src/state/applicationsProvider.tsx` (snapshot + re-read + write gate), `src/domain/applicationRepository.ts` (contract gained `subscribe()`), `src/pages/ApplicationsPage.tsx` (criteria owner)
- **Verification Commands (correct for this repo)**:
  - Typecheck: `npx tsc -p tsconfig.app.json --noEmit` *(verified: exit 0, emits nothing. There is **no** `typecheck` script — the previous file advertised one that does not exist. Do **not** use bare `npx tsc -b`: see DEBT-11)*
  - Tests: `npm run test:run` · Build: `npm run build` · Dev: `npm run dev`
  - Lint: **none configured** · CI: **none configured** · E2E: **not installed**

### 3A. Execution-Control Projection

> Synchronised projection. Canonical authority is `docs/tasks/TASK-m2b-filters-cross-tab.md`.
> M2a's projection was retired into PR #2's merge; `TASK-m2-persistence-seam` is closed.

- **Local Task Source**: `docs/tasks/TASK-m2b-filters-cross-tab.md`
- **Task ID**: `TASK-m2b-filters-cross-tab` · **Planning**: `PLAN-m2b-filters-cross-tab`
- **Execution Scope**: repo `job-tracker`, branch `feat/m2b-filters-cross-tab`, paths `src/**` + `docs/**`
- **Execution State**: `handoff_ready` — implementation complete, PR #3 open, **waiting on a human decision**
- **Mapped `pk:tasks` Status**: `In Review`
- **Active Task Pointer**: `TASK-m2b-filters-cross-tab`
- **Owner / Current Actor**: Lead Engineer (accountable, reviewing) · Assistant (executing, done)
- **Current Branch / Revision**: `feat/m2b-filters-cross-tab`, published; base `main` @ `1ec8fe8`
- **Verification Status**: at the branch tip — `npm run test:run` **100/100 in 13 files**, `tsc --noEmit` exit 0,
  `npm run build` exit 0 with **no `vite.config.js`**, `git diff main -- package.json` empty, and a
  **25-commit per-commit audit in a separate clone: 0 typecheck failures, the only 8 failing commits are
  exactly the 8 Red commits**. Task-owned evidence: Task Record §6
- **CI Evidence**: `N/A` — no CI exists (DEBT-03)
- **Blockers and Resume Condition**: none open. **If the human requests changes**: read Task Record §7
  (seven recorded deviations, three of them process failures) before touching the ladder, then work the
  requested change as its own Red → Green pair on this branch. **If merged**: reconcile `main`, then start M3
- **Next Action**: none by the agent — reviewing and merging PR #3 is the **human** step
  (`AGENTS.md` §Branch & PR workflow). The agent starts no new phase until it is merged.

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

**Locked by M2a (2026-09-10, checkpoint 001) — verified in code, not merely decided:**

10. `JobApplication.id` is a **string v4 UUID** assigned by the repository; seed ids are fixed literals.
    `Number(id)` must not return.
11. The repository returns `Promise<Result<T, RepositoryError>>` and **never throws** across the seam.
12. Availability is decided by a **write probe**; `QuotaExceededError` over a non-empty store means *full*,
    everything else *off*. `typeof localStorage !== 'undefined'` is a banned check (documented false positive).
13. A `schemaVersion` newer than `CURRENT_SCHEMA_VERSION` **fails closed**: refuse reads and writes, leave the
    bytes untouched, do **not** quarantine — a newer file is not corruption.
14. Corrupt payloads are copied to `job-tracker:applications:corrupt-<iso>` **before** the live key is cleared;
    `quarantinedAs: string | null`, where `null` is reported rather than swallowed.
15. Parsed storage is rebuilt **field by field**; no `JSON.parse(...) as` assertion survives (two tests failed
    by returning `ok: true` over garbage before that was fixed).
16. Only `src/data/` may touch `window.localStorage`; `src/domain/` stays side-effect free; pages import the
    interface, never an adapter.
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
| DEBT-01 | — | **CLOSED by M2a.4** (`f1ae7af`) — create/edit/delete reach `localStorage` through the provider, and a second repository instance reading the same store is asserted in `storageFaults.test.ts`. **Residual still owed, and M2b widened it**: every persistence and cross-tab claim is jsdom-backed (jsdom never fires `storage` by itself; the tests dispatch it). The human's check is now two tabs open at `/applications`, edit or delete in one, watch the other update without a reload | — | — |
| DEBT-02 | P1 | Only 1 test file / 2 tests, and it covers a pure presentational leaf. Zero page, route, or derivation coverage | `src/components/StatusBadge.test.tsx` is the sole test | `pk:test` |
| DEBT-03 | P1 | No lint/format tooling and no CI — the DoD is unenforceable on another machine or human | no ESLint/Prettier/Biome config, no `.github/workflows/` | `pk:fix` (bootstrap config) |
| DEBT-04 | P1 | `package-lock.json` untracked → non-reproducible installs | `git status` | `pk:commit` |
| DEBT-05 | — | **CLOSED by M2a.4** (`bd15e4a`) — the three module-scope `filter()` passes became `domain/pipeline.summarise()`, computed per render from provider data | — | — |
| DEBT-06 | P2 | Three pages import `mockApplications` directly — no repository seam to swap for HTTP later | `DashboardPage`, `ApplicationsPage`, `ApplicationDetailsPage` | `pk:plan` (M2) |
| DEBT-07 | — | **CLOSED by M2a.4** (`d33c4ae`) — the lookup is provider-scoped and gated on `status === "ready"`, so a slow read says "Loading" instead of claiming the record is gone | — | — |
| DEBT-08 | P2 | No design-token layer: colours/radii/gaps are ~20 hardcoded literals; `Interview`/`Offer`/active-nav contrast have <0.4 ratio of AA headroom | `src/styles.css`, `DESIGN.md` §2, §6 | `pk:design` |
| DEBT-09 | P3 | `100vh` instead of `dvh`; no `min-width: 0` / truncation on children at 320px; no `:active` state, no motion or reduced-motion block | `src/styles.css:14-19`, `DESIGN.md` §5, §7 | `pk:design` |
| DEBT-10 | P3 | Catch-all `<Route path="*">` silently redirects to `/`, so typo'd and deep links are indistinguishable from home; no skip link; `<title>` never changes per route | `src/App.tsx:14`; `index.html:6` | `pk:fix` |
| DEBT-11 | — | **[CLOSED]** ✅ **FIXED in `bd8a8b6`.** **`tsc -b` emits a `vite.config.js` that shadows the real `vite.config.ts`.** Verified by probe on 2026-09-10: with a throwing `vite.config.js` present, `npx vite build` failed with *my* error (`error during build: Error: PROBE: vite.config.js WAS LOADED`) while `npx vitest run` stayed green — Vite resolves `.js` **before** `.ts` (`DEFAULT_CONFIG_FILES`, `node_modules/vite/dist/node/constants.js:33`), Vitest prefers `.ts`. So any future edit to `vite.config.ts` is silently ignored by dev/build but obeyed by tests, and if the stale file is ever committed every clone and CI run inherits it. `npm run build` produces this file on every run, and `vite.config.d.ts` + `*.tsbuildinfo` land beside it — none gitignored. **Fix (both halves verified this session):** add `"noEmit": true` to `tsconfig.node.json` (accepted alongside `composite: true` by TS 5.5.4, `tsc -b` exits 0, emission stops) and add `*.tsbuildinfo` to `.gitignore` (still written by composite builds) | `tsconfig.node.json` (no `noEmit`), `package.json:9` build script, `node_modules/vite/dist/node/constants.js:33` | `pk:fix` (2-line Level 1 chore, do before M2) |
| DEBT-12 | — | **[CLOSED]** ✅ **FIXED in `f128376`** (kept as a corrected mirror, per decision). **A second, git-tracked copy of the agent doctrine was missed at pass 1.** `.agents/rules/job-tracker-learning.md` (committed in `d78f663`, 51 lines) is the Kilo Code rules file. It held **9 rules that existed nowhere else** — inspect-before-changing, restate-the-goal, one follow-up practice task, behaviour-focused tests, loading/empty/error/success coverage, no unnecessary libraries, no unrelated refactors, no premature abstraction, no huge code dumps (verified: each phrase present in `.agents/`, absent from `AGENTS.md` at pass 1). Those 9 are now mirrored into `AGENTS.md`. **Remaining conflict:** line 41 tells agents to run `npm run test`, which is `"vitest"` → **watch mode, never exits** — any obeying agent or scripted step hangs. Line 40 also routes all verification through `npm run build`, i.e. through the DEBT-11 polluting command | `.agents/rules/job-tracker-learning.md:40-41`; `package.json:8` (`"test": "vitest"`) | Decide + `pk:fix`: either delete it and let Kilo read `AGENTS.md`, or keep it as the IDE mirror and change line 40-41 to `npx tsc -p tsconfig.app.json --noEmit` + `npm run test:run` |
| DEBT-13 | — | **[CLOSED]** ✅ **FIXED in `bd8a8b6`** (shared `.gitignore` now names them; the 12 local-only `.git/info/exclude` lines are harmless leftovers). **63 MB of tool state is invisible to git.** `.kilo/` (`@kilocode/plugin` 7.4.23 + its own `node_modules`) hides behind `.kilo/.gitignore`, and `.fallow/` behind `.fallow/.gitignore` (`*`). Nothing in the **shared** `.gitignore` names either directory, so the protection lives inside the tool, not in the repo. Tree-walking tools (`find`, glob, greps, subagents) see them: pass 2's debt-marker scan returned 8 `TODO:` hits from `.kilo/node_modules/**` — pure noise. Also `.git/info/exclude` carries 12 Kilo worktree entries, which are **local-only and vanish in every other clone** | `du -sh .kilo` = 63M; `.kilo/.gitignore`; `.fallow/.gitignore`; `.git/info/exclude:9-45` | `pk:fix` — add `.kilo/`, `.fallow/`, `*.tsbuildinfo` to shared `.gitignore`; never `git add .` |
| DEBT-15 | P2 | **Nothing in this repo validates CSS or markup structure.** `git blame` proves three orphan declarations (`margin-top: 10px; padding: 8px 12px; }`) were written into `styles.css` by the `f1ae7af` scripted patch and survived **21 commits, a typecheck, a build, and up to 100 tests** — because CSS is imported as a side effect, never parsed by `tsc`, and Vite does not error on an unparseable rule. Fixed in `f92bb74` on this branch, but the *class* is open: the same misapplication in a `.css`/`.html`/`.md` file is invisible to the current Definition of Done | `git blame -L 327,329 src/styles.css` at `f1ae7af`; `npx vite build` exit 0 with the orphan present | `pk:fix` — cheapest real gate is a `stylelint` config with `no-invalid-position-at-import-rule` + `declaration-block-no-shorthand-property-overrides`, folded into DEBT-03's CI bootstrap |
| DEBT-16 | P3 | **`reload.test.tsx` does not unmount; it wipes the DOM.** `unmountAll()` sets `document.body.innerHTML = ''`, which the file's own comment admits is a "simple stand-in". The React root stays mounted, so the second `mount()` runs with the first tree alive — and since M2b every mounted provider subscribes to `storage`. No test in that file dispatches a storage event, so nothing is wrong today; the first person who adds one there will get two live providers answering and a confusing failure | `src/state/reload.test.tsx:71-75`; RTL exposes `unmount()` on the render result, which is unused | `pk:test` — replace with `const { unmount } = mount(); … unmount()`; pair with the `pk:test` seam plan |
| DEBT-14 | — | **[CLOSED]** ✅ **CLOSED by merge.** `.promptkit` sits at `a1eb608` (`v1.1.1-11-ga1eb608`, clean, detached HEAD as expected) `23ceefd` landed `.gitmodules` and the `160000` gitlink for `a1eb608` in one commit; both are now on `origin/main` (verified `git ls-tree HEAD .promptkit`). The entry pins no branch — acceptable for a read-only tooling submodule, since the gitlink is the pin | `git submodule status`; `.gitmodules` | `pk:commit` → done in `23ceefd` |

---

## 6. Recent Architectural Decisions (ADR Log)

| Date | Title & Scope | Decision Summary | ADR File |
| :--- | :--- | :--- | :--- |
| — | — | No ADRs recorded yet. Candidates worth writing: persistence medium (DEBT-01), `id` strategy (`number` → UUID), `.promptkit` submodule vs vendored copy | `docs/adrs/` |

---

## 7. Next Immediate Actions

1. **HUMAN**: review and merge **PR #3** (`feat/m2b-filters-cross-tab` → `main`). Reviewer focus is listed in
   the PR body; the two things worth the most attention are D-2 (the `subscribe()` contract change, which
   alters M3's interface) and D-7 (three history rebuilds, all disclosed). While reviewing, spend 30 seconds
   on the DEBT-01 residual: two tabs, edit in one, watch the other.
2. **Agent, on merge**: reconcile (`git checkout main && git pull --ff-only`), close M2b in §2, then start
   **M3 — Backend (ASP.NET Core Web API)** at **Level 2**: `pk:plan` first (domain model, API envelope, the
   four open architectural questions in §5 that M2a already answered three of), then `pk:api` + `pk:data`.
   M3's repository swap is the payoff for `subscribe()`: an HTTP adapter implements the same interface and no
   page changes. **Stop at PR #4.**
3. **Agent, standing duty (honoured this session)**: keep the branch published after each slice, not only at
   PR time. M2b was pushed slice by slice, force-pushed **twice** with `--force-with-lease` — both times to
   repair its own history before anyone could review it (Task Record §7 D-6, D-7). Merging stays the human's.
4. **Cheap, high-value, do before M3 if the branch is quiet**: `pk:fix` DEBT-03 (no lint, no CI). M2b produced
   **three process failures that no tool could see** — a commit over typecheck errors, a commit whose message
   described only one of its two Greens, and a `git commit --amend` that silently committed nothing because
   the fix was never staged. A `tsc && stylelint && test` GitHub Action catches two of the three on every push.
   DEBT-15's stylelint config rides along in the same change.
5. **Later, gated**: `pk:test` seam plan (100 tests, still zero coverage above the component leaf; see
   DEBT-16); `pk:design` token extraction (`DESIGN.md` §8 — M2b's CSS block adds **17 colour-literal
   occurrences across 7 distinct values, all 7 already in §2's measured palette**: nothing new was invented,
   which is right for consistency and wrong for DEBT-08, whose count grows from ~20 to ~37); DEBT-02/04/06/08/09/10/15/16 remain open.

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
| 2026-09-10 21:53 UTC | Assistant (TDD, M2a.3) | Failure-mode slice | **11 of 17 behaviours, 6 of 11 ACs, suite 24 → 41.** Availability is decided by a **write probe**, not an existence check — MDN documents browsers that keep `window.localStorage` present while every write is discarded, and my own Red proved the hazard: `create()` on such a store returned **ok: true**. A quota refusal mid-write now maps to a typed error with the envelope proven byte-identical; the stored payload gained a real per-record decode after two tests failed by returning `ok: true` over garbage (a runtime-erased `as` assertion validated nothing); a newer `schemaVersion` refuses reads **and** writes and is deliberately **not** quarantined, because treating a newer file as corruption is how a downgrade deletes records. Three self-caught process failures this slice, all disclosed in Task Record §6: a `describe`-scoped `beforeEach` making a suite order-dependent, a scripted edit writing a template literal with escaped backticks that produced 17 tsc errors, and — the root cause of the last one — **committing anyway because verification and commit were chained on one shell line without `set -e`**. That commit was amended (unpushed) rather than left asserting evidence its tree could not produce, and all 29 commits were then audited in a separate clone: 26 clean, 3 failing = exactly the Red commits, 0 unexpected. Five durable rules added to `AGENTS.md` §Commit discipline + Kilo mirror in the same commit. |
| 2026-09-10 22:01 UTC | Assistant (`pk:checkpoint`) | Checkpoint 001 + Handoff 001 | Level 2 checkpoint contract satisfied: `docs/tasks/TASK-m2-persistence-seam.checkpoint-001.md` (state, evidence, 9 locked invariants, one next action) and `.handoff-001.md` (receiver pass V1–V7, guard rails). Execution State deliberately left **`in_progress`**, not `handoff_ready`: nothing is blocked, the records exist so a context restart costs nothing, and a fresh session still runs V1–V7 before editing. Hygiene scan clean — 0 debug residue (the 5 `PROBE` grep hits are the production availability probe), 0 `.only`/`.skip`, 0 `.env`, 0 build artefacts. Evidence re-measured at `e5d0bbf`: **41/41 tests, tsc exit 0**. §7 rewritten because it had gone stale (still listed the M1 merge as pending). **Running my own validation pass found a bad check**: `grep " as JobApplication"` hits a comment explaining why that assertion was removed, so V5 was tightened to `JSON.parse(... as ` — a receiver's check must discriminate code from prose about code. |
| 2026-09-10 22:25 UTC | Assistant (`pk:checkpoint` continuation) | M2a.4 — CRUD UI | Behaviours 012–017 in 11 commits: `ApplicationsProvider` + `useApplications`, `ApplicationForm` serving create and edit from one page, `data/applicationStore.ts` as the sole composition root, `domain/pipeline.summarise` (first domain module a page uses), provider-scoped details lookup with an honest loading state, and delete behind an inline `role=group` confirmation. **68 tests, 9 files; tsc 0; build 0 with no `vite.config.js`.** Closes DEBT-01/05/07 — 7 of 14 debt rows now shut. AC-1…AC-10 met; AC-11 owed at M2a.5. Five deviations disclosed as D-4…D-8 in the Task Record, of which D-4 matters most: 014 produced no Red because 013 had already wired the behaviour, so it is recorded as coverage rather than staged as a failure. Two scripted patches misapplied and the pre-commit gate caught both, which is the M1 incident finally paid off. |
| 2026-09-10 23:00 UTC | Assistant (`pk:checkpoint`) | Checkpoint 002 + Handoff 002 | Level 2 checkpoint contract for the M2a.4 milestone: `checkpoint-002.md` (17/17 behaviours, AC-1…AC-10, evidence re-measured *in this checkpoint* rather than carried forward, 9 new invariants, gaps, one next action) and `handoff-002.md` (V1–V10, each executed before being written). Supersedes 001's next action; 001's invariants stand. Execution State stays `in_progress`. Measured here and now: 68 tests / 9 files, tsc 0, build 0, no `vite.config.js`, 0 storage references outside `src/data/`, 0 `Number(id)`, 3 of 14 debt rows countable as closed. Two findings worth more than the bookkeeping: **persistence has never been checked in a real browser** (every claim is jsdom-backed, so DEBT-01's closure carries a named residual), and the **debt register marks closures inconsistently** — DEBT-11…14 say FIXED in prose while still reading `P1`/`P2`, which is why no aggregate count appears anywhere. |
| 2026-09-11 00:41 UTC | Assistant (M2b) | Filters, search, cross-tab reconciliation | Behaviours 018–026 in a Red → Green ladder: `domain/filters.ts` (pure predicate, `AND` over status + query, `normaliseQuery` trim/lowercase), `ApplicationFilters` (controlled chips with `aria-pressed`, `role="search"` + real `<label>`), a text-only polite count region, a zero-match state that blames the filter instead of the account, and one-action clear. Cross-tab: **`repository.subscribe()` added to the seam** so the storage key and `window` stay in `src/data/` (D-2, against spec §3's letter and for its reason — the event's `newValue` is never parsed, the subscriber re-reads through `list()` and keeps all four M2a safety mechanisms); `key: null` re-reads instead of assuming empty; a `not-found` from update/delete reconciles the snapshot (no ghost row); a newer `schemaVersion` arriving mid-session flips the tab to `error` **and refuses writes**, carrying the existing error rather than inventing an eighth code. 77 → **100 tests / 13 files**, tsc 0, build 0 with no `vite.config.js`, zero new dependencies; 25 commits audited individually — 0 typecheck failures, only the 8 Reds fail their own tests. **Three self-caught process failures, all disclosed**: a commit made over two typecheck errors (the gate ran `test:run`, which never typechecks `.test.tsx`), a commit carrying two Greens under one name, and a history rewrite that dragged a fix two commits early and silently un-Red-ed a Red commit. Also fixed two pieces of debris M2a's scripted patches left behind: three orphan CSS declarations (`f92bb74`, → DEBT-15) and commit-process comments inside the provider's `useMemo` (`2fa85d3`). New debts logged: DEBT-15, DEBT-16. |
| 2026-09-10 23:25 UTC | Assistant (M2a.5) | Contract sweep + AC-11 | Matrix of all 7 `RepositoryError` codes on two axes (produced-by-test x rendered-in-UI) found two real gaps, not two missing assertions: `validation` carries `fieldErrors` that nothing consumed, so a per-field rejection was being flattened into a generic banner — strictly worse than no rejection; and `storage-error` had no case, reaching the default sentence while carrying a developer-facing `detail`. Both fixed in `7589bab`, with the `detail` string pinned as NOT user-visible. `reload.test.tsx` proves persistence through the real component tree with a fresh repository instance per mount (77 tests total, tsc 0, build 0, no `vite.config.js`). Debt register normalised: DEBT-11..14 were saying FIXED in prose while still reading P1/P2, which is why no closure count was quotable before. |
