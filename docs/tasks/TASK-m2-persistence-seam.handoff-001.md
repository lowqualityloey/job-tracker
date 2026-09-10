# Handoff Record 001 — TASK-m2-persistence-seam

- **Task ID**: `TASK-m2-persistence-seam` · **Specification**: `docs/specs/2026-09-10-spec-m2-persistence-seam.md`
- **Branch / audited revision**: `feat/m2-persistence-seam` @ `e5d0bbf` (31 commits over `main@63d769d`)
- **Which commit carries these records**: find it with `git log --grep='docs(m2): checkpoint 001' -1 --format='%h %s'` — this file cannot name its own hash,
  and any hash written here goes stale the moment the record is amended
- **Handed over at**: 2026-09-10 22:00 UTC · **From**: Assistant (same session, `pk:checkpoint`) · **To**: a fresh agent session, or this one
- **Checkpoint evidence**: [`TASK-m2-persistence-seam.checkpoint-001.md`](TASK-m2-persistence-seam.checkpoint-001.md)
- **Execution State at handoff**: `in_progress` — deliberately **not** `handoff_ready`. Nothing is blocked and
  no resume condition is unmet. This document exists so a context-window restart costs nothing; a new session
  must still perform §2 before editing, and this session may continue directly.
- **Release-evaluation handoff fragment**: N/A — no release, tag, or deployment is in progress. Nothing here
  authorises one; `pk:ship` and the human own that.

## 1. What the receiver is picking up

Client-side persistence for a React + TypeScript job tracker, built under Better-PromptKit Level 2 rules with
**TDD Enforcement Mode: enabled**. M2a.1–M2a.3 are complete and verified (validation, the repository seam, the
failure modes). What remains is the part the user can actually touch: a provider plus create/edit/delete UI
(M2a.4, AC-7…AC-10), then a contract sweep and the whole-branch gate (M2a.5, AC-11), then **PR #2**.

Nothing is creatable from the UI yet. `src/data/` can persist, but no component calls it.

## 2. Mandatory validation pass — complete before any edit

| # | Validate | How | If it mismatches |
| :--- | :--- | :--- | :--- |
| V1 | Task ID + revision | `git merge-base --is-ancestor e5d0bbf HEAD && [ -z "$(git status --porcelain)" ]` → exit 0; branch is `feat/m2-persistence-seam`. **Do not** compare `HEAD` for equality with `e5d0bbf`: the commit that added this file is a descendant of it, so an equality check is a guaranteed false mismatch | Stop; reconcile before editing |
| V2 | Tests are green as claimed | `npm run test:run` → **41 passed (41)**, exit 0 | Treat as `blocked`; diagnose with `pk:debug`, do not proceed |
| V3 | Typecheck clean | `npx tsc -p tsconfig.app.json --noEmit` → exit 0 | Stop |
| V4 | Acceptance ledger | Task Record §3: AC-1…AC-6 checked, AC-7…AC-11 open | Trust the **Task Record**, not this file or the chat |
| V5 | Invariants §3 of the checkpoint | Three **executed** commands. ① `grep -rln "localStorage\.\|\.setItem(\|\.getItem(" src --include='*.ts' --include='*.tsx' | grep -v '\.test\.' | grep -v '^src/data/'` → **0 files**. ② `grep -nE "JSON\.parse\([^)]*\) as |applications as JobApplication" src/data/localStorageApplicationRepository.ts` → **0 hits**. ③ `ls vite.config.js` → absent | Do not "fix" silently; raise it |
| V6 | No shadowing config reappeared | `ls vite.config.js` → absent (M1's `noEmit` guard). If present, **delete it and stop** — see `AGENTS.md` ⚠️ | Hard stop |
| V7 | One active task in scope | Task Record §5 Active Task Pointer = `TASK-m2-persistence-seam` | Another agent owns the scope; do not double-edit |

> **Why V5 is scoped the way it is.** Running my own handoff checks exposed three bad greps I had written
> first. A tree-wide `grep " as JobApplication"` hits a **comment** in the adapter explaining why that assertion
> was removed; `grep "JSON.parse(.* as " src` hits a **test** that decodes the raw envelope on purpose to assert
> on durable bytes; and a `--include='*.tsx'` search for `localStorage` hits a **doc comment** in
> `src/domain/applicationRepository.ts` ("async even though localStorage is synchronous"). All three are correct
> code, and each would have sent the next agent off to "fix" a non-problem. So ① matches actual storage-API
> usage while excluding tests and `src/data/`, and ② names the production file instead of the tree. Each command
> was executed against this revision before being written down — 0 files, 0 hits, absent. A verification step is
> only worth handing over once it has been run against the thing it is meant to catch.

## 4. Immediately next (exactly one action)

**Land Red for `BEHAVIOR-m2-persistence-seam-012`.** Create `src/state/applicationsProvider.test.tsx`:
render `<ApplicationsProvider repository={createInMemoryRepository(seedApplications)}>` and assert
`useApplications().applications` has length 5, then that `createApplication(input)` updates the exposed list.
Run it, confirm it fails, commit the test alone. Then Green, in the next commit.

Anchors worth reading first, in order: `docs/tasks/TASK-m2-persistence-seam.md` §3–§5 →
`docs/specs/...md` §3 (seams) and §5 (FMEA) → `src/domain/applicationRepository.ts` → `src/data/…`.

## 5. Guard rails for the remainder of this milestone

- **M2a.4 must add no dependency**, must not bundle the `DESIGN.md` §8 token refactor, and must not start
  filters/search (M2b) or any backend (M3).
- New UI must satisfy `AGENTS.md` Definition-of-Done: loading/empty/error/success states, ≥44 px targets,
  visible `:focus-visible` ring, AA contrast (`DESIGN.md` §6 — never darken the Interview/Offer/active-nav
  backgrounds; they have <0.4 headroom).
- Keep the AC-1…AC-6 ticked boxes honest: tick an AC only when the **whole** criterion is met. AC-5's notice
  half is still owed here.
- The workflow is human-gated: finish M2a → `pk:pr` → push branch + open PR #2 → **stop and report the URL**.
  Merging, pushing to `main`, tagging and deploying are never agent actions.

## 6. Where the truth lives

`AGENTS.md` (canonical rules) · `PROMPTKIT.md` (stack + 11 guardrails) · `DESIGN.md` (measured tokens) ·
`docs/STATE.md` (projection, milestones, 14-row debt register) · this Task Record (**authoritative** for
scope, ACs, state, TDD mode) · spec §5 (FMEA) · spec §4.1 (contracts).
