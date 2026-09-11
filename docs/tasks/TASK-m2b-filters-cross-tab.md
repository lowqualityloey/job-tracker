# Task Record — TASK-m2b-filters-cross-tab

- **Work Type**: Code Work · **Profile**: `sdlc-overlay-v1` · **Ceremony**: Level 2 (Controlled)
- **Planning link**: `PLAN-m2b-filters-cross-tab` (`docs/specs/2026-09-10-spec-m2b-filters-cross-tab.md`)
- **Created / Start Time**: 2026-09-10 23:40 UTC (`date -u`, measured not inferred)
- **Execution State**: `in_progress` · **Active Task Pointer**: `TASK-m2b-filters-cross-tab`
- **Mode**: Gated · **TDD Enforcement Mode**: `enabled` (carried forward from M2a by the same reasoning:
  the persistence layer ships silently broken data if its edge cases are wrong)
- **Estimates**: soft ~50 min, hard ≤80 min. <!-- Actual elapsed time is unmeasurable here: POLICY_LIMITATION, no wall-clock timer. --> **`Host Timer Capability`: none** — this agent has no
  wall-clock timer, so these are estimates only and no elapsed-time claim appears in any evidence line
  (`POLICY_LIMITATION`).

## 1. Objective

A user can narrow the application list by status and free text without losing or misrepresenting data,
and a change made in another tab reconciles into an already-open tab without a reload.

## 2. Scope

`domain/filters.ts`; `components/ApplicationFilters.tsx`; criteria in `ApplicationsPage`;
`StorageEvent` subscription + `not-found` reconciliation in `ApplicationsProvider`; tests for all of it.
**Out**: URL sync, sorting UI, pagination, fuzzy/accent matching, debounce, merge resolution, backend,
new dependencies. **Approval boundary**: agent may commit, push this branch, and open PR #3; merging,
`main` pushes, tags, deploys stay human.

## 3. Acceptance Criteria

- [x] **AC-1** `BEHAVIOR-m2b-filters-cross-tab-018` — a status chip narrows the visible list and the count is announced
- [x] **AC-2** `…-019` — a query matches company, title, location and notes, insensitive to case and surrounding whitespace
- [x] **AC-3** `…-020` — zero matches renders "nothing matches this filter" with a one-action clear, never "no applications yet"
- [x] **AC-4** `…-021` — clearing returns the full list and leaves exactly one active control
- [x] **AC-5** `…-022` **and** `…-023` — a dispatched `StorageEvent` for our key updates the mounted UI; a foreign key does not
- [x] **AC-6** `…-024` — `key: null` re-reads instead of assuming empty
- [x] **AC-7** `…-025` — a `not-found` from a write reconciles the snapshot; no ghost row survives
- [x] **AC-8** `…-026` — a newer `schemaVersion` mid-session flips to error and refuses writes
- [x] **AC-9** — whole branch: `test:run`, `tsc --noEmit`, `npm run build` clean, **no `vite.config.js`**, `git diff main -- package.json` empty
- [x] **AC-10** — a11y per Definition of Done: labelled `role="search"`, chips as buttons with `aria-pressed`, polite live count, 44 px, §6 focus ring

## 4. Verification Condition

Every behaviour lands as Red → Green → (Refactor) in separate commits, gated on the suite output being
scanned for `failed` before any commit; AC-9 measured at branch close; deviations recorded as they occur,
not reconstructed afterwards.

## 5. Stop Conditions

Red that cannot go Green without changing an AC → **Scope Change Record**, stop and ask. Any new
dependency. Any test needing a timer. Any evidence of `vite.config.js`. A `StorageEvent` design that
requires parsing `event.newValue` → stop: spec §4 forbids it for a data-safety reason, not a taste one.

## 6. Completion Evidence

| Slice | Red | Green | Suite at Green (measured) |
| :--- | :--- | :--- | :--- |
| M2b.1 | `e8fc945` 018 (2 failing) | `638387f` | 79 |
| M2b.1 | `efa3cc5` 019 (6 failing) | `8c6295e` | 85 |
| M2b.1 | `8ddd58b` 020 (1 failing) | `ce75e35` | 87 |
| M2b.1 | — (D-1) 021 | `1755284` coverage | 89 |
| M2b.1 | — refactor, no behaviour | `8c8b4b2` | 89 |
| M2b.2 | `d1d7c28` 022 (1 failing) | `c583c15` | 91 |
| M2b.2 | — (D-1) 023 | `0231a2b` coverage | 92 |
| M2b.2 | `fc5e1d3` 024 (1 failing) | `cc3f6d0` | 93 |
| M2b.2 | `da9d920` 025 (2 failing) | `7d37dae` | 95 |
| M2b.2 | `455fdcd` 026 (1 failing) | `10f1cc9` | 96 |
| a11y | `eeea1cd` live region (1 failing) | `0df42fb` | 97 |
| Close | — | `fa8ff30` AC-10 coverage · `0755adb` recovery · `52935e2` B-9 | **100** |

**Whole-branch gate (AC-9), measured at `52935e2`:**

| Check | Result |
| :--- | :--- |
| `npm run test:run` | **100 passed / 13 files**, 0 failed |
| `npx tsc -p tsconfig.app.json --noEmit` | **exit 0** |
| `npm run build` | **exit 0**, 52 modules transformed |
| `vite.config.js` / `vite.config.d.ts` after build | **absent** — DEBT-11 stays closed |
| `git diff main -- package.json package-lock.json` | **empty** — zero new dependencies |
| Per-commit audit (separate clone, **29 commits** — re-run after the docs and refactor commits landed) | **0 typecheck failures**; the only failing commits are exactly the 8 Reds, at 2/6/1/1/1/2/1/1 failures |
| storage references outside `src/data/` | 0 code hits (3 prose mentions in comments) |
| `.only` / `.skip` / `console.log` / `TODO` / `Number(id)` | 0 hits |

## 7. Deviations (recorded as they occurred)

- **D-1 — three behaviours produced no Red.** 021 (one clear resets both filters), 023 (a foreign
  storage key changes nothing), and the AC-10 landmark/label assertions were satisfied by code their
  neighbouring Greens already needed. Their commits say `coverage`, not `Red`. 020's second case
  ("controls stay mounted") was the same. This is disclosed rather than backdated because a Red that
  never failed is evidence for nothing.
- **D-2 — spec §3 put the `StorageEvent` subscription in the provider; it lives in the adapter.**
  The provider would have had to import `APPLICATIONS_STORAGE_KEY` from `src/data/`, which breaks
  invariant 16 and would leave `state/` naming a `localStorage` key that M3's HTTP client has no
  equivalent of. The contract gained `repository.subscribe(onExternalChange): () => void`; the
  provider still owns the snapshot and the re-read, the adapter owns *how* it learns. Spec §3's
  "one subscription" is unchanged in substance.
- **D-3 — FMEA B-4's feature guard was deliberately not implemented.** Registration cannot fail the
  way a write can, and the provider is only ever mounted inside a DOM, so an `if (!window.StorageEvent)`
  branch is unreachable code, not a mitigation. Stated in `applicationsProvider.tsx` so the next
  reader does not "fix" the omission.
- **D-4 — an a11y defect I introduced and my own Red caught.** `ce75e35` put the *Clear filters*
  button **inside** the `role="status"` region, so every count announcement also announced a control
  (`a9ccdef` → `e121d8e`). No review step was needed once the assertion existed.
- **D-5 (process) — a commit was made over two typecheck errors.** `dc80228`'s predecessor used
  `input.labels` on an `HTMLElement`. Its commit gate ran `npm run test:run` and **not** `tsc`, and
  Vitest strips types without checking them, so the commit looked clean. Found at the AC-9 gate.
  Fixed by asserting the `label[for]` pairing through the DOM instead of adding a cast, and amended
  while unpushed. **Durable rule: the gate for a commit that touches a `.test.tsx` file is
  `tsc && test:run`, never `test:run` alone** — the suite cannot see type errors.
- **D-6 (process) — one commit carried two Greens while naming one.** `a0ef077` held both
  BEHAVIOR-025's and 026's implementation but its message claimed 026. The `git status` line showing
  `M src/state/applicationsProvider.tsx` was the warning sign, ignored because the next command was a
  commit. Rebuilt as `74965bb` + `b8e8b55`; the rewrite is proven by `git diff` against the pre-split
  tip being **empty**, then re-audited per commit and force-pushed with `--force-with-lease` before
  any review existed. Root cause is the same as D-5: staging a file because it is dirty, not because
  the current behaviour asked for it.
- **D-7 (process) — the split itself was wrong twice, and both audits nearly passed it by mistake.**
  Rebuilding history exposed three further faults, each caught only because the tree was re-measured:
  (a) `git commit --amend` **without staging** left the fix in the worktree, so the rebuilt 025 Green
  committed a `refusal` binding that no longer existed — 52 failing tests;
  (b) the first re-audit ran against the **old** branch tip, because the work was on a detached HEAD
  and the branch ref had not moved, so it "proved" a history that was never there;
  (c) restoring whole files from the pre-split tip (`git checkout <old-tip> -- <file>`) dragged the
  a11y fix two commits early, which silently un-Red-ed `eeea1cd` and made `0df42fb`'s message false
  about its own contents.
  Final state after the second rebuild: tree byte-identical to the superseded tip, 25 commits audited
  clean, 8 Reds failing exactly their own tests. **Durable rules learned: after any history rewrite,
  re-point the branch ref and confirm the audit's tip SHA before trusting a single number; and when
  splitting a commit, reconstruct the *diff*, never the whole file.**

## 7A. Residuals owed

- **Real-browser cross-tab check.** Every claim here is jsdom-backed. jsdom does **not** fire
  `storage` events on its own — the tests dispatch them by hand, which is the correct simulation of
  the browser rule (the event never fires in the writer) but is still a simulation. The manual
  two-tab check (open `/applications` twice, edit in one, watch the other) is the human's 30 seconds.
  **CLOSED 2026-09-11 — run in real Chromium 128, 13/13.** Both directions passed: a create in tab A appeared
  in tab B with tab B never reloaded (6 cards, `"Showing 6 of 6 applications"`), and a create in tab B appeared
  in tab A. Envelope on disk: `schemaVersion: 1`, 7 applications, exactly one key. Limits of the evidence —
  Chromium only, happy path only — are stated in `docs/spikes/2026-09-11-real-browser-cross-tab-check.md`.
- **No CSS assertion harness.** 44 px targets and the `:focus-visible` ring were verified by reading
  `src/styles.css` (all three new controls have both), not by rendering. Recorded as DEBT-15's
  sibling: nothing in this repo can fail a build for a missing focus ring.
  **Measured 2026-09-11 in a real browser** — chip 45px rendered (`min-height: 44px`), input 46px, `:focus-visible`
  ring `solid 3px rgb(147,197,253)`, first chip reachable in 3 Tab stops. The *tooling* half of this residual
  stands: that came from a manual harness, so nothing on `main` can fail a build if a later change removes the
  ring.

## 7B. Readiness Pointers

Spec §7 depth note applies: this record's planning input is a deliberately condensed Level 2 spec written
under a truncated context window. Behaviours 018–026 are new ids continuing M2a's numbering. Checkpoint
records begin at `…-001` on the first milestone close.
