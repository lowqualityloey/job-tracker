# Handoff Record 002 — TASK-m2-persistence-seam

- **Task ID**: `TASK-m2-persistence-seam` · **Canonical record**: [`TASK-m2-persistence-seam.md`](TASK-m2-persistence-seam.md)
- **Specification**: `docs/specs/2026-09-10-spec-m2-persistence-seam.md` (`PLAN-m2-persistence-seam`, Full, Level 2)
- **Branch / audited revision**: `feat/m2-persistence-seam` @ `0c46b42` (44 commits over `main@63d769d`)
- **Handed over at**: 2026-09-10 22:58 UTC · **From**: Assistant (same session, `pk:checkpoint`) · **To**: a fresh session, or this one
- **Checkpoint evidence**: [`checkpoint-002`](TASK-m2-persistence-seam.checkpoint-002.md) · **Supersedes**: [`handoff-001`](TASK-m2-persistence-seam.handoff-001.md)
- **Execution State**: `in_progress`. Deliberately not `handoff_ready`: nothing is blocked, and no resume condition
  is unmet. This document exists so a context-window restart costs nothing. A new session still runs §2 before editing.
- **Release-evaluation handoff fragment**: N/A. Nothing here authorises a release; `pk:ship` and the human own that.

## 1. What the receiver is picking up

Client-side persistence for a React + TypeScript job tracker, under Better-PromptKit Level 2 with
**TDD Enforcement Mode enabled**. M2a.1–M2a.4 are **complete**: validation, the repository seam, the failure
modes, and the CRUD UI. All 17 behaviours exist and AC-1…AC-10 are met. A user can now create, edit, and delete
an application, and the repository writes to `localStorage`.

What is left is the closing gate, not the feature: **M2a.5** — an error-code contract sweep, then whole-branch
verification (AC-11), `pk:review`, and **PR #2**.

## 2. Mandatory validation pass — every command below was executed before being written here

| # | Validate | Command | Measured at `0c46b42` |
| :--- | :--- | :--- | :--- |
| V1 | Ancestry, not equality | `git merge-base --is-ancestor e5d0bbf HEAD && [ -z "$(git status --porcelain)" ]` | exit 0, clean |
| V2 | Branch and position | `git branch --show-current; git log --oneline 63d769d..HEAD \| wc -l` | `feat/m2-persistence-seam`, 44 |
| V3 | Suite is green as claimed | `npm run test:run` (bare `npm test` never exits) | **68 passed (68)**, 9 files |
| V4 | Typecheck | `npx tsc -p tsconfig.app.json --noEmit; echo $?` | exit **0** |
| V5 | AC ledger honesty | `grep -c '^- \[x\] \*\*AC-' docs/tasks/TASK-m2-persistence-seam.md`; same with `- \[ \]` | **10** met, **1** open (AC-11) |
| V6 | Storage layering holds | `grep -rln "localStorage\.\|\.setItem(\|\.getItem(" src --include='*.ts' --include='*.tsx' \| grep -v '\.test\.' \| grep -v '^src/data/'` | **0** files |
| V7 | No cast on parsed storage | `grep -nE "JSON\.parse\([^)]*\) as \|applications as JobApplication" src/data/localStorageApplicationRepository.ts` | **0** hits |
| V8 | No numeric-id regression | `grep -rn "Number(id)" src \| wc -l` | **0** |
| V9 | M1's shadowing hazard still shut | `ls vite.config.js` · `grep -c noEmit tsconfig.node.json` | absent · **1** |
| V10 | Debt table is countable | `grep -cE '^\| DEBT-[0-9]+ \| — ' docs/STATE.md` · `grep -cE '^\| DEBT-[0-9]+ ' docs/STATE.md` | **3** closed of **14** rows |

If any row mismatches: **stop and reconcile before editing**. Do not "fix" a check to make it pass — three
checks in `handoff-001` were themselves the defect (they matched comments and tests, not code), and one
demanded `HEAD` equal a commit that predates the file making the demand.

## 3. Locked invariants (do not undo)

1. `JobApplication.id` is a **string v4 UUID** assigned by the repository; seed ids are fixed literals.
2. The repository returns `Promise<Result<T, RepositoryError>>` and **never throws** across the seam.
3. Availability = a **write probe**; `typeof localStorage !== 'undefined'` is banned; *full* ≠ *off*.
4. A newer `schemaVersion` **fails closed**: refuse, preserve bytes, never quarantine.
5. Corrupt payloads are copied to `…:corrupt-<iso>` **before** the live key is cleared; `quarantinedAs: null` is reported.
6. Stored records are rebuilt **field by field**; no `as` assertion on parsed JSON.
7. `src/data/` is the only layer that touches storage; `src/domain/` is pure; pages import the **interface**.
8. `data/applicationStore.ts` is the composition root. The provider's repository arrives **as a prop**, built once
   via lazy `useState` — the load effect depends on its identity.
9. The snapshot changes **only after** the repository resolves ok. No optimistic create, update, or delete.
10. Loading is never rendered as "not found"; an error is never rendered as an empty list or as zeros.
11. UI adds **zero packages**; clicks in tests use `fireEvent` because `user-event` is not installed (D-6).
12. New controls keep 44 px targets and the §6 focus ring; the 12 px field radius is **unratified** — do not
    propagate it without updating `DESIGN.md` §4.
13. `npm test` is watch mode and never exits; never remove `"noEmit": true` from `tsconfig.node.json`.
14. Red, Green, Refactor are separate commits; gate every Green commit on the suite (`AGENTS.md` §Commit discipline).

## 4. Immediately next — exactly one action

> **Build the error-code contract matrix** (M2a.5, first step). One row per `RepositoryError` code —
> *produced by ≥1 test* × *rendered by ≥1 UI path*. Measured seed of the gaps: `validation` (tests 0, ui 0) and
> `storage-error` (tests 1, ui 0) have **no distinct rendering**, so they fall through to a generic sentence.
> Write the missing Red tests for each uncovered cell, then re-measure.

Also still owed for AC-11: a **manual reload check in a real browser** (`npm run dev`, create, reload, confirm
persistence and that the notice does not appear). Nothing proves that yet — every persistence claim so far is
jsdom-backed, and that distinction is the honest residual on DEBT-01's closure.

Then, in order: `pk:review` → `pk:pr` → push `feat/m2-persistence-seam`, open **PR #2**, report the URL, **stop**.

## 5. Guard rails

- **Out of scope**: filters/search (M2b), any backend/HTTP/auth (M3), new dependencies, the `DESIGN.md` §8 token
  extraction, F-5 cross-tab reconciliation (M2b), an `updatedAt` field (deliberately absent).
- **Tidy while there**: the debt register marks closures two different ways — DEBT-01/05/07 blank the severity
  column, DEBT-11…14 say FIXED in prose and still read `P1`/`P2`. Normalise it; do not quote an aggregate
  closure count until the table supports one.
- **Who does what**: the agent creates the branch, publishes it (`git push -u origin feat/…`), pushes commits,
  and opens the PR — without being asked, and a milestone is not reported complete until the PR URL exists. The
  human reviews and merges; `main` pushes, tags and deploys stay theirs. Earlier drafts of these records called
  the branch push "the human's call" — that was wrong and is corrected in `AGENTS.md`.
- **Interview-explainability**: this is a learning repository. Explain the *why* in the PR body, surface the
  trade-off (Result vs throw, write-probe vs feature detect, confirm-vs-`window.confirm`, fireEvent vs
  user-event), and propose one follow-up practice task.

## 6. Where the truth lives

`AGENTS.md` (canonical rules, incl. §Commit discipline) · `PROMPTKIT.md` (stack + guardrails) · `DESIGN.md`
(measured tokens; §5/§6 are Definition-of-Done) · `docs/STATE.md` (projection; §8 continuity) · **this Task
Record** (authoritative for scope, ACs, state, TDD mode) · spec §5 (FMEA) · spec §4.1 (contracts) ·
`checkpoint-002` (evidence and invariants) · `docs/tasks/…checkpoint-001.md` and `handoff-001.md` (history;
§4 of 001 still stands, §6 of 001 is superseded).
