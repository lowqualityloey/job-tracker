# Task Record — TASK-m2b-filters-cross-tab

- **Work Type**: Code Work · **Profile**: `sdlc-overlay-v1` · **Ceremony**: Level 2 (Controlled)
- **Planning link**: `PLAN-m2b-filters-cross-tab` (`docs/specs/2026-09-10-spec-m2b-filters-cross-tab.md`)
- **Created / Start Time**: 2026-09-10 23:40 UTC (`date -u`, measured not inferred)
- **Execution State**: `in_progress` · **Active Task Pointer**: `TASK-m2b-filters-cross-tab`
- **Mode**: Gated · **TDD Enforcement Mode**: `enabled` (carried forward from M2a by the same reasoning:
  the persistence layer ships silently broken data if its edge cases are wrong)
- **Estimates**: soft ~50 min, hard ≤80 min. **`Host Timer Capability`: none** — this agent has no
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

- [ ] **AC-1** `BEHAVIOR-m2b-filters-cross-tab-018` — a status chip narrows the visible list and the count is announced
- [ ] **AC-2** `…-019` — a query matches company, title, location and notes, insensitive to case and surrounding whitespace
- [ ] **AC-3** `…-020` — zero matches renders "nothing matches this filter" with a one-action clear, never "no applications yet"
- [ ] **AC-4** `…-021` — clearing returns the full list and leaves exactly one active control
- [ ] **AC-5** `…-022` **and** `…-023` — a dispatched `StorageEvent` for our key updates the mounted UI; a foreign key does not
- [ ] **AC-6** `…-024` — `key: null` re-reads instead of assuming empty
- [ ] **AC-7** `…-025` — a `not-found` from a write reconciles the snapshot; no ghost row survives
- [ ] **AC-8** `…-026` — a newer `schemaVersion` mid-session flips to error and refuses writes
- [ ] **AC-9** — whole branch: `test:run`, `tsc --noEmit`, `npm run build` clean, **no `vite.config.js`**, `git diff main -- package.json` empty
- [ ] **AC-10** — a11y per Definition of Done: labelled `role="search"`, chips as buttons with `aria-pressed`, polite live count, 44 px, §6 focus ring

## 4. Verification Condition

Every behaviour lands as Red → Green → (Refactor) in separate commits, gated on the suite output being
scanned for `failed` before any commit; AC-9 measured at branch close; deviations recorded as they occur,
not reconstructed afterwards.

## 5. Stop Conditions

Red that cannot go Green without changing an AC → **Scope Change Record**, stop and ask. Any new
dependency. Any test needing a timer. Any evidence of `vite.config.js`. A `StorageEvent` design that
requires parsing `event.newValue` → stop: spec §4 forbids it for a data-safety reason, not a taste one.

## 6. Completion Evidence

| Slice | Red | Green | Suite |
| :--- | :--- | :--- | :--- |
| (pending) | | | |

## 7. Deviation and Readiness Pointers

Spec §7 depth note applies: this record's planning input is a deliberately condensed Level 2 spec written
under a truncated context window. Behaviours 018–026 are new ids continuing M2a's numbering. Checkpoint
records begin at `…-001` on the first milestone close.
