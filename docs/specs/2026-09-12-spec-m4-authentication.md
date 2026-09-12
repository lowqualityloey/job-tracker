# M4 — Authentication: Session Cookies, Ownership, and the SSE Constraint

## Planning Record (PromptKit Adaptation)

### Planning Record Metadata

| Field | Value |
| :--- | :--- |
| Spec ID | `SPEC-m4-authentication` |
| Date | 2026-09-12 |
| Level | **2 (Controlled)** — auth, schema migration, and a contract change on a locked invariant |
| Planning depth | **Full** |
| Canonical task record | `docs/tasks/TASK-m4-authentication.md` — **not yet created; `pk:tasks` runs only after this spec is approved** |
| Upstream | `SPEC-m3-backend-api` (`main` `29626a9`) — 14/14 ACs, behaviour ladder `-026`…`-046` |
| Status | **Proposed — awaiting Lead Engineer** |

### Planning Inputs

M2a's `RepositoryError` union (frozen at seven, widened to eight by `DECISION-m3-backend-api-006`), M2b's single-user
stance, M3's §7 ordering constraint (**M4 must precede any public exposure of this API**), grill **Q14** (client-minted
ids are a single-user design) and **Q16** (per-process SSE fan-out), and **gap 12** — the live `500` on a malformed `id`,
which M3's close-out assigned to M4.

### Carried discipline from M3, applied before it is needed

M3's five documentation-integrity findings are encoded here rather than re-learned later:

1. **Every measurable target names the command that produces it, and the command is run before the number is written.**
   (§2.8's latency targets sat unrun for 126 commits; its SSE clause was wrongly called unfalsifiable because a line was
   read through `cut -c1-230`.)
2. **Each contract element states which artifact carries it — header, body, or neither — and the test asserts that
   artifact.** (`-029`'s "carries an `ETag`/`revision`" let a body-only implementation satisfy a header-shaped promise:
   **a test asserting *A or B* cannot detect the absence of *A*.** Cookie attributes are a `Set-Cookie` **header**; the
   user id is a **body** field. They are not interchangeable.)
3. **Baseline-carrying deltas are captured in Slice 0, not "later".** (§2.8 told Slice 3 to record the bundle number;
   Slice 3 didn't, and the `< 2 kB` delta was uncomputable until it was reconstructed from an old commit. Slice 0 below
   captures the pre-M4 bundle baseline as an explicit behaviour.)
4. **Client input is validated at the binder, not the type.** (Gap 12: `Guid Id` on a request DTO throws during
   deserialization, before the validator exists. M4's endpoints add `owner_id`, session ids, and credentials — all of
   which arrive as untrusted strings.)
5. **jsdom has no `EventSource` and enforces no origin policy.** Two of M4's hardest properties (SSE with cookies;
   cross-site rejection) are **invisible to the entire frontend suite** and must be browser-proved.

---

## Assumption Records

| ID | Assumption | Validation action | If false |
| :--- | :--- | :--- | :--- |
| `ASSUMPTION-m4-auth-001` | Chrome's localhost secure-context allowance lets a `Secure` cookie be set over `http://localhost`, so `__Host-` works in dev without TLS | Prove it in the M4 browser harness, not in jsdom | Dev uses a distinct cookie name → **the name differs between environments, which is exactly how tests drift from production** |
| `ASSUMPTION-m4-auth-002` | Single user for M4's lifetime; registration is out of scope | Owner confirms | A `users` table with one row is wrong shape; multi-user needs `pk:data` rework |
| `ASSUMPTION-m4-auth-003` | No rate limiter in M4 (M3's non-goal holds) because deployment is M5 | Owner confirms | A public login endpoint with a 600 ms hash is a cheap DoS; needs `Microsoft.AspNetCore.RateLimiting` |

---

## Technology and Vendor Decision Records

### DECISION-m4-auth-001 — Credential format: **session cookie, not Bearer token** · **Owner decision required, and it is forced**

The naive version of this milestone is "add JWT". **M3's own SSE design forecloses it**: browsers give `EventSource` **no
way to set an `Authorization` header** — the constructor takes `withCredentials` but no headers. So a Bearer design would
need one of: token-in-query-string (leaks into logs and `Referer`; refuse), a `fetch` + `ReadableStream` reader replacing
`EventSource` (rewrites `-040`'s adapter and loses automatic reconnect), or dropping SSE entirely (loses cross-tab).

| Option | Consequence |
| :--- | :--- |
| **(A) `__Host-` prefixed HttpOnly session cookie, server-side sessions** *(recommended)* | SSE keeps working unchanged with `withCredentials`; **revocable**; no token expiry UX; forces CSRF work (006) |
| **(B) Bearer + `fetch`-stream SSE reader** | Rewrite of the SSE adapter and its reconnect story; token must live in JS (**XSS-readable**); no revocation without a denylist — which is a sessions table with extra steps |
| **(C) Bearer + drop SSE** | Removes the feature M3 built `-040`/`-042`/`-046` and gap 10b's measurement to prove |

**Recommendation: (A).** Note the reversal worth stating plainly: **JWT is the fashionable answer and the wrong one here** —
server-side sessions give revocation *and* the only credential form `EventSource` can carry, at the cost of CSRF work.
*Chosen: — · Rejected alternatives: — · Consequences: —*

### DECISION-m4-auth-002 — Identity: **two tables, not ASP.NET Core Identity** · **Owner decision required**

Identity's EF stack drags ~7 tables (`AspNetUsers`, claims, logins, roles…) and a package set, in exchange for features M4
will not use (recovery, external providers, roles). **The learning-first answer is also the smaller one:** `users(id, email,
password_hash, created_at)` + `sessions(id, user_id, created_at, expires_at, revoked_at)`, and `PasswordHasher` from
`System.Security.Cryptography`. **The deletion test:** if we deleted ASP.NET Identity, complexity would concentrate; adding
it would *move* complexity behind an abstraction we'd have to learn to debug. Risk: we own hashing policy — mitigated by
003 storing cost parameters per-hash.

### DECISION-m4-auth-003 — Password hashing: **PBKDF2 via `Rfc2898DeriveBytes`, cost stored in the envelope**

No new runtime dependency (the count has held at **3 since M0**). Envelope format, self-describing so cost can rise
without a migration:

```
pbkdf2$sha512$<iterations>$<salt-b64>$<hash-b64>
```

`FixedTimeEquals` on the derived bytes; **iterations chosen by measurement, not folklore** — see §2.3, whose target is a
*bounded* hash cost (a login that takes 4 s is a DoS; one that takes 4 ms is a GPU's friend). Argon2id is stronger and is
**not taken**: it needs a native dependency, and the honest note is that this is a single-user local tool whose M5
exposure plan is the real trigger to revisit.

### DECISION-m4-auth-004 — Ownership: **`owner_id` + client-minted ids stay, and non-owned rows read as 404, never 403**

Client-minted ids survive (grill Q14, gap 12) because they **are** the idempotency key `DECISION-007` relies on; dropping
them breaks offline create. With authentication, though, a guessed UUID stops being academic. So: `applications.owner_id
UUID NOT NULL`, every query scoped by it, and **a non-owned row answers `404 not-found`, not `403`** — a `403` is an
oracle confirming the id exists. **Gap 12's fix lands here**: `string Id` on the request DTO, `Guid.TryParse` inside the
validator, `errors[].pointer: "/id"`, Red being the 500→400 flip on `{ id: "not-a-guid" }`.

Migration is **Expand–Contract** — never a single instantaneous `ADD COLUMN … NOT NULL`:

| Phase | Change | Ships |
| :--- | :--- | :--- |
| 1 Expand | `owner_id UUID NULL`; backfill `UPDATE … SET owner_id = <the single user>` | Yes, app tolerates NULL |
| 2 Assert | `ALTER … ALTER COLUMN owner_id SET NOT NULL` **after** the count of NULL rows is verified 0 | Yes |
| 3 Scope | Repository/catalog filter by owner; unauthenticated → 401 | Yes, same release |

### DECISION-m4-auth-005 — The **ninth** `RepositoryError` variant: `unauthorized` · **Owner decision required**

M2a froze the union at seven; `DECISION-006` widened it to eight and that widened a locked invariant on an owner's
explicit yes. **This does it again**, so it gets its own row rather than riding along: `{ code: 'unauthorized' }`, plus the
provider's behaviour — **clear the draft-free session state, redirect to login, and preserve the intended URL** so
post-login return works. Reusing `forbidden` or `unavailable` would be wrong: the first says "authenticated but not
allowed", the second says "server unreachable, retry" — and a retry loop against a 401 is a lockout generator.

### DECISION-m4-auth-006 — CSRF: **`SameSite=Lax` + a login-issued antiforgery header required on writes** · **Owner decision required**

The unavoidable cost of 001(A). `Lax` blocks cross-site *POSTs* but permits top-level cross-site *GETs*, which is safe
because M4 has **no state-changing GETs** — a rule this spec states so the next endpoint doesn't break it silently. The
antiforgery token (HMAC over the session id, delivered at login, echoed in `X-CSRF-Token`) covers the `Lax` gap and is
**cheap to test with a cross-site probe from a different origin in the browser harness** — which jsdom cannot do at all.

---

## 1. Executive Summary & Problem Statement

M3 made the data real, durable, and shared across tabs. It also made it **world-readable to anyone who can reach the
socket**, and its `applications` rows belong to *nobody*. The API binds loopback today only because M5 hasn't put it
anywhere else, and M3's §7 makes **M4 precede any public exposure**.

The hard part is not password hashing. It is that **M3's own features constrain M4's design**: `EventSource` cannot carry
a bearer token, so the credential must be a cookie; cookies demand CSRF work; and client-minted ids — the mechanism
`DECISION-007`'s idempotency depends on — become an attack surface the moment rows have owners. M4 is therefore a
**session-and-ownership milestone**, not a login-page milestone.

## 2. Goals and Explicit Non-Goals

### Goals (In Scope)

**2.1** Every `/api/applications*` route except `/api/auth/login` requires a valid session; unauthenticated → **401**
with `{ code: 'unauthorized' }`.
· *Command:* `curl -s -o /dev/null -w '%{http_code} %{content_type}\n' http://127.0.0.1:5080/api/applications` →
expected `401 application/problem+json`. **Also run it on `…/events`** — the SSE route is the one a filter forgets.

**2.2** Session cookie is `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, name **`__Host-JTSession`**.
· *Command:* `curl -i -s -X POST …/api/auth/login -H 'content-type: application/json' -d @creds.json | grep -i '^set-cookie'`
· **Asserted as a header, by test and by hand** — this is discipline 2's whole point, and the browser, not jsdom, is what
will actually enforce the `Secure`/`__Host-` semantics.

**2.3** Password hash cost is **measured and bounded**: `Hash` p95 **< 1000 ms**, `Verify` p50 **200–800 ms** on this
machine, iterations recorded in the envelope.
· *Command:* an xunit `[Fact]` that times 25 iterations and asserts the band — **the assertion is the test**, so the cost
cannot silently drift with a runtime upgrade.

**2.4** No user enumeration: a wrong email and a wrong password are **indistinguishable** in body and within **5 ms** in
timing.
· *Command:* `dotnet test --filter ~EnumerationResistance` — 50 responses each, Mann–Whitney or a documented median bound,
and **a dummy hash verify when the user is absent** (the classic tell isn't the message, it's the missing 400 ms).

**2.5** SSE still delivers cross-tab updates **while authenticated**, in a real browser.
· *Command:* the M3 harness, `docker exec … node /srv/httpCrossTab.mjs` → **7/7**, with cookies. **jsdom cannot test
this** (discipline 5): no `EventSource`, no origin policy.

**2.6** A cross-site POST without `X-CSRF-Token` is rejected.
· *Command:* browser harness from a second origin, plus `curl` with cookies and no token → expected `400`/`403` —
**and the harness must prove the same request *with* the token succeeds**, or a broken endpoint passes.

**2.7** Pre-M4 bundle baseline captured **in Slice 0**, so the login route's delta is computable at the end
(discipline 3).
· *Command:* `git worktree add /tmp/m4base main && VITE_API_BASE_URL=… npx vite build && cat dist/assets/*.js |
gzip -c | wc -c`, recorded verbatim in the task record. **Target: login route adds < 3 kB gzipped.**

**2.8** All **65** M3 API tests still pass — now against an authenticated fixture, with none skipped.
· *Command:* `dotnet test` → `Passed: 65, Failed: 0, Skipped: 0`. **Read `Skipped` too** (M3 lost an hour to a filter
that matched only part of a `describe`).

### Non-Goals (Explicit Scope Boundary)

Registration, password reset/recovery, email verification, MFA, external providers (OAuth), **roles and tenancy** (this is
owner-scoped single-user, `ASSUMPTION-002`), logout-everywhere, token refresh (there is no token), rate limiting
(`ASSUMPTION-003`), and any AWS/IAM work (M5).

## 3. Architecture & System Context

```
Browser (Vite, http://localhost:5173 / :4173)
  │  cookie: __Host-JTSession   ·  X-CSRF-Token on writes
  ▼
ASP.NET Core minimal API ── AuthenticationMiddleware (session handler)
  │        [Authorized] on /api/applications*  ·  /api/auth/* anonymous
  ├── AuthService      (hash · verify · issue/revoke session)
  ├── IPasswordHasher  (PBKDF2, self-describing envelope)
  └── ApplicationCatalog ──► every query scoped by owner_id
                                   │
                              PostgreSQL (users · sessions · applications)
```

**Seams.** The frontend seam is `RepositoryError`'s ninth variant + one new route; **no page changes** except a login
screen — `src/pages|components|state` stays M3's untouched zone, and if that breaks, it is stated as a decision, not a
surprise. The API seam is a `SessionAuthenticationHandler` (deliberately **not** Identity) plus an owner-scoping layer in
`ApplicationCatalog`. **Test surface:** `WebApplicationFactory` with a `Login()` helper returning a client holding cookies —
the existing fixture already shares one server across tests, so sessions must be per-test-client, and **`-046`'s stream
tests need the subscription to be authenticated too.**

## 4. Detailed Design & Contracts First

### 4.1 Data Model

`users(id UUID PK, email CITEXT UNIQUE NOT NULL, password_hash TEXT NOT NULL, created_at TIMESTAMPTZ DEFAULT now())` ·
`sessions(id UUID PK, user_id FK CASCADE, created_at, expires_at, revoked_at NULL)` ·
`applications` gains `owner_id UUID NOT NULL REFERENCES users(id)` (expand–contract per 004), **indexed**
(`CREATE INDEX … ON applications (owner_id)`) — and with the index, **`EXPLAIN` must show it used**, since an unowned
full scan that filters in C# is the bug that hides at 36 rows and appears at 100 k.

### 4.2 Migration Plan — Expand–Contract, in that order

1. **Expand:** create `users`, `sessions`; add `applications.owner_id NULL`; backfill to the seeded user.
2. **Assert:** verify `SELECT count(*) FROM applications WHERE owner_id IS NULL` → **0**, *then* `SET NOT NULL` + index.
3. **Scope:** authorised catalog; 401 default.
4. **Seed:** one user, email and password **from configuration, never a literal**, and **refuse to boot** with default
   credentials in `Production` — a startup guard, because the failure mode of a seeded account is a published one.

### 4.3 API Contracts — **stating the artifact for each element** (discipline 2)

| Endpoint | Request | Success | Auth | Errors |
| :--- | :--- | :--- | :--- | :--- |
| `POST /api/auth/login` | `{ email, password }` | `204` + **`Set-Cookie`** header (`__Host-JTSession`; attributes are header-only, assert them there) | anonymous | `401 unauthorized` (one body for both wrong-field cases) · `400 validation` |
| `POST /api/auth/logout` | — | `204` + `Set-Cookie:` expiry + `revoked_at` | authorized | — |
| `GET /api/auth/session` | — | `200` `{ id, email }` body | authorized | `401` |
| `GET/POST/PUT/DELETE /api/applications*` | unchanged, **plus `X-CSRF-Token` on writes** | unchanged | **authorized** | `401 unauthorized` · `404 not-found` for non-owned ids (never `403`) · `400 validation` incl. **malformed `id` (gap 12)** |

**Contract rules this milestone exists to enforce:** no state-changing `GET` ever (`SameSite=Lax` depends on it); the
session id is **regenerated on login** (no fixation); a `401` body is identical for wrong-email and wrong-password;
**`/events` is authorised**, and the `401` on it is what the browser sees as an `EventSource` error — so the frontend must
treat an SSE failure with a `401` session probe rather than retrying forever.

## 5. Security, Privacy & Failure Modes (FMEA)

| Failure scenario | Prob / Sev | Detection | Mitigation | Recovery |
| :--- | :--- | :--- | :--- | :--- |
| Credential over plaintext HTTP in production | Med / **High** | `__Host-` requires `Secure` → cookie silently absent → **every API call 401s** | Startup guard + §2.2's `curl -i` assertion | Fix the redirect, not the code |
| Session fixation | Low / High | session id unchanged across login | **Regenerate on login** (a test, not a comment) | Revoke old session |
| CSRF via `Lax` top-level GET | Low / Med | cross-site probe harness | No mutating GETs + antiforgery header (006) | — |
| User enumeration via timing | **High** / Med | §2.4's measurement | Dummy verify on absent user + identical bodies | — |
| Brute force / DoS on `/login` | Med / Med | none in-app (no limiter) | Single user + loopback; **M5 gate** | Rate limiter then |
| Session never expires (no `expires_at` enforcement) | Med / High | `GET /api/auth/session` after the window | Sliding expiry + revocation on logout | — |
| XSS → cookie theft | Low / High | not testable here | `HttpOnly`; CSP out of scope, **named as a gap** | — |
| `owner_id` backfill against non-empty DB | Low / **High** | Migration 2's NULL count assertion | Expand–contract; assertion **before** `SET NOT NULL` | Restore the dump M5 documents |
| **SSE route left anonymous by an attribute typo** | Med / High | §2.1's command includes `/events` | Explicit test on the stream | — |
| Gap 12's malformed `id` reaches Npgsql | **High** / Med | `curl -d '{"id":"not-a-guid"}'` → **500 today** | `string Id` + `TryParse` in the validator, Red = 500→400 | — |

## 6. Conditional Implementation Milestones

Task record `TDD Enforcement Mode`: **`enabled`** (canonical here). Behaviour IDs continue the project sequence at
**`-047`**.

**Slice 0 — pre-M4 baseline capture (Configuration/Documentation — no Red/Green).** §2.7's worktree, recorded verbatim.
*This slice exists because M3 assigned a baseline to Slice 3 and Slice 3 never took it.*

**Slice 1 — hashing and users (TDD).** `-047` envelope round-trips · `-048` cost band asserted by measurement (2.3) ·
`-049` `FixedTimeEquals` · `-050` seeded user + **refuse to boot on default credentials in Production**.

**Slice 2 — sessions and login (TDD).** `-051` login sets the `__Host-` cookie **with all four attributes asserted on the
header** · `-052` session regenerated (pre-login id never valid again) · `-053` 401 bodies and timings
indistinguishable (2.4) · `-054` logout revokes server-side, not just the cookie.

**Slice 3 — scoping and the M3 surface (TDD).** `-055` every M3 route 401s anonymously, **including `/events`** ·
`-056` owner scoping: user B reads A's id as `404` · `-057` **gap 12**: `{"id":"not-a-guid"}` → `400` with
`pointer:"/id"` (Red is today's `500`) · `-058` all 65 M3 tests green under auth, `Skipped: 0` · `-059` index used,
`EXPLAIN` asserted.

**Slice 4 — frontend (TDD).** `-060` ninth variant `unauthorized` mapped, and **the table-driven `-037` pattern copied:
every problem code → exactly one variant**, so a new code cannot pass unobserved · `-061` login route + intended-URL
return · `-062` SSE `401` handled as a session probe, not an infinite retry.

**Slice 5 — CSRF and browser acceptance.** `-063` cross-site probe without token rejected **and the same call with it
accepted** · `-064` `httpCrossTab.mjs` **7/7 while authenticated** (the real-browser proof, jsdom cannot attempt it) ·
`-065` bundle delta vs Slice 0's baseline, from the build output.

## 7. Sign-off & Grilling Checklist

- [ ] **Owner decides `DECISION-001` (cookie vs Bearer) as written down.** This one is load-bearing: **`EventSource`'s
      missing header option, not taste, is doing the work**, and it is the sort of constraint that reads as trivia until
      someone tries to bolt Bearer onto `-040` a year later
- [ ] **Owner approves the ninth `RepositoryError` variant** — the second widening of M2a's locked invariant, and it
      should be as deliberate as `DECISION-006` was
- [ ] Owner approves `DECISION-002` (two tables over ASP.NET Identity) and `003` (PBKDF2 with no new dependency, Argon2
      **deferred with a named trigger: M5 exposure**)
- [ ] Owner confirms `ASSUMPTION-001` (localhost `__Host-`) via the browser harness, **002** (single user), **003** (no
      limiter yet)
- [ ] Grilling (`pk:grill`) must challenge at least: cookie vs Bearer · server sessions vs JWT · `404` vs `403` on
      non-owned rows · PBKDF2 vs Argon2 vs Identity's default · whether **keeping client-minted ids is defensible after
      all** · `SameSite` choice · whether "no new dependency" is a real constraint or a habit · **and the enumeration
      target's statistical method** (a 5 ms bound on 50 samples can be noise)
- [ ] **Every §2 target above has a command, and each command was run — or its number is deleted rather than defended.**
      M3 shipped an unrun target and, worse, replaced a ratified one on a truncated read; the fix is not care, it is that
      *the command is written next to the number*
- [ ] `docs/STATE.md` §2 flips M3 to `[x]` **when the owner signs M3's §7**, and records M4 in progress
- [ ] **M5 carries the gates M4 names but does not close:** login rate limiting, CSP, `Secure`-only enforcement,
      cross-instance SSE fan-out via PG `NOTIFY` (grill Q16)

### What this spec could not verify, in one place

Whether Chrome accepts `__Host-` over `http://localhost` (001's assumption, browser-testable only) · the real hash cost on
this machine (Slice 1 measures it; **no number is asserted before the run**) · whether 50 timing samples can distinguish a
400 ms missing verify at the achievable noise floor · the true Identity table count · `CITEXT` availability in the pinned
PostgreSQL 18.6 image · whether PBKDF2 at a bounded cost is defensible for a *deployed* password (M5's question, not
M4's).
