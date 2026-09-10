# Checkpoint Record 001 — TASK-m2-persistence-seam

- **Task ID**: `TASK-m2-persistence-seam` · **Canonical record**: [`TASK-m2-persistence-seam.md`](TASK-m2-persistence-seam.md)
- **Specification / Planning**: `docs/specs/2026-09-10-spec-m2-persistence-seam.md` (`PLAN-m2-persistence-seam`, Full, Level 2)
- **Checkpoint at**: 2026-09-10 22:00 UTC · **Trigger**: event-driven (milestone M2a.3 complete) + explicit `pk:checkpoint`
- **Execution State at checkpoint**: `in_progress` (**not** a stop state — implementation may resume)
- **Branch / audited revision**: `feat/m2-persistence-seam` @ `e5d0bbf`, 31 commits over `main` (`63d769d`)
- **Which commit carries these records**: not nameable here — a file cannot contain the hash of its own
  commit, and amending this one would invalidate any hash written into it (`284e7cb` was already stale that
  way within a minute). Find it with `git log --grep='docs(m2): checkpoint 001' -1 --format='%h %s'`. Validate **ancestry**, not equality.
  Handoff check V1 is written the same way
- **Release-evaluation handoff fragment**: N/A — no release candidate, tag, or QA gate exists for this work

## 1. Objective (unchanged)

A user can create, edit, and delete a job application and still see those changes after a page reload, with
every read and write passing through one `ApplicationRepository` interface.

## 2. Completed since the task opened

| Slice | Behaviours | Landed |
| :--- | :--- | :--- |
| M2a.1 domain validation | 001–003 | `fdffc2c`, `714c108`, `d35fe10`, `1f6aba2` |
| M2a.2 repository seam | 004–007 + aliasing hardening | `d82b2dc`, `43df0c4`, `6dbe8eb`, `9bacc0d`, `0724ad2`, `c67b04f` |
| M2a.3 failure modes | 008–011 | `4d72564`, `6b86025`, `a721971`, `892d56e` |

**Acceptance ledger: 6 of 11 met** (AC-1…AC-6) · **11 of 17 behaviours** · AC-5 met at the seam, its
*visible notice* half is explicitly carried into M2a.4 rather than claimed early.

New modules: `src/domain/validation.ts`, `src/domain/applicationRepository.ts`,
`src/data/localStorageApplicationRepository.ts`, `src/data/seedApplications.ts`
(replacing `src/data/mockApplications.ts`), tests `validation.test.ts` (11),
`localStorageApplicationRepository.test.ts` (11), `storageFaults.test.ts` (17).
Changed: `src/types/application.ts` (**`id: number → string`**, `createdAt` added), 3 pages, `AGENTS.md`
+ `.agents/rules/job-tracker-learning.md` (commit-discipline rules), `docs/STATE.md`, this record set.

## 3. Verification evidence at this revision

| Check | Result |
| :--- | :--- |
| `npm run test:run` | **41 passed (41)**, 4 files, exit 0 |
| `npx tsc -p tsconfig.app.json --noEmit` | exit 0 |
| Per-commit audit, all 31 commits in a **separate clone** | 26 typecheck clean · 3 failing = exactly the Red commits · **0 unexpected** |
| `npm run build` | exit 0 at M2a.1; re-run required at branch close (AC-11) |
| Hygiene | 0 debug/probe residue, 0 `.only`/`.skip`, 0 `.env` files, 0 build artefacts in tree |
| CI | **N/A — this repository has no CI (DEBT-03)**; the table above is the only evidence trail |

## 4. Decisions and invariants this checkpoint locks

1. `JobApplication.id` is a **string v4 UUID** assigned by the repository; seed ids are **fixed literals**.
2. The repository returns `Promise<Result<T, RepositoryError>>` and **never throws** across the seam.
   A stored/transport fault is data the UI can switch on, not an exception that blanks the page.
3. Availability is decided by a **write probe**, never by the existence of `window.localStorage`;
   `QuotaExceededError` over a non-empty store means *full* (recoverable), everything else means *off*.
4. A `schemaVersion` newer than `CURRENT_SCHEMA_VERSION` **fails closed**: refuse reads and writes, leave
   the bytes alone, and do **not** quarantine it — a newer file is not corruption.
5. Corrupt bytes are **copied to `job-tracker:applications:corrupt-<iso>` before** the live key is cleared.
   `quarantinedAs: string | null` where `null` = the copy itself failed, surfaced rather than swallowed.
6. Stored payloads are rebuilt **field by field**; no `as JobApplication[]` assertion. Unknown keys dropped.
7. Only `src/data/` touches `window.localStorage`; `src/domain/` stays pure; pages depend on the interface.
8. Storage is reached through `setItem`/`getItem`/`removeItem`, never property assignment.
9. **`src/domain/pipeline.ts` still does not exist** — the dashboard still derives counts in module scope
   (DEBT-05 open), and no UI can write yet, so **DEBT-01 stays open** despite persistence existing.

## 5. Blockers, risks, scope changes

- **Blockers**: none. Resume condition N/A (state is `in_progress`, not `checkpoint_due`).
- **Principal risk**: this branch exists **only on this machine** — `git ls-remote` shows no remote head.
  31 commits and 838+ lines of design documentation would be lost to a disk failure, and no CI would catch
  a regression. Mitigation available to the human: `git push -u origin feat/m2-persistence-seam`
  (a branch push, not a merge — outside what this checkpoint performs).
- **Scope changes**: **none**. No Scope Change Record and no TDD Exception Record exists or is needed —
  every behaviour in spec §6 is being built as planned; only the *order* of two commits' contents was
  corrected (see Task Record §6), never their scope.
- **Open items carried**: F-5 cross-tab last-write-wins (deferred to M2b by decision);
  UNCERTAINTY-001-1 (quota size — never asserted); UNCERTAINTY-002-1 (`randomUUID` needs a secure
  context — gated to M5 deployment).
- **Process debt raised by this session**: five commit-discipline rules now live in `AGENTS.md`
  §Commit discipline (mirrored to the Kilo file in `813e04b`) because no lint or CI exists to enforce them.

## 6. Exactly one prioritised next action

> **Land Red for `BEHAVIOR-m2-persistence-seam-012`** — render `ApplicationsProvider` and assert that
> `useApplications()` exposes the seeded list and that `create()` updates it, in
> `src/state/applicationsProvider.test.tsx`. That opens M2a.4 (CRUD UI: AC-7…AC-10). Do **not** start
> filters/search (M2b) or any backend work (M3).

Before editing, the receiver must complete the validation pass in
[`TASK-m2-persistence-seam.handoff-001.md`](TASK-m2-persistence-seam.handoff-001.md).
