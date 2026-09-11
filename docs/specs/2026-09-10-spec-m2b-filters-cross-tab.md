# M2b — Filtering, Search, and Cross-Tab Reconciliation

**Plan ID**: `PLAN-m2b-filters-cross-tab` · **Date**: 2026-09-10 (23:40 UTC, measured) · **Ceremony**: Level 2 (Controlled)
**Depth note**: written with a truncated agent context window. It is a real spec with decisions and
failure modes, not a Full-depth document; where it is thinner than M2a's spec that is disclosed here
rather than hidden in the diff. Task Record: `docs/tasks/TASK-m2b-filters-cross-tab.md`.

## 1. Problem and metric

After M2a the user has a growing list and no way to narrow it, and a second tab silently overwrites the
first: the provider holds a snapshot that nothing invalidates. **Metric**: a user with 30 applications can
reach the Interview ones in one action, and an edit made in tab B appears in already-open tab A without a
reload.

## 2. Goals / non-goals

**G1** status filtering and text search over the live snapshot. **G2** empty results distinguishable from
an empty account. **G3** external storage writes reconcile into a mounted UI. **G4** a write that failed
because the row vanished elsewhere leaves an honest UI. **G5** all of it stays inside M2a's seams.
**Non-goals**: URL/query-string sync; sorting UI (deferred until a sort behaviour demands `byNewest`);
pagination/virtualisation; fuzzy, accented, or stemmed matching; debounced input on a 30-record list;
operational transforms or merge conflict resolution; any backend or new dependency.

## 3. Design

Two pure modules, one subscription, one view-state owner:

| Module | Responsibility | Sealed decision |
| :--- | :--- | :--- |
| `domain/filters.ts` | `selectApplications(records, criteria): JobApplication[]`, `matchesQuery`, `normaliseQuery` | Pure and total: never throws on any criteria shape. M3 can mirror the same predicate server-side. |
| `state/applicationsProvider.tsx` | snapshot + `StorageEvent` subscription | Filter criteria are **not** stored here. |
| `components/ApplicationFilters.tsx` | search input + status chips | Owns criteria as local view state. |
| `pages/ApplicationsPage.tsx` | composition | `useMemo` over provider data + criteria. |

**Filter state lives in the view, not the provider.** The provider holds the canonical *data*; criteria are
what the user is currently looking at. Storing criteria beside the snapshot would make a filter change look
like a data change, and would leak a transient view choice into every consumer.

**Deletion test**: removing `domain/filters.ts` and inlining its predicate would scatter matching rules
across components and leave M3 without anything to mirror. The module earns its place.

## 4. Cross-tab contract (the interesting half)

`StorageEvent` fires **only in other tabs**, never in the one that wrote — so a test must dispatch the
event by hand, and no test can prove the writer's own view is correct (it already is).

On an event with `key === APPLICATIONS_STORAGE_KEY`, the provider calls **`repository.list()`**. It does
**not** parse `event.newValue`. That is the load-bearing choice: `newValue` is raw bytes, and decoding it
locally would bypass the envelope validation, the field-by-field rebuild, the quarantine path, and the
fail-closed version check — the four mechanisms M2a exists to provide. A cross-tab "optimisation" that
reads `newValue` would reintroduce every hazard the seam closed, and would be invisible in the happy path.

When `key === null` (storage cleared wholesale) the provider treats it as "the world changed, we cannot
tell how" and re-reads, rather than assuming empty.

**Reconciliation semantics**: last-write-wins, but *last-read-wins* for display — tab A's snapshot becomes
whatever the store holds after B's write. An in-flight A write can still clobber B. Accepted knowingly for
a single-user local tool; the honest safety net is that `update`/`remove` return `not-found` when the row
is gone, and the provider **must** reconcile its snapshot on that error instead of showing a row that no
longer exists.

**Mid-session schema upgrade**: if a newer build writes while this tab is open, the next read fails with
`unsupported-version`. The provider flips to `status: 'error'` and stops offering a stale view; writes then
refuse. Failing closed on a live tab is the same rule M2a applies at startup.

## 5. Failure modes (FMEA, M2b numbering)

| # | Mode | Effect | Mitigation |
| :--- | :--- | :--- | :--- |
| B-1 | Listener not removed on unmount | Leaked subscriptions; setState on dead tree | `useEffect` cleanup; test asserts detach |
| B-2 | Re-read storm from rapid events | Jank | One re-read per event; no debounce (record count makes it moot); documented |
| B-3 | Event for an unrelated key | Spurious reloads | Identity check on `APPLICATIONS_STORAGE_KEY`; tested |
| B-4 | `window.StorageEvent` absent/unsupported | Broken mount | Feature-guard the subscription; provider still works single-tab |
| B-5 | Query case/whitespace surprise | "It's not finding it" | `normaliseQuery` trims + lowercases; tested |
| B-6 | Zero matches rendered as "No job applications yet" | Claims the account is empty when the filter is wrong | Distinct empty state + one-action clear |
| B-7 | Criteria lost on navigation | User re-types after every View details | Accepted for M2b (local state); URL sync is the named non-goal |
| B-8 | Search exposes `notes` text | Same-origin only; no new leak | Notes are already rendered on-screen; no change |
| B-9 | Filter drops the record being edited | Details page says "not found" for existing data | Details lookup reads the provider snapshot, never filtered output |
| B-10 | Two tabs open with same seed | Duplicate `createdAt` ordering ties | Order is store order; no sort promised |

## 6. Behaviours

| Slice | ID | Behaviour |
| :--- | :--- | :--- |
| M2b.1 | 018 | A status chip narrows the list to matching records and announces the count |
| | 019 | A query matches across company, title, location **and** notes, case- and whitespace-insensitive |
| | 020 | Zero matches says "nothing matches this filter" with a clear action, not "no applications yet" |
| | 021 | Clearing restores the full list; the active chip is the only filter left |
| M2b.2 | 022 | A write made in another tab (dispatched event) appears in the already-mounted UI |
| | 023 | An event for a different storage key changes nothing |
| | 024 | `key: null` triggers a re-read rather than an assumption of emptiness |
| | 025 | A delete that returns `not-found` reconciles the snapshot instead of showing a ghost row |
| | 026 | A newer `schemaVersion` arriving mid-session flips the provider to error and refuses writes |

## 7. Definition of Done

`npm run test:run`, `npx tsc -p tsconfig.app.json --noEmit`, `npm run build` all clean and **no
`vite.config.js` emitted**; zero new packages; loading/empty/error/success states present; chips are real
buttons with `aria-pressed`; the search input sits in a `role="search"` region with an explicit label;
result count announced via a polite live region; 44 px targets and the §6 focus ring on every new control;
`pk:review`, then push and open PR #3, then **stop** for the human to merge.
