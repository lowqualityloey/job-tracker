# Technical Design Document (RFC): M3 — ASP.NET Core Web API over PostgreSQL

- **Author**: Assistant (executing) for Lead Engineer (decision owner)
- **Status**: In Review
- **Created**: 2026-09-11
- **Target Release**: Milestone 3 — Backend (ASP.NET Core Web API)

---

## Planning Record (PromptKit Adaptation)

<a id="PLAN-m3-backend-api"></a>

### Planning Record Metadata

- **Planning Record ID**: `PLAN-m3-backend-api`
- **Planning Depth**: `Full` — mandatory triggers all fire: external HTTP contract, persistent schema, multiple behavioural components, new runtime and dependency ecosystem
- **Owner**: Lead Engineer (accountable for every decision marked `decided`)
- **Record Status**: `ready` — all four architectural forks were answered by the owner on 2026-09-11; implementation is blocked only on approval of this document
- **Local Task Record Link**: `N/A yet` — `pk:tasks` mints `TASK-m3-backend-api` from these inputs after this spec is approved. Per `plan.md`, planning supplies inputs and never creates the Task Record's authority.
- **Workflow Links**: `pk:plan` → `pk:tasks` → `pk:api` + `pk:data` → `pk:test` → `pk:grill` (pre-code) → `pk:review` → `pk:pr`

### Planning Inputs

- **Requested Outcome**: The frontend's six-method `ApplicationRepository` seam is satisfied by an HTTP adapter backed by a real ASP.NET Core API and PostgreSQL, **with zero changes to any file in `src/pages/`, `src/components/`, or `src/state/`** — the payoff that M2b's D-2 deviation was built to earn.
- **Observable Completion Condition**: With `VITE_API_BASE_URL` set, the CDP harness at `docs/spikes/2026-09-11-real-browser-cross-tab-check/crosstab.mjs` passes its write/read checks **against the server** in real Chromium, and `psql` confirms the rows survive a full browser restart; with the variable unset, `npm run verify` is green exactly as it is today.
- **Scope Boundary**: **In** — `api/**` (new), `src/data/httpApplicationRepository.ts`, adapter selection in `src/data/applicationStore.ts`, one additive field on the domain type, `.github/workflows/ci.yml`, `docs/**`. **Out** — auth, multi-tenancy, deployment, server-side filtering, deletion of the localStorage adapter.
- **TDD Enforcement Proposal (reference only)**: `enabled`, as in M2a/M2b. **This cannot activate TDD**; the canonical Task Record owns `TDD Enforcement Mode`.

### Full Planning

- **Explicit Non-Goals**: login/tokens/claims (M4); per-user data isolation (M4, see §4.2 Phase 3); deploying anything to AWS (M5); pagination, server-side search, and sorting (the 5-record dataset does not need them, and filters were decided as view state in M2b); bulk import/export; rate limiting (nothing is publicly reachable yet — §5); OpenAPI client codegen; replacing `domain/validation.ts`; migrating existing `localStorage` records (Assumption 003).
- **Affected Behavioral Components**: the repository seam's HTTP implementation; adapter selection; the domain type (one additive field); the API's endpoints, schema and migrations; CI (a second job); every state/error path that can now receive a network failure instead of a storage failure.
- **Externally Visible Contracts**: a JSON HTTP API (`/api/applications`, `/api/applications/{id}`, `/api/applications/events`, `/api/health`); an RFC 9457 problem envelope with a `code` extension member; `application/problem+json`; an `ETag`/`If-Match` pair on writes. **Also breaking-adjacent: `RepositoryError` gains an eighth variant** (`conflict`) — see `DECISION-m3-backend-api-006`, which is the one change here that touches a locked M2a invariant.
- **Failure or Rollback Considerations**: the local adapter is **not deleted**, so the swap is reversible by unsetting one environment variable; no user data is destroyed by any step in M3; the database is droppable (dev data) and its first migration is pure-additive; the API test suite must never touch the frontend's.
- **Verification Approach**: `npm run verify` unchanged and still green (frontend regression guard); `dotnet test` for the API; a **contract test** asserting the client and server validators agree at every boundary they share; and the real-browser CDP harness promoted from one-off probe to M3's acceptance test (Slice 4).

### Assumption Records

<a id="ASSUMPTION-m3-backend-api-001"></a>
**ASSUMPTION-m3-backend-api-001** — **Unanswered Decision**: whether Amazon RDS offers PostgreSQL **18.6** (the pinned local version) when M5 deploys. **Provisional Answer**: assume yes; pin 18.6 for development and treat the M5 engine version as a separate decision. **Impact if Wrong**: the local pin must drop to a version RDS supports (e.g. 16.x), and `DECISION-m3-backend-api-001`'s exact-version claim stops describing production reality. **Validation Action**: in M5, before selecting an engine, query the AWS API for `DescribeDBEngineVersions` — do not assume from a blog post. **Decision Owner**: Lead Engineer. **Status**: `open`. **Supporting Evidence**: `CITATION-DECISION-m3-backend-api-001-001` (PostgreSQL project). **Resolution Evidence**: N/A — unresolved.

<a id="ASSUMPTION-m3-backend-api-002"></a>
**ASSUMPTION-m3-backend-api-002** — **Unanswered Decision**: whether the API will run as a single instance through M5. **Provisional Answer**: yes — one process, one in-memory fan-out channel for SSE. **Impact if Wrong**: with two or more instances behind a load balancer, an SSE client subscribed to instance B never learns about a write handled by instance A, so `subscribe()` silently under-notifies — a correctness bug that no test in this repo would catch. **Validation Action**: revisit at M5 sizing; the documented escape is PostgreSQL `LISTEN`/`NOTIFY` (already reachable, no new service) or a Redis-backed channel. **Decision Owner**: Lead Engineer. **Status**: `open`. **Supporting Evidence**: `DECISION-m3-backend-api-005`. **Resolution Evidence**: N/A — unresolved.

<a id="ASSUMPTION-m3-backend-api-003"></a>
**ASSUMPTION-m3-backend-api-003** — **Unanswered Decision**: whether the records currently in this browser's `localStorage` are throwaway dev data. **Provisional Answer**: throwaway. **Impact if Wrong**: switching to the API shows a different, apparently empty list, and a real dataset looks lost (it is not — the local adapter stays and the data stays in that profile). **Validation Action**: confirm with the owner before Phase 3 of §4.2 deletes the local adapter. **Decision Owner**: Lead Engineer. **Status**: `open`. **Supporting Evidence**: `docs/spikes/2026-09-11-real-browser-cross-tab-check.md` (observed envelope: 7 records). **Resolution Evidence**: N/A — unresolved.

### Technology and Vendor Decision Records

<a id="DECISION-m3-backend-api-001"></a>
#### Decision Record: `DECISION-m3-backend-api-001`
- **Decision Statement**: Which database engine and exact version does M3 target?
- **Considered Options**: PostgreSQL 18.6 (owner chose PostgreSQL, 2026-09-11); SQL Server (native ASP.NET pairing, but container-only locally and the costliest credible M5 option); SQLite (zero infra, but no server, roles, or pooling — which is the point of the milestone).
- **Selected Option**: **PostgreSQL, pinned to 18.6** for the dev container.
- **Rejected Options**: `postgres:latest` and `postgres:18` as *floating* pins (the version policy forbids selecting `latest`; `18` floats across patch releases, so two sessions could disagree); SQLite and SQL Server as above.
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-001-001`, `CLAIM-DECISION-m3-backend-api-001-002`
- **Remaining Uncertainty**: `UNCERTAINTY-DECISION-m3-backend-api-001-001` (RDS availability — Assumption 001)
- **Decision Owner**: Lead Engineer · **Status**: `decided`
##### Version Selection Fields
- **Version Selection Context**: Greenfield/no existing pin
- **AI Recommendation**: 18.6, the current stable patch release
- **Selected Exact Version**: **18.6** (`postgres:18.6` image tag; RDS engine version to be matched in M5)
- **Release Channel**: stable
- **Support/Lifecycle Status**: current stable major as of 2026-09-11; PostgreSQL 19 is **beta only** (`19 Beta 3` announced 2026-08-13), so prerelease is excluded by the version policy
- **Compatibility Constraints**: needs Npgsql provider matching EF Core 10 (`DECISION-m3-backend-api-003`); local dev requires Docker (present: 29.7.2)
- **Version Rationale**: newest stable major with the newest patch, chosen by measurement rather than recollection — and rejecting 19 because it is a beta, not because of caution about the number
- **Exact-Version Evidence**: `CITATION-DECISION-m3-backend-api-001-001`; plus local measurement `docker run postgres:18 postgres --version` → `postgres (PostgreSQL) 18.6 (Debian 18.6-1.pgdg13+2)` and `docker pull postgres:19` → `not found`, both 2026-09-11
- **Existing Version Baseline**: N/A — greenfield
- **Owner Approval**: Lead Engineer selected PostgreSQL on 2026-09-11; **the 18.6 patch pin needs explicit sign-off in PR #7 review**
- **pk:spike or ADR Link**: ADR candidate — "persistence medium and engine" (STATE §5)

<a id="CLAIM-DECISION-m3-backend-api-001-001"></a>
**CLAIM-DECISION-m3-backend-api-001-001** → `DECISION-m3-backend-api-001` — *PostgreSQL 18.6 is a current supported release, and 19 is unreleased as stable as of 2026-09-11.* → `CITATION-DECISION-m3-backend-api-001-001`

<a id="CITATION-DECISION-m3-backend-api-001-001"></a>
**CITATION-DECISION-m3-backend-api-001-001** — Publisher: PostgreSQL Global Development Group · Document: *PostgreSQL: Versioning Policy* (news line "August 13, 2026: PostgreSQL 18.6, 17.11, 16.15, 15.19, 14.24 and 19 Beta 3 Released!") · URL: <https://www.postgresql.org/support/versioning/> · Access Date: 2026-09-11 · Supports: `CLAIM-DECISION-m3-backend-api-001-001` · **Status**: `verified`

<a id="UNCERTAINTY-DECISION-m3-backend-api-001-001"></a>
**UNCERTAINTY-DECISION-m3-backend-api-001-001** — **Affected claim/context**: that RDS will offer engine version 18.6 in M5. **Impact**: the pin is development-only, or must be lowered before deployment. **Resolution Action**: `Defer the decision` (to M5, with an AWS API query). **Decision Owner**: Lead Engineer. **Status**: `open`. **Supporting Evidence**: `ASSUMPTION-m3-backend-api-001`.

<a id="DECISION-m3-backend-api-002"></a>
#### Decision Record: `DECISION-m3-backend-api-002`
- **Decision Statement**: Which .NET runtime and SDK version, and how is it installed on this machine?
- **Considered Options**: .NET 10 (current LTS); .NET 9 (STS, shorter runway); .NET 8 (LTS, but its support window ends within a year of this milestone); container-only SDK (verified working, no host footprint).
- **Selected Option**: **.NET 10, SDK 10.0.401, installed on the host via `dotnet-install.sh` into `~/.dotnet`** (owner chose host install, 2026-09-11), with `dotnet/sdk:10.0` as the CI and container image.
- **Rejected Options**: container-only SDK — rejected for *authoring* because editor intelligence for C# degrades, which is costly in a project whose purpose is reading code; .NET 8/9 — shorter or already-ending support for a greenfield pin.
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-002-001`, `CLAIM-DECISION-m3-backend-api-002-002`
- **Remaining Uncertainty**: `None` for the runtime; the install itself is an action, not a belief — see Execution note below.
- **Decision Owner**: Lead Engineer · **Status**: `decided`
##### Version Selection Fields
- **Version Selection Context**: Greenfield/no existing pin
- **AI Recommendation**: .NET 10 LTS
- **Selected Exact Version**: **SDK 10.0.401** (ASP.NET Core / .NET 10 line)
- **Release Channel**: LTS
- **Support/Lifecycle Status**: "**.NET 10 is a Long Term Support (LTS) release and will be supported … for three years from November 11, 2025 to November 14, 2028**" — primary source, accessed 2026-09-11
- **Compatibility Constraints**: EF Core 10.0.12 and the Npgsql EF provider 10.0.3 (both latest stable on the feed); Docker image `mcr.microsoft.com/dotnet/sdk:10.0`; Node stays 24.x for the frontend
- **Version Rationale**: newest LTS, which maximises the window in which this portfolio project stays explainable; the 8→10 span comfortably outlives M3-M5
- **Exact-Version Evidence**: `CITATION-DECISION-m3-backend-api-002-001`, `CITATION-DECISION-m3-backend-api-002-002`; local measurement `docker run --rm mcr.microsoft.com/dotnet/sdk:10.0 dotnet --version` → `10.0.401` (2026-09-11)
- **Existing Version Baseline**: N/A — none; `dotnet` is not installed on this host today (measured 2026-09-11)
- **Owner Approval**: install path approved 2026-09-11; **the version number itself needs sign-off in PR #7**
- **Execution note**: deliberately **not installed yet**. Installing ~500 MB against a version this document has not had approved would put the machine ahead of the decision. Slice 0 installs it, with the approval recorded first.
- **pk:spike or ADR Link**: None

<a id="CLAIM-DECISION-m3-backend-api-002-001"></a>
**CLAIM-DECISION-m3-backend-api-002-001** → `DECISION-m3-backend-api-002` — *.NET 10 is LTS and is supported until 2028-11-14.* → `CITATION-DECISION-m3-backend-api-002-001`

<a id="CLAIM-DECISION-m3-backend-api-002-002"></a>
**CLAIM-DECISION-m3-backend-api-002-002** → `DECISION-m3-backend-api-002` — *.NET 10 documentation for ASP.NET Core error handling and problem details exists and was updated in 2026, i.e. the line is the documented current release.* → `CITATION-DECISION-m3-backend-api-002-002`

<a id="CITATION-DECISION-m3-backend-api-002-001"></a>
**CITATION-DECISION-m3-backend-api-002-001** — Publisher: .NET project (`dotnet/core`) · Document: *.NET 10 release notes — release status* · URL: <https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/README.md> · Access Date: 2026-09-11 · Supports: `CLAIM-DECISION-m3-backend-api-002-001` · **Status**: `verified`

<a id="CITATION-DECISION-m3-backend-api-002-002"></a>
**CITATION-DECISION-m3-backend-api-002-002** — Publisher: Microsoft · Document: *Handle errors in ASP.NET Core APIs (ASP.NET Core 10.0)*, "Last updated 2026-03-04" · URL: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0> · Access Date: 2026-09-11 · Supports: `CLAIM-DECISION-m3-backend-api-002-002` · **Status**: `verified` · *Note: the page also carried a "not the latest version" banner on retrieval; the quoted content is the `aspnetcore-10.0` view. Recorded rather than smoothed over.*

<a id="DECISION-m3-backend-api-003"></a>
#### Decision Record: `DECISION-m3-backend-api-003`
- **Decision Statement**: Data access approach — EF Core, micro-ORM (Dapper), or raw `Npgsql`?
- **Considered Options**: **EF Core 10.0.12** with `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (migrations tooling, change tracking, `xmin` concurrency tokens, the industry default for this stack); **Dapper** (explicit SQL, more visible to a learner, but migrations become someone else's problem); **raw Npgsql** (maximum transparency, minimum leverage at 5 records).
- **Selected Option**: **EF Core 10.0.12 + Npgsql provider 10.0.3**, with SQL logging on in Development so the generated statements stay visible — the honest answer to the "EF hides the SQL" objection in a learning repo.
- **Rejected Options**: Dapper and raw Npgsql for M3; **revisit Dapper in M4 only if a query needs shape EF cannot express**, rather than mixing both by default.
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-003-001`, `CLAIM-DECISION-m3-backend-api-003-002`
- **Remaining Uncertainty**: `UNCERTAINTY-DECISION-m3-backend-api-003-001`
- **Decision Owner**: Assistant recommended; Lead Engineer ratifies at PR #7 · **Status**: `decided`
##### Version Selection Fields
- **Version Selection Context**: Greenfield
- **AI Recommendation**: the feed's latest stable of each
- **Selected Exact Versions**: `Microsoft.EntityFrameworkCore` **10.0.12**; `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**
- **Release Channel**: stable (both)
- **Support/Lifecycle Status**: latest stable on NuGet at access date; EF Core 10.x aligns with the .NET 10 LTS line chosen above
- **Compatibility Constraints**: provider and EF major versions must match the runtime; `dotnet ef` migrations require the design package
- **Version Rationale**: measured from the package feed on 2026-09-11 rather than recalled — and the presence of a 10.x provider is itself evidence the .NET 10 line is GA and serviced
- **Exact-Version Evidence**: `CITATION-DECISION-m3-backend-api-003-001`
- **Existing Version Baseline**: N/A
- **Owner Approval**: pending PR #7 (the compatibility uncertainty below is the reason to look at this one)
- **pk:spike or ADR Link**: None

<a id="CLAIM-DECISION-m3-backend-api-003-001"></a>
**CLAIM-DECISION-m3-backend-api-003-001** → `DECISION-m3-backend-api-003` — *10.0.12 and 10.0.3 are the latest stable versions published for those two packages.* → `CITATION-DECISION-m3-backend-api-003-001`

<a id="CLAIM-DECISION-m3-backend-api-003-002"></a>
**CLAIM-DECISION-m3-backend-api-003-002** → `DECISION-m3-backend-api-003` — *the Npgsql provider version 10.0.3 is compatible with EF Core 10.0.12.* → `UNCERTAINTY-DECISION-m3-backend-api-003-001` **(`verified` was deliberately not claimed)**

<a id="CITATION-DECISION-m3-backend-api-003-001"></a>
**CITATION-DECISION-m3-backend-api-003-001** — Publisher: NuGet.org feeds for the two owning projects · Document: *flatcontainer version index (`index.json`)* · URLs: <https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore/index.json> · <https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json> · Access Date: 2026-09-11 · Supports: `CLAIM-DECISION-m3-backend-api-003-001` · **Status**: `verified`

<a id="UNCERTAINTY-DECISION-m3-backend-api-003-001"></a>
**UNCERTAINTY-DECISION-m3-backend-api-003-001** — **Affected claim**: `CLAIM-…-003-002`, provider/EF compatibility. It rests on matching major version numbers, **not** on reading the provider's compatibility matrix. **Impact**: Slice 0's first `dotnet ef migrations add` may fail with a provider/EF mismatch, forcing a version change mid-slice. **Resolution Action**: `Proceed with an explicitly accepted assumption` — the first build validates or falsifies it within minutes, so a spike would cost more than it saves. **Decision Owner**: Lead Engineer. **Status**: `open`.

<a id="DECISION-m3-backend-api-004"></a>
#### Decision Record: `DECISION-m3-backend-api-004`
- **Decision Statement**: Error response format for the API.
- **Considered Options**: **RFC 9457 problem+json** with a `code` extension member (standard, ASP.NET Core has first-party support via `AddProblemDetails`/`Results.Problem`); a bespoke `{ ok: false, error: {…} }` envelope mirroring `Result<T, RepositoryError>` exactly (no translation layer, but it re-invents a format the ecosystem already speaks and quietly redefines HTTP status semantics).
- **Selected Option**: **RFC 9457 `application/problem+json`**, with extension members `code` (one of the eight seam codes) and, for validation failures, `errors[]` of `{ pointer, detail }`.
- **Rejected Options**: bespoke envelope; `about:blank`-only problems (status code alone cannot carry `fieldErrors`, which the form already renders).
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-004-001`, `CLAIM-DECISION-m3-backend-api-004-002`
- **Remaining Uncertainty**: `None` on the format; the *vocabulary* question is its own record (`DECISION-m3-backend-api-006`).
- **Decision Owner**: Lead Engineer · **Status**: `decided`
##### Version Selection Fields
- **Version Selection Context**: Greenfield; the relevant "version" is the specification
- **Selected Exact Version**: **RFC 9457** (Proposed Standard, July 2023), which **obsoletes RFC 7807** — stated because ASP.NET Core's own documentation still links 7807, and the newer number is the one to cite
- **Release Channel**: standards-track, final
- **Support/Lifecycle Status**: current standard; first-party generator support in ASP.NET Core 10
- **Compatibility Constraints**: §3.2 requires extension-member names to start with a letter, use `ALPHA`/`DIGIT`/`_`, and **be three characters or longer** — which is why the member is `code` and not `c`, and why clients `MUST` ignore members they do not know (the reason adding fields to the envelope is safe but removing them is not)
- **Version Rationale**: interoperability and one less private format to document; the client adapter's translation to `RepositoryError` is then the only place our vocabulary lives
- **Exact-Version Evidence**: `CITATION-DECISION-m3-backend-api-004-001`, `CITATION-DECISION-m3-backend-api-002-002`
- **Existing Version Baseline**: N/A
- **Owner Approval**: Lead Engineer, PR #7
- **pk:spike or ADR Link**: ADR candidate — "error envelope"

<a id="CLAIM-DECISION-m3-backend-api-004-001"></a>
**CLAIM-DECISION-m3-backend-api-004-001** → `DECISION-m3-backend-api-004` — *RFC 9457 defines `type`/`status`/`title`/`detail`/`instance` plus extension members, uses `application/problem+json`, and obsoletes RFC 7807.* → `CITATION-DECISION-m3-backend-api-004-001`

<a id="CLAIM-DECISION-m3-backend-api-004-002"></a>
**CLAIM-DECISION-m3-backend-api-004-002** → `DECISION-m3-backend-api-004` — *ASP.NET Core can generate problem-details responses for both handled and unhandled errors once `AddProblemDetails`/`UseExceptionHandler`/`UseStatusCodePages` are configured.* → `CITATION-DECISION-m3-backend-api-002-002`

<a id="CITATION-DECISION-m3-backend-api-004-001"></a>
**CITATION-DECISION-m3-backend-api-004-001** — Publisher: IETF · Document: *RFC 9457 — Problem Details for HTTP APIs* · URL: <https://datatracker.ietf.org/doc/html/rfc9457> · Access Date: 2026-09-11 · Supports: `CLAIM-DECISION-m3-backend-api-004-001` · **Status**: `verified`

<a id="DECISION-m3-backend-api-005"></a>
#### Decision Record: `DECISION-m3-backend-api-005`
- **Decision Statement**: What implements `subscribe()` once the data is remote?
- **Considered Options**: **SSE** (`text/event-stream` + browser `EventSource`); polling; SignalR; a no-op unsubscriber.
- **Selected Option**: **SSE** at `GET /api/applications/events` (owner chose it 2026-09-11), one server-side channel, one `event: change` message per committed write.
- **Rejected Options**: **no-op unsubscriber** — it would make a documented interface mean nothing and regress cross-tab sync; **polling** — kept as the *fallback behaviour* inside the adapter (see §4.3 SSE contract) rather than the primary design; **SignalR** — bidirectional and idiomatic but brings WebSocket negotiation and sticky-session concerns to M5 for an app with five rows.
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-005-001`
- **Remaining Uncertainty**: `UNCERTAINTY-DECISION-m3-backend-api-005-001`
- **Decision Owner**: Lead Engineer · **Status**: `decided`
##### Version Selection Fields
- N/A — this decision selects a protocol already fixed by the platform (HTTP) and the browser API `EventSource`; no external package version is chosen. The *behavioural* claims are deferred to a real run rather than asserted: see the uncertainty record.
- **Owner Approval**: Lead Engineer selected SSE on 2026-09-11
- **pk:spike or ADR Link**: ADR candidate — "change notification transport"

<a id="CLAIM-DECISION-m3-backend-api-005-001"></a>
**CLAIM-DECISION-m3-backend-api-005-001** → `DECISION-m3-backend-api-005` — *SSE needs no new client library, rides the same HTTP port as the API, and therefore needs no CORS or WebSocket upgrade path through the dev proxy.* → `UNCERTAINTY-DECISION-m3-backend-api-005-001`

<a id="UNCERTAINTY-DECISION-m3-backend-api-005-001"></a>
**UNCERTAINTY-DECISION-m3-backend-api-005-001** — **Affected context**: two claims were **not** looked up in a specification during this session — that `EventSource` behaves as assumed in current Chromium, and that an SSE stream survives buffering at whatever proxy M5 puts in front (ALB/nginx buffering is exactly where SSE goes quiet). **Impact**: silent loss of external-change notification, which is precisely the behaviour M2b was built for. **Resolution Action**: `Proceed with an explicitly accepted assumption` for development, and **`Run a targeted pk:spike` in M5** against the real proxy before trusting it in production; Slice 4 re-runs the CDP harness over HTTP, which converts the first half from belief into observation. **Decision Owner**: Lead Engineer. **Status**: `open`. **Supporting Evidence**: `ASSUMPTION-m3-backend-api-002`.

<a id="DECISION-m3-backend-api-006"></a>
#### Decision Record: `DECISION-m3-backend-api-006`
- **Decision Statement**: Does `RepositoryError` grow an eighth variant, `conflict`?
- **Considered Options**: **(a) add `{ code: 'conflict'; id: string }`**; **(b) map HTTP 409 onto `unavailable`**; **(c) onto `storage-error`**; **(d) reject optimistic concurrency in M3** so 409 cannot occur.
- **Selected Option**: **(a)**.
- **Rejected Options**: (b) is a lie — `unavailable` tells the provider to refuse further writes, and the service is plainly available; (c) mislabels a *successful* server response as a transport fault; (d) throws away the one thing that makes M3's concurrency worth learning, and would leave two tabs silently overwriting each other.
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-006-001`
- **Remaining Uncertainty**: `None` — this is our vocabulary, not a vendor claim.
- **Decision Owner**: Lead Engineer · **Status**: `decided` — **requires explicit sign-off: this widens a union M2a deliberately froze.**
##### The argument, because it is the interesting one
M2a's rule was **"no eighth code"**, made when a write could be refused for exactly one reason beyond validation and quota. That rule was never about the number seven; it was about not inventing codes for convenience. A **conflict** is not an invention: it is a failure mode that `localStorage` *physically could not express* — two tabs writing the same key always resolved silently, last write winning, which is why M2b could observe "the other tab updated" as a feature rather than a hazard. Moving to a server with row versions turns a previously invisible behaviour into a visible one. Growing the vocabulary when reality grows is exactly when the rule permits it; mapping a 409 onto an unrelated code to keep a count tidy would be the actual violation.
- **Version Selection Fields**: N/A — internal type, no external technology or version.
- **Existing Version Baseline**: **seven** variants today, verified by reading `src/domain/applicationRepository.ts:19-29` — `validation`, `not-found`, `unavailable`, `quota-exceeded`, `corrupt-data`, `unsupported-version`, `storage-error`. *(A planning note earlier this session called this "13 codes"; the real number is 7 — 13 is an invariant count. Corrected rather than carried forward.)*
- **Owner Approval or Accepted Assumption**: **open** — Lead Engineer's approval requested in PR #7 review.
- **pk:spike or ADR Link**: ADR candidate — "why the error union grew once, and what would justify it again"

<a id="CLAIM-DECISION-m3-backend-api-006-001"></a>
**CLAIM-DECISION-m3-backend-api-006-001** → `DECISION-m3-backend-api-006` — *no existing `RepositoryError` variant carries "the row changed underneath you", and `unavailable` additionally triggers the provider's write-refusal gate, so reusing it would change app behaviour as well as its meaning.* → `CITATION-DECISION-m3-backend-api-006-001`

<a id="CITATION-DECISION-m3-backend-api-006-001"></a>
**CITATION-DECISION-m3-backend-api-006-001** — Publisher: this repository (owning project) · Document: `src/domain/applicationRepository.ts` lines 19-29 and 53-58, and `src/state/applicationsProvider.tsx`'s refusal gate · URL: <https://github.com/lowqualityloey/job-tracker/blob/main/src/domain/applicationRepository.ts> · Access Date: 2026-09-11 · Supports: `CLAIM-DECISION-m3-backend-api-006-001` · **Status**: `verified` (read directly this session)

<a id="DECISION-m3-backend-api-007"></a>
#### Decision Record: `DECISION-m3-backend-api-007`
- **Decision Statement**: Who mints the `id` of a new application, and how are retried writes made safe?
- **Considered Options**: **server-mints** (`gen_random_uuid()` column default; conventional REST, but a retried `POST` creates a duplicate unless an `Idempotency-Key` header is invented); **client-mints** in the HTTP adapter (the adapter calls `crypto.randomUUID()` exactly as the localStorage adapter does today, sends the id in the body, and the unique PK makes a retry a 409 rather than a second row).
- **Selected Option**: **client-mints, with `gen_random_uuid()` kept as the column default** so the API is still correct for a client that omits it; `POST` on an existing id returns **409**, and **the duplicate is refused with `409` so no second row can exist** (idempotent create, `DECISION-007` as amended 2026-09-11 19:00 UTC — see `Amendment` below and test §17; the adapter **surfaces** the conflict to the caller rather than silently re-reading) (idempotent create).
- **Rejected Options**: server-mints alone — it forces either duplicate rows on retry or a bespoke idempotency header, and it would change `create(input)`'s contract while client-mints does not.
- **Material Claim Links**: `CLAIM-DECISION-m3-backend-api-007-001`
- **Remaining Uncertainty**: `None` internal; the general risk of client-supplied identifiers is recorded honestly below.
- **Decision Owner**: Lead Engineer · **Status**: `decided`

> **Amendment (2026-09-11 19:00 UTC, gap 9, owner decision pending ratification of (B))** — the clause
> *"the adapter resolves that by re-reading the row and treating an existing record as success"* is **narrowed to what is
> built and proven**, and the original wording is preserved here rather than deleted, so a reader can see what was
> promised versus what shipped. **Why the change is a narrowing and not a regression:** the decision's *purpose* — a
> retried `POST` must not create a duplicate — is guaranteed and tested (`-034`: `409` and `count(*) == 1`). What the
> clause additionally promised, turning the conflict into a **silent success**, was never implemented, and
> `-037`'s error table asserts the opposite (`create()` throws `{code:'conflict', id}`). Choosing to swallow a conflict
> silently is a **user-visible** behaviour (a double-submitter stops learning the second submit was refused) and
> therefore needs its own spec pass with an observable outcome, not a late edit that narrows the §4.3 table AC-8 exists
> to protect. **Consequence**: `BEHAVIOR-034`'s client clause and `AC-7`'s third clause are amended to match, in the
> same commit, so the three cannot drift. **Alternative kept open**: implementing it for real is option (A) in
> `docs/tests/2026-09-11-test-m3-backend-api.md` §17 (~2h), which is M4-scale polish, not an M3 defect.

##### Fields
- **Version Selection Fields**: N/A — no external technology.
- **Risk accepted**: a client-assigned surrogate key means a buggy or hostile client can *choose* an id and collide with an existing row. Mitigation: the unique constraint makes the collision loud rather than silent, M3 has no authentication to evade, and M4's owner column makes cross-user collision worthless. **Recorded so it is not mistaken for an oversight.**
- **Continuity**: `id` became an opaque `string` in M2a precisely so this decision could be made at the adapter, and `ApplicationInput` carries no `id` field (`applicationRepository.ts:56`) — so **the adapter mints it, and no page ever sees a raw identifier being invented.**

<a id="CLAIM-DECISION-m3-backend-api-007-001"></a>
**CLAIM-DECISION-m3-backend-api-007-001** → `DECISION-m3-backend-api-007` — *`create(input: ApplicationInput)` returns the created entity, so a client-minted id requires no signature change anywhere in the frontend.* → `CITATION-DECISION-m3-backend-api-006-001`

<a id="DECISION-m3-backend-api-008"></a>
#### Decision Record: `DECISION-m3-backend-api-008`
- **Decision Statement**: API test stack, and how tests obtain a real PostgreSQL.
- **Considered Options**: **xUnit + `Testcontainers.PostgreSql`** (real server per run, no CI service container to configure, tests exercise the same engine as dev); **NUnit/MSTest** (equally capable, less common in new .NET work); an EF **InMemory** provider (**rejected outright** — it does not speak SQL, so constraints, `xmin`, and `CHECK`s would all be untested, which is the exact trap M2a escaped by refusing to mock `localStorage`).
- **Selected Option**: **proposed** — xUnit + Testcontainers.PostgreSql.
- **Rejected Options**: EF InMemory, as above.
- **Material Claim Links**: `None` — **deliberately**: no versions were looked up for this record, so no claim is made, so nothing can be falsely marked verified.
- **Remaining Uncertainty**: `UNCERTAINTY-DECISION-m3-backend-api-008-001`
- **Decision Owner**: Lead Engineer · **Status**: `proposed`
##### Version Selection Fields
- **Selected Exact Versions**: `None` — **`proposed` on purpose.** The exact packages will be pinned from the NuGet feed *at the moment the test project is created*, with that access date recorded in the Task Record. Pinning them now would be a guess about what is current in a month.
- **Existing Version Baseline**: N/A
- **Owner Approval**: requested at PR #7

<a id="UNCERTAINTY-DECISION-m3-backend-api-008-001"></a>
**UNCERTAINTY-DECISION-m3-backend-api-008-001** — **Affected context**: Testcontainers needs a working Docker daemon **on the CI runner**, which has never been exercised in this repository (today's runner only executes npm). **Impact**: the API test job could be red on arrival for infrastructure reasons. **Resolution Action**: `Run a targeted pk:spike` — Slice 0's first CI run is that spike; if runners cannot provide Docker, fall back to a GitHub **service container** for PostgreSQL, which requires no Testcontainers at all. **Decision Owner**: Lead Engineer. **Status**: `open`.

---

## 1. Executive Summary & Problem Statement

The frontend has, since M2a, hidden every byte of persistence behind one six-method interface. It now runs on data that lives in one browser profile: it cannot be reached from a phone, survives no other device, and — most usefully for this project — offers no server-side place to put validation, identity, concurrency, or a user. **M3 replaces the medium, not the interface**, which is the experiment the whole architecture has been set up to run: if `pk:plan`'s design holds, an HTTP adapter plus a real API lands with **zero edits to any page or component**, and every place that claim fails is a defect worth finding in a practice repo rather than a production one.

The outcome is a deployed-able ASP.NET Core Web API over PostgreSQL with migrations, optimistic concurrency, standard error envelopes, and change notifications — plus, for the learning record, a measured answer to what breaks when a synchronous local store becomes an asynchronous network service.

## 2. Goals and Explicit Non-Goals

### Goals (In Scope)
1. **G-1** `HttpApplicationRepository` satisfies `ApplicationRepository`'s six methods, and TypeScript's structural check — not a comment — proves it.
2. **G-2** Adapter selection by `VITE_API_BASE_URL`; **when unset the app behaves exactly as it does today**, so M3 can never take the working frontend down.
3. **G-3** A real schema with constraints the domain cannot bypass: `NOT NULL`, a `CHECK` on status, a UUID primary key, FK-free but future-proof for M4's `owner_id`.
4. **G-4** Optimistic concurrency via PostgreSQL `xmin` surfaced as an `ETag`, so two tabs cannot silently overwrite — closing M2b's handed-over **P2-2**.
5. **G-5** `subscribe()` over SSE, keeping cross-tab sync working when the medium is a server.
6. **G-6** Contract test proving client and server validators agree at every shared boundary — two definitions of "valid", one truth (`§5`).
7. **G-7** CI gains an `api` job that cannot fail because of the frontend job and vice versa (`paths` filters).
8. **Measurable targets** (dev machine, 5-50 rows — deliberately modest so a number is never unfalsifiable): `GET /api/applications` **p50 < 30 ms, p95 < 80 ms**; `POST` **p95 < 150 ms** including commit; SSE notification observed in the second tab in **< 500 ms**; API suite runs in **< 60 s** in CI; **0** unhandled exceptions reaching a client (everything becomes a problem response); frontend bundle **not measured here** — the API client uses `fetch`, so the bundle delta must stay **< 2 kB** and Slice 3 records the actual number from the build output.

### Non-Goals (Explicit Scope Boundary)
Authentication/authorisation, tenants, roles (M4) · deploying to AWS or any cloud (M5) · pagination/search/sorting · bulk import/export · rate limiting · OpenAPI/Swagger client **codegen** (`Microsoft.AspNetCore.OpenApi` may be added for *browsing*, never as a source of generated types) · soft delete, archive, audit history · email/notifications · **deleting the localStorage adapter** · migrating existing local records (Assumption 003) · any change to a page, component, or the provider's state machine.

## 3. Architecture & System Context

### High-Level Architecture Diagram
```text
                     development                                        M3 boundary
┌───────────────────────────────────────────────┐   ┌────────────────────────────────────────────┐
│  Browser (React, unchanged pages)             │   │  api/  — ASP.NET Core 10 (.NET 10 SDK)     │
│                                               │   │                                            │
│  pages / components / state  ── never import transport ──┐                                    │
│    └─ src/data/applicationStore.ts            │   │  endpoints (thin: HTTP ⇄ Result)           │
│         ├─ VITE_API_BASE_URL unset            │   │    └─ ApplicationCatalog  ← DEEP MODULE    │
│         │    └─ LocalStorageApplicationRepo   │   │        validation · EF Core · error map    │
│         └─ set → HttpApplicationRepository ───┼───┼───►  /api/applications      (CRUD)         │
│                        (fetch + EventSource)  │   │        /api/applications/events (SSE)      │
│                                               │   │        /api/health                         │
│  Vite dev server: /api proxy → loopback ──────┼───┼──► no CORS configuration exists           │
└───────────────────────────────────────────────┘   └────────────────┬───────────────────────────┘
                                                                     │ EF Core ( Npgsql 10.0.3 )
                                                          ┌──────────▼───────────┐
                                                          │ PostgreSQL 18.6      │  Docker, loopback-published
                                                          │ applications table   │  port 5432 → 127.0.0.1:5432
                                                          │ + migrations history │
                                                          └──────────────────────┘
```
**Dev-loop note:** the Vite proxy makes `/api` same-origin, so **no CORS policy is configured at all** — one whole class of misconfiguration (wildcard origin + credentials) is prevented structurally rather than reviewed for correctness.

### Deep Module Decomposition & Seams

| Module | Interface it offers | Depth argument (deletion test) |
| :--- | :--- | :--- |
| `ApplicationCatalog` (API) | `ListAsync`, `GetAsync`, `CreateAsync`, `ReplaceAsync`, `RemoveAsync` — each returning a domain result, never an `ActionResult` | **Deleting it scatters validation, EF usage, conflict detection and event publication across every endpoint.** It concentrates the whole of "what makes an application write legal" in one file. This is the deep module. |
| Endpoints (Minimal API) | HTTP verbs ⇄ catalog calls, status codes, `ETag`/`If-Match` translation | Deliberately **thin, and that is correct** — they are the translation layer, not a business layer. No `IService` between them and the catalog. |
| `DbContext` (`JobTrackerDb`) | `DbSet<ApplicationEntity>` + configuration | **Used directly by the catalog.** An `IRepository<Application>` wrapper over EF is rejected outright: it is the textbook shallow pass-through the workflow warns about, and EF's `DbContext` already *is* a repository. |
| `HttpApplicationRepository` (frontend) | the existing 6-method `ApplicationRepository` | Deleting it means pages call `fetch` — complexity *moved*, not concentrated. Its whole job: translate HTTP + problem+json into `Result<T, RepositoryError>`, so **nothing above `src/data/` ever learns that a network exists.** |
| `applicationStore` selector | `createApplicationStore()` unchanged | One environment variable chooses a medium. This is the seam's payoff and G-2's test target. |

**Locked invariants carried forward** (each already holds; each must still hold in M3):
1. Only `src/data/` touches `localStorage` **or its key names** → extended: **only `src/data/` touches `fetch`, the API base URL, HTTP status codes, or `EventSource`.**
2. No `Number(id)` anywhere; ids stay opaque strings.
3. Envelope `schemaVersion` gate stays in the local adapter (untouched).
4. Fail-closed on unknown data; `RepositoryError` stays a **closed** union (widened once, by `DECISION-006`, not left open-ended).
5. Filters/search stay **view state** owned by the page; the API returns the collection and never a filtered subset. (Choosing otherwise would move logic to the server for no user-visible gain and would invalidate M2b's filter tests.)
6. `npm run verify` stays the frontend's gate — `api/` gets its **own** gate, and neither is folded into the other silently.

**State ownership & invalidation across concurrent writers** (workflow step 2.3): PostgreSQL is the single system of record; the browser holds a **copy** with no independent authority. Invalidation is pushed (SSE `change` → provider re-`list()`), and — the important inversion from M2b — **the writer now receives its own change too**, because the server does not know which client wrote. That is fine for a re-read, but it makes M2b's handed-over **P2-2 real**: two re-reads can now land out of order, so a stale response could paint an older list over a newer one. M3 fixes it where it belongs — in the adapter layer, by tagging each `list()` with a monotonic sequence and dropping responses whose sequence is older than one already applied (`BEHAVIOR-039`), *not* by asking the provider to care.

## 4. Detailed Design & Contracts First

### 4.1 Data Model

```sql
CREATE TABLE applications (
    id           uuid PRIMARY KEY,                -- client-minted (DECISION-007); DB default gen_random_uuid()
    company_name text NOT NULL,
    job_title    text NOT NULL,
    location     text NULL,
    status       text NOT NULL
                 CHECK (status IN ('Saved','Applied','Interview','Rejected','Offer')),
    applied_at   date NULL,                       -- see the type decision below
    notes        text NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX applications_updated_at_idx ON applications (updated_at DESC);
```
**Two of M2's four still-unanswered questions get settled here** — which is why this document was worth writing before any code:

- **`appliedAt` (M2 question 3)** stays **`date`, not `timestamptz` and not a branded ISO string.** The domain question is *"on which day did I apply?"*, not *"at what instant?"*. A timestamptz would let the server's timezone decide which day a record shows — the classic off-by-one-day bug, imported for no benefit. On the wire it remains a `yyyy-MM-dd` string, which is what `src/domain/types.ts` already carries, so **the frontend changes not at all.** A `Date` object is explicitly *not* introduced into the domain: JSON has no date type, and constructing one re-opens the same timezone question at every comparison.
- **Status (M2 question 4)** is a **fixed union in TypeScript** *and* a `CHECK` constraint in PostgreSQL; a data-driven lookup table is rejected for M3 (one list, five values, no user-editable semantics). The *reason for choosing `CHECK` over a PostgreSQL `ENUM` type* is migration ergonomics: adding a status later is `DROP CONSTRAINT` + `ADD CONSTRAINT` (an **expand**, safe and reorderable), whereas `ALTER TYPE … ADD VALUE` cannot run inside a transaction in the version chosen and drags a special case into every migration. The `CHECK` list is duplicated in the API's FluentValidation-equivalent rules, and the two must agree — enforced by test, not by comment (`G-6`).

**Domain type change (the only frontend type edit, and it is additive):** `JobApplication` gains `revision?: number` — an opaque row version from `xmin`. **No page reads or writes it**; the adapter sets it on reads and echoes it as `If-Match` on writes; the local adapter never populates it, which is itself the honest signal that `localStorage` **cannot detect a conflict at all** (last write always won, invisibly).

### 4.2 Zero-Downtime Migration Plan (Expand–Contract)

M3 changes **no existing column** (the table is new: pure expand), so the real Expand-Contract subject here is the **medium swap** — a case the pattern applies to, one step removed from a schema:

| Phase | When | What happens | Safety property |
| :--- | :--- | :--- | :--- |
| **1 Expand** | M3 | PostgreSQL + API come into existence beside the untouched localStorage path. Selection by environment variable; **both media remain fully functional** | No user-visible regression possible with the flag off; `npm run verify` stays green throughout, which is the guard |
| **2 Dual-read / backfill** | M3 | **No backfill is built** (Assumption 003: dev data). The "backfill" that does exist is behavioural: any record created via HTTP is written only to PostgreSQL, and reading it back requires the API. Documented rather than coded | Explicit and reversible; nothing is copied, therefore nothing can be copied wrong |
| **3 Contract** | **M4** (after auth exists) | Delete `LocalStorageApplicationRepository` and the key-name literals; the `unsupported-version`/`quota-exceeded` codes become unreachable and are **removed in a separate, named commit** | Precondition recorded now: **confirm with the owner that no browser profile holds data that matters.** Deleting code ≠ deleting the user's data, and the removal commit must say which of the two it touched |

**Rollback**: unset `VITE_API_BASE_URL` (instant, client-side); `git revert` the API commits; `dotnet ef migrations remove` before anything is applied, or `migrations script` down to the previous snapshot after. **RPO/RTO**: not yet meaningful (no production data, no deployment); M5 must state them when it picks a snapshot policy — recorded as a deliberate gap rather than a fake number.

### 4.3 API Endpoints & Contracts

| Method / path | Request | Success | Errors (problem+json, `code`) |
| :--- | :--- | :--- | :--- |
| `GET /api/applications` | — | `200 Application[]` (unordered is fine; client sorts) | `503 unavailable`, `500 storage-error` |
| `GET /api/applications/{id}` | — | `200 Application` + `ETag: "<xmin>"` | `404 not-found` |
| `POST /api/applications` | `{ id, companyName, jobTitle, location?, status, appliedAt?, notes? }` | `201` + `Location` + body + `ETag` | `400 validation` (+`errors[]`), `409 conflict` (id exists), `503`, `500` |
| `PUT /api/applications/{id}` | full replacement; **`If-Match: "<xmin>"` required** | `200` + new `ETag` | `404 not-found`, `409 conflict` (stale version), `400 validation`, `428`→**mapped to `409`** (missing precondition, see note) |
| `DELETE /api/applications/{id}` | `If-Match` optional | `204` | `404 not-found`, `409 conflict` |
| `GET /api/applications/events` | `Accept: text/event-stream` | `200`, `event: change` + `data: {"id":"…"}` after each committed write | n/a (a dropped stream reconnects) |
| `GET /api/health` | — | `200 {"status":"ok"}` | used by CI/Slice 0 readiness only |

**Problem envelope** (mirrors RFC 9457 §3's own validation example):
```json
{ "type": "https://job-tracker.local/probs/validation",
  "title": "The application record is not valid.",
  "status": 400, "detail": "Two fields failed validation.",
  "instance": "/api/applications",
  "code": "validation",
  "errors": [ { "pointer": "#/companyName", "detail": "Company name is required." } ] }
```
**HTTP ⇄ `RepositoryError` mapping** — total, with no silent fallthrough:

| HTTP + `code` | becomes | rationale |
| :--- | :--- | :--- |
| `400 validation` + `errors[]` | `{ code:'validation', fieldErrors }` | pointer → field name; a pointer naming an unknown field becomes `storage-error` (loud, not guessed) |
| `404` | `{ code:'not-found', id }` | direct |
| `409 conflict` | `{ code:'conflict', id }` | **new variant, `DECISION-006`** |
| `503` / any `502`,`504` from an intermediary | `{ code:'unavailable' }` | provider's refusal gate engages — same behaviour as storage being blocked |
| `200/201` with a body that fails runtime parsing | `{ code:'corrupt-data' }` | **new response for the HTTP medium**, replacing "corrupt envelope on disk"; the response is validated at the boundary, **not** trusted because TypeScript said so |
| transport never reached the server (`TypeError` from `fetch`), or a non-problem body on an error status | `{ code:'storage-error', detail }` | deliberately **not** `unavailable`: "we could not learn anything" is not "the server said it is busy" |
| any *other* `code` we do not recognise | `corrupt-data` | fail closed. The alternative — a default that guesses — is how a new server code becomes a silent client bug |

**Codes that become unreachable over HTTP**: `quota-exceeded`, `unsupported-version` (both `localStorage` concepts). They **stay in the union** while the local adapter exists (Phase 3 removes them) — recorded here so nobody "simplifies" them away in Slice 3 and then discovers M4's tests reference them.

**`subscribe()` over SSE**: the interface `subscribe(onExternalChange: () => void): () => void` **cannot report an error** — it hands back only an unsubscriber. That is a real constraint, not an inconvenience: so the adapter owns recovery invisibly. Policy: `EventSource` with browser-native reconnect; **on `open` after any disconnect, call `onExternalChange()` once** (a dropped stream may have missed events, and a re-read is always safe); on the unsubscriber, close the stream. No page or provider change, and a dropped connection degrades to "reads only refresh on the next user action" rather than to stale-forever.

**Concurrency**: `PUT` requires `If-Match`. A mismatch, or a missing header, is `409 conflict` — the UI already refuses to overwrite on refusal (`DECISION-006` rationale), and the message is *"someone else changed this record; reload to see it"*, which is the first user-facing sentence in this project that only a server could make necessary.

## 5. Security, Privacy & Failure Modes (FMEA)

### Security & Multi-Tenancy Audit
- **Unauthenticated by design in M3 — and that is the section that must be read carefully.** There is no login, so every endpoint is world-readable and world-writable **to anyone who can reach the port**. The controls are therefore *reachability*, not authorization: the API binds to loopback in Development; the container publishes `127.0.0.1:5432`/loopback only, never `0.0.0.0`; **M5 must not expose this API publicly until M4 exists**, and that ordering is a hard dependency between milestones, written into STATE §2 rather than left to memory.
- **Multi-tenancy**: not applicable today (one owner) but the *shape* is decided now: M4 adds `owner_id uuid NOT NULL` as an **expand** (nullable → backfill to the single owner → `NOT NULL`), and every query then filters by it. Deliberately **not** added in M3 as an empty column: an unpopulated, unenforced tenant column is worse than none, because it advertises isolation that nothing provides.
- **Input validation**: server-side is **authoritative**; client-side is **UX only**, and both exist on purpose. The pair is duplicated risk, not duplicated truth — `G-6`'s contract test feeds the same boundary values (empty, exactly-max-length, one over, whitespace, unicode, control chars, 10 kB notes) through `domain/validation.ts` and the API's validator and fails if the verdicts differ. Limits are declared once per side and **named in the test** so raising one without the other breaks loudly.
- **SQL injection**: EF Core parameterises; no `.FromSqlRaw`/interpolated SQL is permitted in M3, and the first such call would need an ADR. `NOT NULL`/`CHECK` are the last line, not the first.
- **Secrets**: connection string only ever in `dotnet user-secrets` or environment; `appsettings.Development.json` carries no credential; **no secret exists in CI** (the test container fabricates its own); `pk:commit`'s leak scan covers `api/**` too.
- **PII**: job applications are inherently personal (employer, role, notes). It stays **local** in M3; M5's deployment must treat the DB as containing personal data (encryption at rest, no logs of `notes`).
- **DoS/rate limiting**: explicit non-goal *because* nothing is publicly reachable; the honest note is that this becomes a real requirement the moment M5 exposes a port.
- **Request size**: bounded by the JSON body limit (`notes` has a max length client-side; the server sets an explicit limit rather than inheriting an unbounded default).

### FMEA Resilience Matrix

| Failure scenario | Prob. / Sev. | Detection | Mitigation / fallback | Recovery |
| :--- | :--- | :--- | :--- | :--- |
| PostgreSQL container down / unreachable at request time | Med / High | `200→503` in API logs; `unavailable` in the client | Catalog catches the connection fault → `503 unavailable`; provider's **refusal gate** engages (already built, M2a) | Operator restarts the container; UI recovers on the next successful read, no client state to clear beyond the error |
| **Concurrent edit** — two tabs, same record, both save | **High / Med** (M2b made this reachable; today it is silent last-write-wins) | `409` + `conflict` code, visible in the API log | `xmin`/`If-Match`; refusal message; **no auto-merge** — a merged application record would be a lie | User reloads, re-applies the change deliberately |
| **Re-read lands out of order** (P2-2, handed from M2b and **owned by M3**) | Med / Med | a stale list flashing after a newer one; contract test with a delayed first response | monotonic sequence per `list()`; older responses dropped | The next change re-reads |
| `POST` retried after a timeout | Med / Low | duplicate id → `409`, surfaced as `conflict` | idempotent create (`DECISION-007`, **amended — gap 9**): the duplicate is refused and no second row exists; the adapter **does not** silently re-read (test §17) | none needed |
| SSE stream dropped (proxy timeout, laptop sleep) | High / Low | `EventSource` `error` then `open` | browser reconnects; adapter re-reads once on re-open | self-healing; degrades to manual refresh if the API is down |
| Slow SSE consumer (client cannot drain) | Low / Med | server-side channel backlog | bounded channel; on overflow **drop that subscriber** rather than block writers — a missed notification is a re-read, a blocked writer is an outage | client reconnect + re-read |
| Body that does not match the contract (200 OK, wrong shape) | Low / High if silent | boundary parse fails | `corrupt-data` refusal; **never** partial-trust the object (same rule M2a applies to `event.newValue`) | operator fixes server; user reloads |
| Migration applied to a non-empty database | Low / High | `dotnet ef database update` output; CI proves the empty path | M3's migrations are pure-additive by design; anything destructive needs an Expand-Contract entry in §4.2 first | restore dev container |
| API test job cannot get Docker on the runner | Med / Low (CI only) | first CI run of Slice 0 | `UNCERTAINTY-008-001`'s fallback: GitHub service container | swap the fixture, tests unchanged |
| Clock skew between app and DB (`updated_at`) | Low / Low | — | server-generated timestamps only; `updated_at` set in the DB, never sent by the client | n/a |

## 6. Conditional Implementation Milestones

Task Record `TDD Enforcement Mode` **proposes `enabled`** (canonical owner: the Task Record). Behaviour IDs continue the project sequence — `001-025` are used, so **M3 starts at `BEHAVIOR-026`**. Each Red names its failing assertion and the command that shows it.

**Slice 0 — toolchain, scaffold, and CI (Configuration/Documentation Work — no Red/Green ladder)**
*The install happens here, after this spec is approved — not before.* `dotnet-install.sh --channel 10.0 → ~/.dotnet`; `dotnet new webapi -minimal`; solution under `api/`; `docker compose` file for PostgreSQL 18.6 with loopback-only publishing; `.gitignore` additions (`bin/ obj/ user-secrets`); CI gains a job with `paths: api/**` and its own `dotnet build && dotnet test`, plus a check that **`vite.config.js` never appears** (DEBT-11's guard, extended to the new tree). Evidence: `dotnet --version` = the pinned number; a failing `dotnet test` proves the job is wired to something real, not decorative; one CI run red then green proves path filters behave.

**Slice 1 — schema and reads (TDD)**
- `BEHAVIOR-026` empty list returns `[]`, not `404` · Red: `GET /api/applications` on an empty DB must assert `200` + `[]` · Green: migration + `ListAsync`
- `BEHAVIOR-027` a stored row round-trips every field, including `null` location/notes and `applied_at` as `yyyy-MM-dd` · Red: assert the JSON equals the seeded entity (the timezone trap is caught here, in a test, not in production)
- `BEHAVIOR-028` `GET …/{unknown-id}` → `404` with `code:"not-found"` · Red
- `BEHAVIOR-029` list results carry an `ETag`/`revision` that changes after a write · Red

**Slice 2 — writes and constraints (TDD)**
- `BEHAVIOR-030` create persists to the database and is visible to a **second** HTTP client (the durability claim that `localStorage` could never make)
- `BEHAVIOR-031` server rejects the same records the client rejects, with `errors[].pointer` naming the right field
- `BEHAVIOR-032` status outside the five is rejected **by the database as well as by the validator** (constraint proven real, not advisory)
- `BEHAVIOR-033` stale `If-Match` → `409 conflict`, and **the row is unchanged** (the assertion that matters: a conflict response that still wrote would be the worst bug in the milestone)
- `BEHAVIOR-034` retried `POST` with the same id → `409` **(amended, gap 9: the adapter surfaces `conflict`; the ratified guarantee is that no duplicate row exists — re-read-and-succeed was never built and `-037` asserts the throwing behaviour)**
- `BEHAVIOR-035` `DELETE` → `204`, then `404` on re-read

**Slice 3 — the frontend swap (TDD, and the honest test of "no page changes")**
- `BEHAVIOR-036` with `VITE_API_BASE_URL` set, the store yields an HTTP adapter; unset, the local one; **both satisfy the same type** (a compile-time assertion, plus a runtime one)
- `BEHAVIOR-037` every problem response maps to the documented `RepositoryError` variant — **table-driven over the whole mapping table, including the "unknown code ⇒ corrupt-data" row**
- `BEHAVIOR-038` `fetch` rejection (server down) → `unavailable`, and the provider's refusal gate behaves as M2a's tests say it should
- `BEHAVIOR-039` **out-of-order re-read guard** (P2-2 closed): first response delayed, second resolves first, and the delayed older one must not repaint
- `BEHAVIOR-040` `subscribe()` over SSE calls the callback on a `change` event, and re-reads once after a simulated reconnect
- `BEHAVIOR-041` `notes` and `companyName` survive unicode + long strings byte-for-byte through JSON (the encoding question M2a's `JSON.parse` discipline anticipated)

**Slice 4 — real-browser acceptance (the harness becomes the gate)**
- `BEHAVIOR-042` the CDP harness, pointed at the API-backed build, passes its cross-tab checks **over HTTP in Chromium** — the same script that retired DEBT-01's residual today, now run with `VITE_API_BASE_URL` set. This is the milestone's most valuable single artefact: an acceptance test that no jsdom run can fake.
- `BEHAVIOR-043` a record created in the browser is present in the database after the browser is closed and reopened (persistence proven *outside* the client process)

**Sign-off readiness**: this spec approved by Lead Engineer (including `DECISION-006`'s widened union, `001`'s patch pin, `008`'s proposed stack) → `pk:tasks` mints `TASK-m3-backend-api` with `TDD Enforcement Mode` and the `BEHAVIOR-*` ladder → `pk:grill` before the first line of C# → Slice 0 → PR #8. **No implementation is authorised by this document**, and `pk:plan` changes no execution state.

## 7. Sign-off & Grilling Checklist

- [ ] Owner approves the four answered forks as *written down*, not as remembered
- [ ] Owner explicitly approves the **eighth `RepositoryError` variant** — the only change here that widens a locked M2a invariant
- [ ] Owner approves pinning **PostgreSQL 18.6** while `19 Beta 3` exists, and approves `.NET 10 / SDK 10.0.401`
- [ ] Assumptions 001-003 accepted with their validation actions (they are the parts of this plan we chose not to look up)
- [ ] Grilling (`pk:grill`) challenges at least: `text + CHECK` vs `ENUM`; client-minted ids; whether an opaque `revision?: number` on the domain type is "leaking transport into the domain" (my answer: it is a row version, and no page may read it — the field is the honest place to put it, and I expect that to be attacked); `date` vs `timestamptz`; single-instance SSE fan-out; whether keeping the local adapter is a bridge or a fork in the product
- [ ] The measurable targets in §2 are falsifiable by a command; if any cannot be, it is deleted rather than defended
- [ ] STATE.md §2 records that **M4 must precede any public exposure of this API**

### What this spec could not verify, in one place
RDS engine availability for 18.6 (Assumption 001) · Npgsql 10.0.3 ⇄ EF Core 10.0.12 compatibility, inferred from matching majors (Uncertainty 003) · `EventSource` semantics in current Chromium and SSE through a future proxy (Uncertainty 005) · Docker availability on CI runners (Uncertainty 008) · test-stack versions, deliberately unpinned (Decision 008 `proposed`). Five unknowns, each with an owner, an impact, and a validation action — **none of them presented as fact.**
