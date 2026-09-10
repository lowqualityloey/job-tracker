# Checkpoint Record 002 — TASK-m2-persistence-seam

- **Task ID**: `TASK-m2-persistence-seam` · **Canonical record**: [`TASK-m2-persistence-seam.md`](TASK-m2-persistence-seam.md)
- **Specification / Planning**: `docs/specs/2026-09-10-spec-m2-persistence-seam.md` (`PLAN-m2-persistence-seam`, Full, Level 2)
- **Checkpoint at**: 2026-09-10 22:58 UTC · **Trigger**: major milestone (M2a.4 complete, all behaviours landed) + explicit `pk:checkpoint`
- **Execution State**: `in_progress` — a stop state is not warranted; nothing is blocked
- **Branch / audited revision**: `feat/m2-persistence-seam` @ `0c46b42`, 44 commits over `main` (`63d769d`)
- **Which commit carries these records**: not nameable here — a file cannot hold the hash of its own commit, and an
  amend invalidates any hash written into it. Find it with
  `git log --grep='docs(m2a.4): close AC-7..AC-10' -1 --format='%h %s'` and validate **ancestry**, not equality.
- **Supersedes**: [`checkpoint-001`](TASK-m2-persistence-seam.checkpoint-001.md) §6, whose "next action" (Red for
  behaviour 012) is done. Its invariants and evidence still stand.
- **Release-evaluation handoff fragment**: N/A — no release candidate, tag, or QA gate exists for this work.

## 1. Objective (unchanged)

A user can create, edit, and delete a job application and still see those changes after a page reload, with every
read and write passing through one `ApplicationRepository` interface.

## 2. Completed

| Slice | Behaviours | Evidence |
| :--- | :--- | :--- |
| M2a.1 domain validation | 001–003 | `fdffc2c` `714c108` `d35fe10` `1f6aba2` |
| M2a.2 repository seam | 004–007 | `d82b2dc` … `c67b04f` |
| M2a.3 failure modes | 008–011 | `4d72564` `6b86025` `a721971` `892d56e` |
| M2a.4 CRUD UI | 012–017 | `55030d6` `6228370`→`01f9c7d` `b1c9c1f` `4b464b5`→`bd15e4a` `d33c4ae` `a9a48f4`→`f1ae7af` |

**17 of 17 behaviours · AC-1…AC-10 met · AC-11 open** (it is the whole-branch gate and belongs to M2a.5).
`src/` is now 29 files / 2 675 lines. Landed in M2a.4: `state/applicationsProvider.tsx`,
`components/ApplicationForm.tsx`, `components/StorageNotice.tsx`, `pages/ApplicationFormPage.tsx`,
`data/applicationStore.ts`, `domain/pipeline.ts`.

## 3. Verification evidence (all re-measured for this checkpoint, not carried over)

| Check | Result |
| :--- | :--- |
| `npm run test:run` | **68 passed (68)**, 9 files |
| `npx tsc -p tsconfig.app.json --noEmit` | exit 0 |
| `npm run build` (`tsc -b && vite build`) | exit 0, built in 1.27 s, **no `vite.config.js` emitted** |
| Debt register | DEBT-01, 05, 07 shut by M2a.4 — see §5 for a bookkeeping inconsistency in how closures are marked |
| CI | **N/A — this repository has no CI (DEBT-03)** |
| Prior whole-branch audit | 29 commits, 26 typecheck-clean, 3 failing = exactly the Red commits, 0 unexpected (pre-M2a.4) |

## 4. Invariants this checkpoint locks (additions to checkpoint-001 §4)

1. **`src/data/applicationStore.ts` is the composition root**: the only file outside the adapter that may name
   `window.localStorage`. Availability, the fallback repository, and the notice all come from one decision.
2. `ApplicationsProvider` takes the repository **as a prop**. It is never constructed inside a page, and the app
   builds it once via a lazy `useState` — its load effect depends on repository identity, so constructing it
   inline would reload on every render.
3. Writes replace the snapshot **only after the repository resolves ok**. No optimistic remove anywhere.
4. Pages read through the provider and gate on `status`. A lookup that has not resolved is reported as loading,
   never as "not found" — that conflation was DEBT-07.
5. `domain/pipeline.summarise()` is the only place dashboard counts are computed; pages contain no arithmetic.
6. UI adds **zero** packages. `@testing-library/user-event` is not installed, so tests click with `fireEvent`
   (lower interaction fidelity, accepted and recorded as D-6).
7. New controls follow `DESIGN.md` §5 (44 px minimum) and §6 (`outline: 3px solid #93c5fd; outline-offset: 3px`).
   The 12 px field radius is a **new, unratified** value, flagged in `styles.css` for `DESIGN.md` §4.
8. Destructive actions require an explicit second control (`role=group` + named Cancel), never `window.confirm`.
9. Red, Green and Refactor stay separate commits; Green commits are gated on grepping the suite output for
   `failed` and exiting non-zero before `git add`.

## 5. Known gaps and risks

- **AC-11 residue (real)**: persistence is proven against jsdom only. **No manual reload check in a real
  browser has been performed.** That is the honest remaining half of DEBT-01's closure.
- **M2a.5 sweep seed, measured this checkpoint** — codes with no distinct UI branch:
  `validation` (tests=0 by code string, ui=0) and `storage-error` (tests=1, ui=0). Both currently fall through
  to a generic sentence. `not-found`, `unavailable`, `quota-exceeded`, `corrupt-data`, `unsupported-version`
  each have at least one producing test and one rendering branch.
- **Bookkeeping inconsistency found while measuring**: DEBT-01/05/07 were closed by blanking the severity column
  (`—`), while DEBT-11…14 say FIXED/resolved inside their description text and still show `P1`/`P2`. So the
  table supports "3 rows closed" and not any aggregate like "7 of 14". Tidy the register during M2a.5; do not
  quote an aggregate count until it does.
- **Principal risk unchanged**: `git ls-remote --heads origin feat/m2-persistence-seam` → **0 heads**. 44 commits
  and ~1 000 lines of records exist on this machine only, with no CI. Mitigation is the human's:
  `git push -u origin feat/m2-persistence-seam` (a branch push, not a merge).
- **Deferred by decision**: F-5 cross-tab `StorageEvent` reconciliation (M2b), fallback UUID generator (M5),
  filters/search (M2b), `DESIGN.md` §8 token extraction, `updatedAt`.
- **UNCERTAINTY-001-1** (localStorage quota size) never asserted; design stays quota-agnostic.
  **UNCERTAINTY-002-1** (`crypto.randomUUID` needs a secure context) gated to M5 deployment.
- **`POLICY_LIMITATION`**: no host wall-clock timer is available to this agent, so the Task Record's soft ~60 /
  hard ≤90 min figures remain *estimates only* and no elapsed-time claim is made in any evidence line.

## 6. Scope changes

**None.** No Scope Change Record and no TDD Exception Record exists or is needed. Five process deviations
(D-4…D-8, Task Record §6) are disclosed there; D-4 in particular records that behaviour 014 produced **no Red**
because 013 had already implemented it, and was committed as coverage rather than staged as a failure.

## 7. Exactly one prioritised next action

> **M2a.5, first step: build the error-code contract matrix.** For all seven `RepositoryError` codes, record one
> row per code — *produced by at least one test* × *rendered by at least one UI path* — using §5's measured
> seed as the starting gaps. Then write the missing Red tests for any uncovered cell (expected: `validation`,
> `storage-error`). Do **not** start filters/search (M2b) or any backend work (M3).

The receiver must complete the validation pass in
[`TASK-m2-persistence-seam.handoff-002.md`](TASK-m2-persistence-seam.handoff-002.md) before editing.
