# Technical Design Document (RFC): M2a — Persistence Seam & Persisted CRUD

**Status:** Proposed · **Author:** Assistant (`pk:plan`) · **Date:** 2026-09-10 · **Revision:** 1
**Route:** Level 2 (Controlled Work) · **Planning depth:** Full (mandatory trigger: persistent data + multiple behavioural components)
**Task Record:** [`docs/tasks/TASK-m2-persistence-seam.md`](../tasks/TASK-m2-persistence-seam.md)

---

## Planning Record (PromptKit Adaptation)

### Planning Record Metadata

- **Planning ID**: `PLAN-m2-persistence-seam`
- **Requested by**: Lead Engineer (human) — "proceed next implementation" after PR #1 merge
- **Owner**: Lead Engineer · **Planner**: Assistant (`pk:plan`)
- **Classification**: Level 2 Controlled · **Depth**: Full
- **Linked spec**: this document · **Linked task record**: `TASK-m2-persistence-seam`

### Planning Inputs

| Input | Value |
| :--- | :--- |
| Requested outcome | The app can create, edit, and delete job applications, and those changes are still there after a page reload |
| Observable completion condition | `npm run test:run` green incl. a create→reload→read-back round-trip test; manual: add an application, press F5, it is still listed |
| Scope boundary | Client-side persistence + CRUD + the `src/domain` extraction it forces. **Not** filters/search (M2b), **not** a backend (M3) |
| Affected behavioural components | types, domain validation, repository seam, seed data, app state provider, 3 pages, new form component, route table |
| Externally visible contracts | `JobApplication.id` changes `number → string`; storage key format becomes a versioned contract |
| Verification approach | TDD (Red → Green → Refactor) per behaviour, Task-Record-enforced; typecheck + build |

### Assumption Records

- **ASSUMPTION-m2-persistence-seam-001** — *Single user, single browser, no concurrency requirement beyond
  two open tabs of the same origin.* Owner: Lead Engineer. Impact: removes the need for server authority or
  merge resolution in M2a. Validation: revisit in M3 when an API introduces other devices. Status: accepted.
- **ASSUMPTION-m2-persistence-seam-002** — *Losing all stored applications on this device is acceptable
  damage (demo data), but silently corrupting or clobbering them is not.* Owner: Lead Engineer. Impact: drives
  the fail-closed rules in §5. Validation: none needed; it is a product stance. Status: accepted.

### Technology and Vendor Decision Records

No new dependency is introduced by this spec — `crypto.randomUUID`, `localStorage`, `JSON`, and
`structuredClone` are all existing platform APIs, and the project adds **zero** runtime packages in M2a.
Two decisions are nevertheless material (they determine compatibility and failure behaviour), so they are
recorded with sources.

#### DECISION-m2-persistence-seam-001 — Use `window.localStorage` behind an async repository interface

- **Options considered**: (a) localStorage behind an async `Promise`-returning repository · (b) IndexedDB via
  the `idb` library · (c) defer to the ASP.NET Core API (M3)
- **Selected**: (a) — approved by the Lead Engineer on 2026-09-10
- **Rejected**: (b) adds a dependency and a second data model for a 5-record demo, and is discarded wholesale
  when M3 lands; (c) merges two milestones and cannot be reviewed as one PR
- **Rationale**: the interface is the durable decision; the medium is not. Because the repository already
  returns Promises, the M3 swap to HTTP replaces one file and no UI code
- **Owner / status**: Lead Engineer / approved

| ID | Material claim | Disposition |
| :--- | :--- | :--- |
| CLAIM-001-1 | `localStorage` persists across browser sessions and page loads, unlike `sessionStorage` | **Verified** → CITATION-001-1 |
| CLAIM-001-2 | Keys and values are always strings (UTF-16), so domain records must round-trip through `JSON` | **Verified** → CITATION-001-1, CITATION-001-2 |
| CLAIM-001-3 | Storage is scoped to the **protocol + origin**: `http://localhost:5173` and `https://localhost:5173`, and `localhost` vs `127.0.0.1`, are **different storage areas** | **Verified** → CITATION-001-1 ("specific to the protocol of the document") |
| CLAIM-001-4 | Merely testing that `window.localStorage` exists is **insufficient** feature detection — browsers can disable the API while leaving the global present, and private-mode browsers may hand back an object with a **zero quota** | **Verified** → CITATION-001-2 |
| CLAIM-001-5 | `setItem` can throw `SecurityError` (invalid origin scheme, or a policy decision such as blocked cookies) and `QuotaExceededError`; the two must be distinguished from plain unavailability | **Verified** → CITATION-001-1, CITATION-001-2 |
| CLAIM-001-6 | Use the `setItem`/`getItem`/`removeItem`/`key`/`length` API rather than direct property assignment, to avoid collisions with built-ins (`.clear`, `.getItem`), data leaks through prototype inheritance, and prototype-pollution when handling untrusted input | **Verified** → CITATION-001-2 |
| CLAIM-001-7 | A `StorageEvent` is fired to *other* tabs of the same origin when storage changes — i.e. concurrent-tab writes are a real scenario | **Verified** → CITATION-001-2 (event output example) |

- **CITATION-001-1** — Publisher: MDN (Mozilla). Title: *"Window: localStorage property — Web APIs"*.
  URL: <https://developer.mozilla.org/en-US/docs/Web/API/Window/localStorage>. Accessed: 2026-09-10.
  Supports: CLAIM-001-1, -2, -3, -5.
- **CITATION-001-2** — Publisher: MDN (Mozilla). Title: *"Using the Web Storage API — Web APIs"*.
  URL: <https://developer.mozilla.org/en-US/docs/Web/API/Web_Storage_API/Using_the_Web_Storage_API>.
  Accessed: 2026-09-10. Supports: CLAIM-001-2, -4, -5, -6, -7.
- **Normative upstream**: WHATWG HTML, *Web storage* — <https://html.spec.whatwg.org/multipage/webstorage.html>
  (cited by MDN as the specification for `dom-localstorage-dev`).

| ID | Uncertainty | Disposition |
| :--- | :--- | :--- |
| UNCERTAINTY-001-1 | **The per-origin storage quota is not stated by the primary sources I read.** The commonly quoted "~5 MB" is folklore, not a normative figure, and it varies by browser. Design consequence: the code must be **quota-agnostic** — handle `QuotaExceededError` as a condition, never as an impossible case | **Accepted assumption** (owner: Lead Engineer). No action needed now; measure in a browser during M5 via `pk:perf` if it ever matters |
| UNCERTAINTY-001-2 | Whether `localStorage` writes are slow enough at realistic data sizes to jank the main thread. The methods are synchronous by interface (they return `void`, not a `Promise`), but **jsdom timings are not browser timings**, so measuring in Vitest would produce a misleading number | **Deferred.** Claim no measurement. Revisit only if record counts grow large enough to matter |

#### DECISION-m2-persistence-seam-002 — Generate `id: string` with `crypto.randomUUID()` inside the repository

- **Options**: (a) `crypto.randomUUID()` · (b) hand-rolled `Math.random()` uuid · (c) keep `id: number`
- **Selected**: (a), approved by the Lead Engineer on 2026-09-10. (b) is rejected as not
  collision-safe and not cryptographically secure; (c) rejected because a backend will hand back string ids
- **Owner / status**: Lead Engineer / approved

| ID | Material claim | Disposition |
| :--- | :--- | :--- |
| CLAIM-002-1 | `crypto.randomUUID()` returns a 36-character **v4** UUID string generated by a cryptographically secure PRNG | **Verified** → CITATION-002-1 |
| CLAIM-002-2 | It is **Baseline "Widely available"**, shipped across browsers since March 2022, and available in Web Workers | **Verified** → CITATION-002-1 |
| CLAIM-002-3 | **Secure-context only** — available only over HTTPS (and, in practice, `localhost`) in some or all supporting browsers | **Verified** → CITATION-002-1 |
| CLAIM-002-4 | In *this project's test environment* (Vitest 2.1.9, `environment: 'jsdom'`), `crypto.randomUUID` is a working function and returns valid v4 strings | **Verified by direct measurement**, not documentation — probe run 2026-09-10: `crypto_randomUUID: "function"`, samples `7d4ca9fa-fa69-4899-9e05-2799061cc294`, `b59c566a-8f2c-44a3-8c79-611668a8ecf9`. Probe file deleted, never committed |
| CLAIM-002-5 | In the same jsdom environment `localStorage` exists, `setItem`/`getItem`/`removeItem` round-trip, `length` starts at 0, and `structuredClone` is available | **Verified by the same probe run**. Consequence: the *real* `LocalStorageApplicationRepository` can be tested directly in Vitest with a `beforeEach` clear, instead of only testing a fake |

- **CITATION-002-1** — Publisher: MDN (Mozilla). Title: *"Crypto: randomUUID() method — Web APIs"*.
  URL: <https://developer.mozilla.org/en-US/docs/Web/API/Crypto/randomUUID>. Accessed: 2026-09-10.
  Supports: CLAIM-002-1, -2, -3.
- **UNCERTAINTY-002-1** — CLAIM-002-3 means a deployment served over plain HTTP has **no** `randomUUID`.
  Impact: creating an application would throw at the id-assignment step. Mitigation is behavioural, not
  speculative: the repository wraps id assignment so a failure surfaces as a typed `RepositoryError` rather
  than an unhandled exception. **No fallback UUID generator is being written in M2a** (that would be
  untested, unnecessary code); if M5 deploys over HTTP, revisit here. Owner: Lead Engineer. Status: open,
  gated to M5.

---

## 1. Executive Summary & Problem Statement

Today the application is **read-only theatre**: three pages render a hardcoded array
(`src/data/mockApplications.ts`), and `JobApplication.id` is `number`. There is no way to add, change, or
remove an application, and any change would vanish on reload anyway. That blocks every subsequent milestone —
filters and search are pointless over a fixed 5-row fixture, and the M3 API needs a call site to replace.

**Why now:** the seam is cheapest to introduce *before* the UI grows more surfaces, and before any real user
data exists to migrate. The `number → string` id decision is free today and expensive the moment a user has
saved records with numeric ids.

**Success metric (observable, not aspirational):** a user can add an application, press F5, and still see it.
Plus: `npm run test:run` covers the round-trip, and no page imports a storage module directly.

---

## 2. Goals and Explicit Non-Goals

### Goals (In Scope)

1. **G1 — Validation domain module**: pure, synchronous, dependency-free rules for a new/edited application.
2. **G2 — Repository seam**: an `ApplicationRepository` interface returning `Promise<Result<T, E>>`, with a
   `LocalStorageApplicationRepository` implementation and an `InMemoryApplicationRepository` double.
3. **G3 — Versioned storage envelope** (`{ schemaVersion, applications }`) under a namespaced key, with
   fail-closed handling for corrupt, unavailable, over-quota, and newer-version states.
4. **G4 — Id contract change**: `JobApplication.id: string` (v4 UUID), assigned by the repository; seed
   records carry **fixed literal** UUIDs so tests stay deterministic.
5. **G5 — App state provider**: one React context owning the repository and an in-memory snapshot; pages and
   components consume it through `useApplications()`.
6. **G6 — CRUD surfaces**: create form, edit (same form), delete with confirmation, and detail/list/dashboard
   reading from the provider. Fixes DEBT-05 (dashboard derivations in UI), DEBT-06 (no seam), DEBT-07
   (unvalidated `Number(id)`).

### Non-Goals (Explicit Scope Boundary)

- ❌ Search, status filters, sorting UI (M2b) — deliberately excluded to keep this PR reviewable
- ❌ Any backend, HTTP client, API contract, or auth (M3, M4)
- ❌ IndexedDB, and any new runtime dependency (**this spec adds zero packages**)
- ❌ Multi-device sync, cross-tab merge, or `StorageEvent` reconciliation — the failure mode is *documented*
  in §5 and consciously deferred
- ❌ Notes rich-text, attachments, company logos, salary fields, reminder dates
- ❌ CSS token extraction and the rest of `DESIGN.md` §8 (independent concern, own PR)
- ❌ Tests for M2b/M3 features, and any release/tag/deploy action
- ❌ Rewriting the 5 seed records into "real" data or user-provided content

---

## 3. Architecture & System Context

### High-Level Flow

```text
        ┌────────────────── UI (src/pages, src/components) ──────────────────┐
        │  DashboardPage · ApplicationsPage · ApplicationDetailsPage          │
        │  ApplicationsProvider ── useApplications() ── ApplicationForm       │
        └───────────────┬────────────────────────────────────────────────────┘
                        │ depends only on the INTERFACE + Result types
                        ▼
        ┌───────────────────────── src/domain ───────────────────────────────┐
        │  applicationRepository.ts  (interface, Result, RepositoryError)     │
        │  validation.ts             (pure rules: names, dates, status)       │
        │  pipeline.ts               (pure derivations: counts, ordering)     │
        └───────────────┬────────────────────────────────────────────────────┘
                        │ implemented by
                        ▼
        ┌────────────────────────── src/data ────────────────────────────────┐
        │  localStorageApplicationRepository.ts   ← the ONLY module touching  │
        │  inMemoryApplicationRepository.ts        window.localStorage        │
        │  seedApplications.ts                     (fixed-uuid demo records)  │
        └────────────────────────────────────────────────────────────────────┘
                        ▼
                 window.localStorage  ·  origin-scoped, string-only, quota-limited
```

### Deep Module Decomposition & Seams

| Module | Interface (what callers see) | Depth justification |
| :--- | :--- | :--- |
| `domain/validation` | `validateApplication(input) → Result<ValidatedInput, FieldError[]>` | Every rule (required, length, calendar-date, status membership) lives here; UI never re-implements a check |
| `domain/pipeline` | `summarise(applications) → { total, inInterview, offers, … }`, `byNewest(list)` | Replaces the module-scope `filter()` passes in `DashboardPage`; pure, trivially testable |
| `domain/applicationRepository` | the `ApplicationRepository` interface + `Result`/`RepositoryError` types | **The** seam. The M3 API swap replaces one implementation and imports nothing else |
| `data/localStorage…` | `createLocalStorageRepository(overrides?) → ApplicationRepository` | Hides JSON encoding, envelope/version checks, the availability probe, `DOMException` name discrimination, and the corrupt-data quarantine copy |

**Deletion test.** Deleting `domain/validation` does not move boilerplate — it *scatters* four distinct rules
into form components, the repository, and the details page. Deleting the repository interface would push
`window.localStorage` and `JSON.parse` into 4+ components, which is exactly DEBT-06. Both pass.
`inMemoryApplicationRepository` earns its place as the mandated test double and the unavailable-storage
fallback — it is used by both, so it is not a speculative second implementation.

**The interface is the test surface.** Behaviours are asserted through
`repository.list()/create()/update()/remove()` and through rendered UI text — not by poking at private
helpers. No network or filesystem mocking is required: the jsdom probe (CLAIM-002-5) shows the *real*
localStorage adapter can be exercised directly.

**State ownership.** `ApplicationsProvider` owns the single in-memory snapshot; `localStorage` is the durable
store of record. Writes go repository → storage → new snapshot; there is exactly one writer per tab. Reads are
served from the snapshot so rendering never awaits.

---

## 4. Detailed Design & Contracts First

### 4.1 Data Models & Schemas

```ts
// src/types/application.ts  — the public contract
export type ApplicationStatus = 'Saved' | 'Applied' | 'Interview' | 'Rejected' | 'Offer'

export interface JobApplication {
  id: string          // CHANGES number → string. v4 UUID (36 chars), assigned by the repository
  companyName: string // required, trimmed, 1–120
  jobTitle: string    // required, trimmed, 1–120
  location: string    // required, trimmed, 1–120
  status: ApplicationStatus
  appliedAt?: string  // optional; date-only ISO 8601 'YYYY-MM-DD', must be a real calendar date
  notes?: string      // optional, 0–2000
  createdAt: string   // ISO 8601 timestamp; drives newest-first ordering (G6)
}

export type NewApplicationInput = Omit<JobApplication, 'id' | 'createdAt'>
export type ApplicationPatch = Partial<NewApplicationInput>
```

**Why `string` ids are safe to change now:** the only consumers are `key={application.id}`, the
`/applications/:id` link, and `useParams()`. `Number(id)` disappears along with DEBT-07.

**Storage envelope** (a contract in its own right — it is what survives to a user's disk):

```ts
// key: 'job-tracker:applications'   (namespaced; never a bare word)
interface ApplicationsEnvelopeV1 {
  schemaVersion: 1
  applications: JobApplication[]
}
```

```ts
// src/domain/applicationRepository.ts — error + result contract
export type RepositoryError =
  | { code: 'validation';  fieldErrors: FieldError[] }
  | { code: 'not-found';   id: string }
  | { code: 'unavailable' }                       // storage disabled/quota-zero (CLAIM-001-4)
  | { code: 'quota-exceeded' }                     // CLAIM-001-5
  | { code: 'corrupt-data'; quarantinedAs: string | null } // raw copy preserved; null = copy also failed
  | { code: 'unsupported-version'; found: number } // newer schemaVersion on disk — fail closed
  | { code: 'storage-error'; detail: string }

export type Result<T, E = RepositoryError> = { ok: true; value: T } | { ok: false; error: E }

export interface ApplicationRepository {
  list(): Promise<Result<JobApplication[]>>
  get(id: string): Promise<Result<JobApplication>>
  create(input: NewApplicationInput): Promise<Result<JobApplication>>
  update(id: string, patch: ApplicationPatch): Promise<Result<JobApplication>>
  remove(id: string): Promise<Result<{ id: string }>>
}
```

**Design note:** the interface is `async` although localStorage is not. That is deliberate — the shape of the
seam is chosen for what *replaces* it (M3's HTTP calls), not for what it wraps today. Callers must `await`
from day one, so the M3 swap changes no call sites.

### 4.2 Zero-Downtime Migration Plan (Expand-Contract)

No database exists, so there is no DDL and no `DROP`. The equivalent risk is **clobbering a returning user's
stored data**, which is what `schemaVersion` exists to prevent. There is no old reader to migrate away from
(storage key is new in M2a), so only the Expand phase applies:

| Stage | M2a content | Rollback |
| :--- | :--- | :--- |
| **Expand** | Write `{ schemaVersion: 1, applications }` under a **new** key. First run seeds from `seedApplications.ts` with fixed literal UUIDs. Old `mockApplications.ts` is deleted, but no stored data is read by the old path, so nothing is orphaned | Delete the key; nothing else references it |
| **Backfill** | N/A — no pre-existing persisted records exist today | — |
| **Contract** | Deferred to a future version bump: when v2 lands, read v1 → normalise to v2 in memory → write v2, and **never** delete the v1 key in the same release | Ship v2 reader, restore v1 writer |

**Fail-closed rules (these exist to make data loss impossible):**
1. Envelope with `schemaVersion > 1` → return `unsupported-version`, **write nothing**, keep the raw value.
2. Value that is not valid JSON, or not an object with the expected shape → copy the raw string to
   `job-tracker:applications:corrupt-<isoTimestamp>`, then seed fresh, and surface `corrupt-data`. Never
   discard a byte the user typed without leaving it somewhere retrievable.
3. Availability probe fails (§5, F-1) → operate on the in-memory repository for the session, surface
   `unavailable`, and **never** claim success as if it persisted.

### 4.3 API Endpoints & Contracts

N/A — no HTTP surface in M2a. The equivalent artefact is the TypeScript `ApplicationRepository` contract in
§4.1, which is the file M3 will implement against over HTTP. `pk:api` owns the eventual envelope; M2a's
`Result<T, RepositoryError>` shape is intentionally compatible with a future
`{ ok, data?, error? }` response envelope so the two do not need reconciling later.

---

## 5. Security, Privacy & Failure Modes (FMEA)

### Security & Multi-Tenancy Audit

- **Multi-tenancy**: N/A. Single user, single origin (PROMPTKIT.md §1). No isolation control is needed and
  none should be invented.
- **Where untrusted input enters**: the create/edit form, and — importantly — **`localStorage` itself**. A
  tampered or stale payload is attacker-influenced data entering the app's trust boundary, which is why every
  read is shape- and type-validated rather than trusted because "we wrote it".
- **Prototype-pollution / built-in collision**: use `setItem`/`getItem`/`removeItem` and never
  `localStorage[key] = value` (CLAIM-001-6). Records are parsed with an explicit field-by-field mapper, not a
  bare `JSON.parse(...) as JobApplication[]`.
- **PII**: employer names, job titles, and free-text notes are personal data the user chooses to store. It
  stays on-device in one origin's localStorage — no transmission, no third party, no logging. Notes are never
  rendered inside `dangerouslySetInnerHTML`; React's text escaping is the whole story here, and must stay so.
- **Storage is plaintext and readable by any script on the same origin.** Accepted for demo data, but stated
  explicitly: **never** put a credential, token, or real account password in this store, and M4's auth tokens
  must not land here — `pk:auth` will need to reject localStorage for session tokens on exactly this reason.
- **DoS / quota**: bounded by validating lengths at the domain layer before any write.

### FMEA Resilience Matrix

| ID | Failure scenario | Prob. / Sev. | Detection | Mitigation / fallback | Recovery |
| :--- | :--- | :--- | :--- | :--- | :--- |
| F-1 | Storage unsupported or **policy-disabled** (cookie blocked, zero quota in private mode) — and the global object still *exists* (CLAIM-001-4) | Low / High | `storageAvailable()` write-probe at repository construction: `setItem`/`removeItem` of a sentinel inside `try` | Swap to `InMemoryApplicationRepository` for the session; surface `unavailable`; render a persistent non-blocking banner "Changes won't be saved on this device" | None needed; next session re-probes. Data entered in-session is lost — the banner is the honest disclosure |
| F-2 | `QuotaExceededError` on write (CLAIM-001-5); note quota is *not* specified — UNCERTAINTY-001-1 | Low / Medium | `catch` discriminated by `e instanceof DOMException && e.name === 'QuotaExceededError'` | Return `quota-exceeded`; keep the in-memory snapshot **unchanged** (no half-applied write); form keeps its input and shows the error | User removes records; retry. Never a silent no-op |
| F-3 | Stored JSON corrupt, hand-edited, or from a future build | Medium / High | Envelope shape + per-record validation on read | Quarantine raw value under `…:corrupt-<ts>` then seed (rule 2, §4.2); return `corrupt-data` | Raw bytes remain retrievable in the same store |
| F-4 | `schemaVersion` newer than this build (user downgrades the app) | Low / High | Explicit version comparison | `unsupported-version`; refuse to write — **fail closed, do not downgrade in place** | Upgrade back; key untouched |
| F-5 | **Second tab writes the same key** (CLAIM-001-7: `StorageEvent` fires cross-tab) | Medium / Low | Not detected in M2a | **Accepted, documented**: last write wins per whole-envelope; no merge. Deferred to M2b — and M2a deliberately does *not* add an `updatedAt` field to fake a capability it lacks | User reloads the tab; sees the other tab's state |
| F-6 | `crypto.randomUUID` missing (non-secure context; CLAIM-002-3 / UNCERTAINTY-002-1) | Low / High | Type of the function at id-assignment time | `storage-error` (detail `id-generation-unavailable`) surfaced on the form; no record written | Serve over HTTPS/localhost. No speculative fallback generator in M2a |
| F-7 | Double-submit (user hits save twice, or Enter + click) | Medium / Low | Two `create()` calls race | Snapshot is written from the repository's returned record, so a duplicate surfaces as **two records**, not corruption. Mitigation = disable the submit button while pending. Client idempotency keys are **not** built (YAGNI at single-user scale); revisit with the API where server-side uniqueness exists | User deletes the duplicate |
| F-8 | Invalid or hostile form input (blank, over-length, `2026-02-30`, `<script>`, unbounded notes) | High / Medium | `domain/validation` before the repository is ever called | Reject with per-field errors; trim; enforce caps; reject non-existent calendar dates rather than letting `Date` roll them over | User corrects; input preserved in the form |
| F-9 | Unknown `/applications/:id` (typo, deleted record, stale bookmark) | Medium / Low | `get()` → `not-found` | Dedicated not-found branch with a link back (replaces the `Number(id)` NaN path, DEBT-07) | Navigation |
| F-10 | Provider still loading while a page renders | Medium / Low | `status === 'loading'` in `useApplications()` | Render the existing `EmptyState` skeleton — and per AGENTS.md, a loading state is a **required** state, not an afterthought | Resolves on first `list()` |

---

## 6. Implementation Milestones (Task Record TDD Mode: **enabled**)

The Task Record sets `TDD Enforcement Mode: enabled`, so each behaviour runs **Red → Green → Refactor** with a
stable `BEHAVIOR-` identity. One behaviour per Red commit; no test is written after its implementation.

| # | Behaviour | Red (failing assertion) | Green (smallest change) | Refactor / hardening |
| :--- | :--- | :--- | :--- | :--- |
| **M2a.1** | `BEHAVIOR-m2-persistence-seam-001` — validation accepts a valid application | `validateApplication({...})` → `ok: true` | `domain/validation.ts` field rules | Shared bounds as named constants |
| | `BEHAVIOR-m2-persistence-seam-002` — rejects blank/over-length `companyName` | expected `fieldErrors` contains `companyName` | required + max-length rules | Collapse duplicated rules |
| | `BEHAVIOR-m2-persistence-seam-003` — rejects a non-existent calendar date | `'2026-02-30'` → error on `appliedAt` | real-date check (no `Date` rollover) | Extract `isIsoDateOnly` |
| **M2a.2** | `BEHAVIOR-m2-persistence-seam-004` — `create()` assigns a v4 string id + `createdAt` | `/^[0-9a-f]{8}-/` matches, `typeof id === 'string'` | localStorage repository | Envelope write helper |
| | `BEHAVIOR-m2-persistence-seam-005` — **round-trip**: a second repository instance sees the record | `repoB.list()` contains repoA's record | read path + envelope parse | Single `decodeEnvelope` |
| | `BEHAVIOR-m2-persistence-seam-006` — `update()` preserves `id` and `createdAt` | unchanged after patch | merge-at-the-edge rules | — |
| | `BEHAVIOR-m2-persistence-seam-007` — `remove()` then `get()` → `not-found` | `error.code === 'not-found'` | delete path | — |
| **M2a.3** | `BEHAVIOR-m2-persistence-seam-008` — unavailable storage falls back + reports `unavailable` | injected throwing storage → `ok:false`, code `unavailable` | availability probe + in-memory swap | Name the sentinel key once |
| | `BEHAVIOR-m2-persistence-seam-009` — quota error returns `quota-exceeded`, list unchanged | injected `QuotaExceededError` → snapshot intact | `DOMException.name` discrimination | Error-mapping table |
| | `BEHAVIOR-m2-persistence-seam-010` — corrupt payload is quarantined, never silently discarded | raw value still readable under a `…:corrupt-` key | quarantine-then-seed | Timestamp format |
| | `BEHAVIOR-m2-persistence-seam-011` — newer `schemaVersion` refuses to write | after a failed `create`, the stored value is byte-identical | version gate before writes | — |
| **M2a.4** | `BEHAVIOR-m2-persistence-seam-012` — provider exposes list + create to the UI | render `<Provider>` → `useApplications().applications` has the seed | `ApplicationsProvider` + hook | Split the reducer out if the file grows |
| | `BEHAVIOR-m2-persistence-seam-013` — submitting the form adds a visible row | `fireEvent.submit` → company name in the document | `ApplicationForm` + route `/applications/new` | Label/`htmlFor` pairing, error announcements |
| | `BEHAVIOR-m2-persistence-seam-014` — invalid submit shows field errors and saves nothing | `getAllByRole('alert')` + repository still 5 records | wire validation → form state | — |
| | `BEHAVIOR-m2-persistence-seam-015` — dashboard counts derive from the provider (DEBT-05) | counts change after a `create()` in the same test | `pipeline.summarise` through the provider | delete module-scope `filter()`s |
| | `BEHAVIOR-m2-persistence-seam-016` — unknown id renders the not-found branch (DEBT-07) | `render` at `/applications/nope` → "not found" text | string-id lookup + branch | remove `Number()` |
| | `BEHAVIOR-m2-persistence-seam-017` — delete removes the row and confirms first | click delete → confirm → row gone | confirm dialog + `remove()` | focus return, a11y naming |
| **M2a.5** | Contract sweep + hardening | `StatusBadge.test.tsx` and any id-typed fixture updated for `string` ids | typecheck green | Full `test:run`, build, `pk:review` pass |

**Presentation note (not a TDD behaviour):** new controls must satisfy `DESIGN.md` §5/§6 — ≥44 px targets,
`:focus-visible` ring, AA contrast — because `AGENTS.md` makes a11y a Definition-of-Done item. No colour or
token refactor is bundled here.

---

## 7. Sign-off & Grilling Checklist

- [x] Problem, measurable success metric, and non-goals written down (§1, §2)
- [x] Deep modules + seams drawn; deletion test applied to all four (§3)
- [x] Contracts before implementation: domain types, envelope, `Result`, repository interface (§4.1)
- [x] Migration/rollback plan for the durable artefact that exists (the storage envelope) (§4.2)
- [x] FMEA with 10 named failure modes, each with detection + mitigation + recovery (§5)
- [x] Technology decisions carry claims → citations (MDN, access-dated) or explicit Uncertainty Records;
      the unsised quota figure is recorded as uncertainty, not asserted as fact
- [x] Environment claims measured locally (jsdom probe), not inferred from documentation
- [x] Milestones follow the Task Record's `TDD Enforcement Mode: enabled` (Red → Green → Refactor)
- [ ] **`pk:grill` before code** — the two attacks I most expect: *"your 'persistence' is a browser cache you
      are treating like a database of record"* and *"F-5 last-write-wins is a silent data-loss bug you
      documented rather than fixed."*
- [ ] Human approval of this spec + the Task Record before the first Red commit lands

### Ready-state note

Level 2 readiness is **not** satisfied by this document alone: implementation starts only after
`TASK-m2-persistence-seam` records acceptance criteria, execution scope, active ownership, and the TDD mode,
and the human approves. Planning supplies inputs; it approves nothing.
