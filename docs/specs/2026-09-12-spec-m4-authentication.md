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

> **⚠️ `ASSUMPTION-m4-auth-001` is FALSE — proven 2026-09-12 17:37 UTC, in the browser the assumption named, not in jsdom.**
> Full measurement in [`docs/spikes/2026-09-12-host-prefix-cookie-jar.md`](../spikes/2026-09-12-host-prefix-cookie-jar.md).
> In Chromium 128 (the `jt-bridge` build `-064` would use), over `http://127.0.0.1` the page reports
> `isSecureContext === true` **and accepts a `Secure` cookie**, but **discards an attribute-identical `__Host-` cookie** —
> and `__Secure-` with it. The same `__Host-` cookie **is** accepted over `https://127.0.0.1` with a self-signed cert.
> **So the two rules are different rules:** the `Secure` *attribute* is gated on **secure context**, where loopback is
> allowed; the `__Host-`/`__Secure-` *prefix* is gated on the **scheme being cryptographic**, where it is not. The assumption's
> premise was measured true and its conclusion false, and it was the inference between them that was the error.
>
> **Its own "if false" column is therefore the live problem, not the escape hatch**: the listed fallback — a distinct dev
> cookie name — is the one move the mitigation column simultaneously forbids, because `AuthCatalog.cs:26` holds the name as a
> `const` and the `Secure` in the emitted header is a literal, *precisely* so that dev cannot drift from production. The real
> consequence is that **`-064`/AC-12 cannot run over plain HTTP at all**, and `DECISION-m4-auth-007` is amended below to
> require **HTTPS in dev**. Nothing in the product changes either way: the emitted header is already correct, which is
> exactly why `-051`'s header seam passed and why no test before this one could see the difference between *sent* and *kept*.

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
> **Chosen: (A) — `__Host-` prefixed HttpOnly session cookie with server-side sessions.**
> **Rejected:** **(B)** Bearer + a `fetch`/`ReadableStream` reader — rewrites `-040`’s adapter and its automatic
> reconnect; **(C)** Bearer + dropping SSE — removes the feature M3 built `-040`/`-042`/`-046` to prove.
> **Consequences:** CSRF work becomes mandatory (`DECISION-006`, Slice 5’s `-063`); CORS must send `AllowCredentials`
> **with a specific origin, never `*`**; sessions become server-side state that must expire and be revocable; and
> **the browser, not jsdom, is the only place this credential format can actually be proven.**
>
> **Approval provenance:** filled 2026-09-12 04:30 UTC as **inferred from the merge of PR #34 (`fa4ed7d`)**, which
> landed before this field was written. §7’s owner checkboxes remain physically unchecked in `main`. **Dissent costs
> one commit** — but the field should not read as undecided when the process has decided it.

> **Grill amendment Q1 (2026-09-12, [`reviews/2026-09-12-m4-plan-grill.md`](../reviews/2026-09-12-m4-plan-grill.md)) — the conclusion stands, the argument was overstated.** `EventSourceInit { withCredentials?: boolean }` is now measured from `lib.dom.d.ts:688`, so the header limitation is fact. But "Bearer is impossible" is not: a `fetch` + `ReadableStream` reader *can* send a header. The accurate justification is **keeps `-040`'s adapter, reconnect and frame parsing intact**, not "no alternative exists" — and a decision resting on a false impossibility would not be trustworthy the next time a real constraint arrives.

### DECISION-m4-auth-002 — Identity: **two tables, not ASP.NET Core Identity** · **Owner decision required**

Identity's EF stack drags ~7 tables (`AspNetUsers`, claims, logins, roles…) and a package set, in exchange for features M4
will not use (recovery, external providers, roles). **The learning-first answer is also the smaller one:** `users(id, email,
password_hash, created_at)` + `sessions(id, user_id, created_at, expires_at, revoked_at)`, and `PasswordHasher` from
`System.Security.Cryptography`. **The deletion test:** if we deleted ASP.NET Identity, complexity would concentrate; adding
it would *move* complexity behind an abstraction we'd have to learn to debug. Risk: we own hashing policy — mitigated by
003 storing cost parameters per-hash.

> **Grill amendment Q2 — scope narrowed.** This rejection was of Identity's **EF stores** (~7 tables, and the features `ASSUMPTION-002` excludes). It was read during the grill as also rejecting Identity's **hasher**, which is a separable half with a zero-package cost. See 003.

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

> **Grill amendment Q2 — the premise of this decision is FALSE and the decision is REVERSED.** The "no new runtime dependency" argument is an **npm** count (3, `src/**`) being used to decide a **backend** question; the API already carries **5** `PackageReference`s. Worse, `Microsoft.Extensions.Identity.Core.dll` — which *is* `PasswordHasher<TUser>` — **ships in the ASP.NET Core shared framework**, and this project is `<Project Sdk="Microsoft.NET.Sdk.Web">`, so it needs **zero** packages to use.
>
> **Revised choice: use `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>`**, with `PasswordHasherOptions` iterations raised explicitly rather than inherited, and note that its `{PRF$iterations$salt$subkey}` envelope is **already self-describing** — the upgradeability property this decision claimed to want from a hand-rolled format. Hand-parsing crypto envelopes is the actual risk, and it was adopted to honour a number from the wrong layer.
> **What survives:** Argon2id remains deferred with the same named trigger (M5 exposure). **What this is a lesson in:** bundling a correct rejection (the stores) with an adjacent one (the hasher) and letting the second inherit the first's justification unexamined.

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

### DECISION-m4-auth-005 — The **ninth** `RepositoryError` variant: `unauthorized` · **Owner decision required — GIVEN, 2026-09-12**

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

> **Note at execution (`-063`, 2026-09-12 ~21:00 UTC), appended rather than replacing the paragraph above — and it
> deviates in one place, deliberately.** The token is **not** a hand-rolled HMAC: it is the session id run through
> ASP.NET Core's data-protection stack (`CreateProtector("…Antiforgery.v1").Protect`), which is authenticated *and*
> encrypted, keyed by the framework's ring. The property this decision asked for is unchanged — unforgeable without
> server key material, bound to the session — and no new secret has to be configured per environment or guarded by a
> new boot refusal. Agent-decided, per the standing instruction, not owner-ratified.
>
> **Two things execution measured that the paragraph could not have known:**
>
>   · The framework's key ring is **ephemeral per process by default**. Nothing is shared "for free": two instances,
>     or one restart, cannot read each other's tokens, and every write that lands on the wrong one is a `403`.
>     `OwnershipTests` proved it to itself by building a fresh test host per request — six of its own cases became
>     antiforgery refusals while the code under test was correct. Hence `-074`, and until then the deployed
>     behaviour is *a session may only be written to by the instance that issued it*.
>   · **The wire code is distinct (`antiforgery`); the client variant is not** — it maps to the existing
>     `unauthorized`, by this repository's own rule for merging (`Problems.cs`: codes share when the client's answer
>     does). The cost is on the record: a user mid-edit bounces to `/login` and loses the draft, and the two events
>     are distinguishable only in logs and devtools. A tenth `RepositoryError` was the alternative, and DECISION-005
>     is the precedent that a new variant has to earn its place.

---

### DECISION-m4-auth-007 — Browser harness origin: **serve the harness page from the API's own origin (grill Q4, option a)** · **GIVEN by the owner, 2026-09-12 16:46 UTC**

Grill **Q4** asked whether the harness's topology would make AC-12 fail for a reason that is not the product. It would:
`tests/browser/httpCrossTab.mjs` defaults to same-host/different-port — **same-site**, since Chromium's site is scheme +
host and **ignores the port** — but M3's real runs could not use those defaults, because the sandbox cannot route into the
Docker network. The built `dist` was served inside `jt-bridge` while the API stayed on the host, giving document host
`127.0.0.1` against API host `172.23.x.x`: **two different sites**, so `SameSite=Lax` drops the cookie on every request and
AC-12 reports "auth broke SSE" when nothing in the product is broken.

**Given: (a) serve the harness page from the API's origin** (static files + SPA fallback, environment-gated), over **(b)** a
Development-only reverse proxy, **(c)** `SameSite=None; Secure` in dev, and **(d)** shipping AC-12 unverified. The reason
matches this spec's own: **(c) would make the test environment more permissive than production**, and a green suite over a
looser policy is a fake.

**The cost is real and is not being smuggled past the reader — the grill named it in Q4's own text.** Under (a) the page and
the API are same-origin, so **no CORS preflight ever occurs**: the credentialed-CORS property (`-067`,
`Access-Control-Allow-Credentials` echoed with the exact origin, never `*`) **stops being exercised in the browser
entirely**. It does not become false — it becomes *unobserved at that seam* — and it stays proven where it can be proven:
`-067`'s **Integration half**, closed in `CorsCredentialsTests` against a real `WebApplicationFactory` host. That is exactly
the trade Q4 predicted ("buys the browser proof, costs the topology constraint"); (a) accepts it, because **AC-12's truth is
worth more than AC-16's browser redundancy.** Recorded so nobody later finds the gap and calls it an oversight.

**Consequence for the ladder**, appended here rather than rewritten into `-067`'s ratified row:

- **Unblocked by the answer:** `-064` (cross-tab SSE in Chromium → AC-12) and `-065` (login-route bundle delta → AC-14 —
  whose baseline is URL-literal-dependent, per Slice 0's finding that a 14-character IP moves the gzip sum by 9 B, so the
  run must **state which literal it built with** or the delta is a number about a build nobody ships).
- **Needs a restatement, not an execution:** `-067`'s **Browser half**, and **AC-11**'s *cross-site* framing in `-063`. A
  cross-site POST under `Lax` carries **no cookie at all**, so the antiforgery-header check is never reached — the request
  dies at the session gate as `401`, which is the `Lax` defence working and not the header defence. `-063` must therefore be
  written as **two distinct cases, each with its own positive control**: a same-site write without `X-CSRF-Token` (the header
  check bites) and a cross-site write (cookie absent → `401`). **Splitting a ratified row's promise is a design decision, so
  it goes to the owner before Slice 5 rather than being resolved quietly in code.**
- **Still gated on a different answer:** **AC-3** sits behind its own restrike-or-restate decision, **not** behind Q4. The
  claim in `checkpoint-001` §2/§5 and `docs/STATE.md` §3A that "all four open ACs sit behind Q4" was false — see the
  correction appended there, which also records that AC-13 is *verified* and AC-18 does not exist.

> ### 🔧 AMENDED BY MEASUREMENT, 2026-09-12 17:37 UTC — option (a) as written **cannot deliver `-064`**, and the answer needs one more decision
>
> Everything above about the **site** analysis is still true. What was missing is that same-origin also has to be a
> *storable* origin, and it is not: per `ASSUMPTION-001`'s falsification at the top of this file, **Chromium discards
> `__Host-JTSession` over plain `http` at any address, loopback included**
> ([spike](../spikes/2026-09-12-host-prefix-cookie-jar.md)). So "(a) serve the harness from the API's origin" removes the
> cross-site problem and replaces it with a larger one — the cookie is **never stored**, every authenticated request answers
> `401`, and `-064` fails while looking like a broken login rather than a missing certificate.
>
> **Same-origin was necessary. It is not sufficient.** The sufficient shape is **(a′): same-origin *and* HTTPS in dev** —
> `https://127.0.0.1:<port>` with `dotnet dev-certs https`, the `https` profile that already exists in
> `launchSettings.json`, or a locally-trusted cert the CDP harness bypasses via `Security.setIgnoreCertificateErrors`.
> Measured: over `https://127.0.0.1` with a **self-signed** cert, `__Host-JTSession` **is** accepted and carried. **The
> product emits exactly the same header in every case**, which is the point — (a′) is the only row of the option table that
> leaves AC-2's attributes untouched.
>
> **Two things this retro-actively corrects in the owner's own trade.** (1) Option **(c)**, `SameSite=None; Secure` in dev,
> was rejected as "more permissive than production" — right conclusion, **wrong mechanism**: it was never a candidate at all,
> because `None` governs *cross-site sending* and the measured failure is *storage* on a same-origin request. (c) would have
> changed nothing. (2) "One dev-server change" understated (a′): it needs a **certificate**, and the obvious mechanism is
> **measured unavailable here** — `dotnet dev-certs https` fails with *"error saving the HTTPS developer certificate to the
> current user personal certificate store"* and never reaches `--trust`. The live route is a PEM handed to Kestrel directly
> (`Kestrel__Certificates__Default__PemPath` / `KeyPath`), which the spike proves the **browser** accepts and the **server**
> half of which is unverified — verifying it means booting the app against the developer's own database, whose startup seed
> rotates the dev account password. **That is a decision, not a probe, so it is asked rather than taken.**
>
> **A fourth consequence, outside the harness:** `launchSettings.json`'s default `http` profile is
> `http://localhost:5039`, so **a human running `dotnet run` today gets a `204` from login and a browser that silently keeps
> no session.** Not measured against the real endpoint (the probes used a synthetic server emitting the same string), so it is
> stated as inference from an identical wire shape and labelled as such in the spike's "Not measured". If it holds it is a
> **development-experience defect in M4's own deliverable**, found by the measurement that was supposed to be about test
> topology — and it argues for (a′) on product grounds rather than harness ones.
>
> **And it no longer rests on inference:** the identical run against the **real endpoint** (below) gives `204` from login, an
> **empty cookie jar**, and `401` on the protected route over plain `http`.
>
> #### ✅ ANSWERED, 2026-09-12 ≈18:10 UTC — the owner chose **(a′)**, and the shape is now measured on the real app
>
> Direct answers to the refutation above, given with two others in one exchange:
>
> 1. **`(a′) same-origin + HTTPS in dev`** — over `(d)` ship-AC-12-unverified, over running `-065` first, and over `(b′)` a
>    TLS-terminating proxy. The recommendation stood for the reason it was worth making: it is **the only option that leaves
>    the shipped cookie attributes untouched.**
> 2. **Verify against a scratch database on the same server, dropped afterwards** — explicitly *not* `api-db-1`'s
>    `jobtracker`. Asked as its own question because the `-050b` seed rotates the Development bootstrap account's password on
>    boot, and a decision-support probe does not earn that mutation by silence. Outcome in
>    [the spike's follow-up](../spikes/2026-09-12-host-prefix-cookie-jar.md): **Kestrel loads a PEM from
>    `Kestrel__Certificates__Default__Path` + `KeyPath`** — `PemPath` is **not** a key, and using it produces the same
>    "no server certificate was specified" message as an absent cert store, two causes behind one symptom. Real login over
>    `https://172.23.124.252:5443` with a self-signed cert: `204` → `__Host-JTSession` **in the jar** (`secure=true
>    httpOnly=true`, `domain` with no leading dot, so host-only as `__Host-` requires) → **`200`** on the protected route.
>    Identical run over `http`: `204` → **empty jar** → `401`. **`-064` is therefore executable**, in the only topology the
>    container can reach — which also shows option (a) was never reachable there at loopback, because loopback inside
>    `jt-bridge` is the container's own.
> 3. **`-063`/AC-11 splits into two cases, each with its own positive control** — ratified, so the restatement is an
>    owner-approved edit to a ladder row rather than an agent rewriting a ratified promise. It arrived with a datum worth
>    having before the row is written: the login `POST` needs **no** `X-CSRF-Token` and answers `204`, so case 1 must target
>    a **state-changing** endpoint (`PATCH`/`PUT`/`DELETE`) or it passes for a reason unrelated to the check it exists to prove.
>
> **What none of this changes:** the cost recorded in the original decision stands. Same-origin serving means **no preflight
> ever occurs**, so AC-16's credentialed-CORS property remains proven at `CorsCredentialsTests` only. **HTTPS does not
> restore it — the origins are still one origin.**

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
> **Measured 2026-09-12 (`BEHAVIOR-049`, this machine, .NET 10.0.401) — the target above is unedited; this is its result.**
> Dedicated bench, 13 iterations each, first observation reported not discarded: `Hash` n=12 p50 279.1 / **p95 314.8** /
> max 336.0 ms; `Verify` n=12 **p50 271.2** / p95 312.2 / max 330.3 ms, at the 350,000 iterations `BEHAVIOR-048` names.
> **Both figures pass.** The committed assertion (`PasswordCostBandTests`) is deliberately **coarser** — 150–3000 ms per
> observation — because xUnit runs collections in parallel and a percentile measured there measures the scheduler: the
> in-suite first hash was observed at 1108.9 ms against 280 ms alone. The percentile lives in the bench, the coarse guard
> lives in CI, and the two are named apart so neither is mistaken for the other. A lower bound is included because the
> regression nobody would otherwise notice is cost going **down**.
machine, iterations recorded in the envelope.
· *Command:* an xunit `[Fact]` that times 25 iterations and asserts the band — **the assertion is the test**, so the cost
cannot silently drift with a runtime upgrade.

**2.4** No user enumeration: a wrong email and a wrong password are **indistinguishable** in body and within **5 ms** in
timing.
· *Command:* `dotnet test --filter ~EnumerationResistance` — 50 responses each, Mann–Whitney or a documented median bound,
and **a dummy hash verify when the user is absent** (the classic tell isn't the message, it's the missing 400 ms).

- **⚠️ Restated by grill Q8 before any number was written.** A 5 ms bound over 50 samples is a claim about noise, and no run has measured the noise floor; the signal it is meant to detect is a ~400 ms missing verify. So: **asserted** = bodies byte-identical, status codes identical, and the dummy verify's presence proven **structurally**; **reported, not asserted** = the timing distribution with `n`, min/median/max and warm-up included. **An assertion that cannot separate signal from noise is not a gate — it is a coin flip with a green check.** This is §2.8's M3 sin (unmeasured thresholds) refused in advance rather than re-learned.

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

> **§6 amendment — `pk:test`, 2026-09-12.** The grill deferred five items to the test plan; two are real behaviours and land here as **`-066` (Slice 2: expiry via idle window AND hard cap, plus opportunistic prune)** and **`-067` (Slice 3: CORS echoes the exact origin with `Allow-Credentials`, never `*`)**. **Appended, not renumbered into the slices above** — rewriting ratified IDs is precisely how M3's phantom cross-references were created, so the stable IDs keep their meaning and this note carries the change. **Ladder is now `-047`…`-067` = 21 behaviours**, derived by the command printed in the test plan's header — **which had to be corrected once: a naive `-0[0-9]{2}` scan of §6 returns 22, because §6 cites M3's `-037` pattern in prose.** The other three deferred items are not behaviours: the harness-origin question (Q4) is an open decision gating Slice 2, the `404`-gone-vs-not-yours split (Q7) is M5 residue, and the AGENTS.md layer-scoping amendment (Q9) is a separate chore with its own DEBT-12 mirror sync.
> **§6 amendment — execution-time, 2026-09-12 (`-068`).** The `◆`-marked row is **not** grill-derived and not from the plan:
> `-057`'s probing found that `ApplicationEventBus` writes every event to every connected channel, and the row was raised as a
> proposal in the task record rather than fixed inside `-057` — "a fix nobody was asked for is a second change riding inside
> this one". M4 is where it stops being theoretical: `-050` made this a multi-user application, the frame body is
> `data: {"id": "…"}`, and an unscoped fan-out therefore announces **another user's record identifiers** to every open
> stream. `-056`'s `404` still stops them reading the row, and that is the whole difference between "you cannot fetch it" and
> "you were never told it exists". Appended with a stable ID rather than renumbered, for the reason the amendment above
> gives. **Ladder is now `-047`…`-068` = 22 behaviours**, counted by the same command that corrected the last claim.
> **§6 amendment — execution-time, 2026-09-12 (`-069`).** The second `◆` row, from the same `-057` probing note, ratified and
> delivered the same way. AC-8 closed one member — a malformed `id` on the route — and the note recorded that "wrong-typed
> *values* in the other members still die in the binder… `companyName: 42` → **500, no `code`**". The measurement that shaped
> the fix: `RequestDelegateFactory` logs the failure as **`InvalidJsonRequestBody`** and wraps it in a
> `BadHttpRequestException` **whose own `StatusCode` is 400**, so the correct answer exists in-process two frames before the
> response is written and the default exception mapping discards it. That reframes the row: this is not "handle a framework
> limitation", it is "stop throwing away what the framework worked out". **Ladder is now `-047`…`-069` = 23 behaviours**,
> counted the same way.
> **§6 amendment — execution-time, 2026-09-12 19:18 UTC (`-071`).** Raised by the owner's answer to **Q4**, not by the plan or
> the grill: `(a) serve the harness from the API origin` was chosen, then **refuted by measurement** (a `__Host-` cookie is
> discarded over plain `http` even on loopback — Chromium gates the *prefix* on a cryptographic scheme, not on *secure
> context*), and re-answered as **`(a′)` same origin over TLS**. `(a′)` needs no TLS code — Kestrel reads a PEM from config —
> but it does need the API to serve a page, which nothing in this product had ever done, and that is a behaviour: the shell at
> `/`, a real asset as **itself**, a deep client route as the **fallback**, and **`/api/**` still answering as the API**.
> Development-only is part of the behaviour, not packaging: `docs/aws-deployment.md` puts front end and API on **different
> origins**, which is what makes AC-16's credentialed CORS load-bearing, so a production bundle on the API's own origin would
> be a second app silently outside the boundary everyone believes in.
> **Two implementation shapes were rejected by observed failures, recorded because both were invisible from inside the row.**
> A middleware that awaits the pipeline and checks for `404` cannot see the status here (`UseStatusCodePages` is registered
> above it and defers the write) — and **eight of nine cases passed anyway**. `MapFallback` passed **all ten** of `-071` and
> broke a ratified sibling: `Non_json_body_rejected` went **`415 → 404`** in isolation, because a fallback is consulted for
> requests the API should decide itself, which would have quietly destroyed **`-045`'s Content-Type guard** against simple
> cross-origin writes. Only the full suite saw it. **A row being green is not evidence that a catch-all is safe.**
> **`-070` is deliberately unassigned.** Two records cite "`-070`'s scoped-mutation-gate verdict"; `grep -rln "scoped.mutation"
> docs/` returns those two files and nothing else — no row, no spec reference, no EXEC record. The number is left empty rather
> than reused, because filling it would make both citations read as satisfied.
> **Ladder is now `-047`…`-069` + `-071` = 24 behaviours** (`-070` unassigned), counted by the same command that corrected the

> **§6 amendment — execution-time, 2026-09-12 (`-063` delivered; `-074`/`-075` added).** `-063` ran and is verified at
> the Browser seam: 11/11 checks in real Chromium (`tests/browser/csrfGate.mjs` via `run-063.sh`), covering a
> same-site tokenless write → `403 antiforgery` **with no row written**, the same write with the token → `201`, a
> wrong-valued header → `403`, a stale token after rotation → `403`, and a cross-site write → `401` observed from CDP
> network events (never `403`, which is what separates "the cookie never arrived" from "the gate refused"). The
> owner-approved two-case restatement was what made that attribution possible, and executing it confirmed the datum
> behind the split: login itself answers `204` with no token, so case 1 had to target a state-changing verb.
> Two `◆` practice rows came out of the same slice — **`-074`** (the ephemeral key ring, above) and **-075** (two CDP
> harnesses and boot scripts duplicate the same recipe (the pair that does is `run-063`/`run-064`; `run-043` shares none of it), including two fixes applied twice today).
> **Ladder is now `-047`…`-069` + `-071` + `-074`/`-075`/`-076` = 27 behaviours**, counted with

> **§6 amendment — execution-time, 2026-09-13 (`-065` delivered; `-076` added).** AC-14 is verified: the flag-on delta is
> **1,242 B against Slice 0's 3,072 B gate**, with the baseline **rebuilt in the same run** at `41b32af` and reproducing
> 60,118 / 61,368 B **to the byte**. Two facts correct the AC's own wording, and both are kept visible because they change
> what the number means: **`/login` is not a chunk** (`App.tsx` imports `LoginPage` statically), so "the login route's
> bundle delta" is a whole-JS measure; and **flag-off is not free** — the adapter tree-shakes but the auth UI does not, so
> the flag-off delta is 1,022 B rather than the "flattering 0 kB" this spec warned about, and M4's cost splits into 1,022 B
> always-present and 220 B flag-dependent. One `◆` row came out of the slice: **`-076`**, because
> `grep -c bundleDelta .github/workflows/ci.yml` → **0** — a gate that nothing re-runs is a measurement with an expiry
> date, not a guard. The row is listed and left unstarted.
> `grep -cE '^\| `…-0(4[7-9]|[5-7][0-9])`' docs/tests/2026-09-12-test-m4-authentication.md` → **27**. `-070` stays
> unassigned (a phantom cited by two records, defined by none), and `-072`/`-073` are **reserved by STATE §3A's D-2
> and D-3 as proposals, not rows** — the gap in the numbering is the proof they were not quietly filled.
> first claim in this series.

## 7. Sign-off & Grilling Checklist

- [ ] **Owner decides `DECISION-001` (cookie vs Bearer) as written down.** This one is load-bearing: **`EventSource`'s
      missing header option, not taste, is doing the work**, and it is the sort of constraint that reads as trivia until
      someone tries to bolt Bearer onto `-040` a year later
- [x] **Owner approves the ninth `RepositoryError` variant** — **approved 2026-09-12**, when the agent asked rather than inferring it from a merge; `DECISION-006` set the precedent that this class of change needs an explicit yes. Landed in `-061`. — the second widening of M2a's locked invariant, and it
      should be as deliberate as `DECISION-006` was
- [ ] Owner approves `DECISION-002` (two tables over ASP.NET Identity) and `003` (PBKDF2 with no new dependency, Argon2
      **deferred with a named trigger: M5 exposure**)
- [ ] Owner confirms `ASSUMPTION-001` (localhost `__Host-`) via the browser harness, **002** (single user), **003** (no
      limiter yet)
- [x] Grilling (`pk:grill`) must challenge at least: cookie vs Bearer · server sessions vs JWT · `404` vs `403` on
      **Done 2026-09-12 — [`docs/reviews/2026-09-12-m4-plan-grill.md`](../reviews/2026-09-12-m4-plan-grill.md), Q1–Q9.** All seven promised targets were challenged, and the pass produced **one reversed decision (003), one narrowed one (002), one argument corrected (001), one target restated (2.4), and one new open item** (harness origin, Q4). *Evidence, per M3's precedent for ticking a "done" box:* `grep -c "^## Q" docs/reviews/ 2026-09-12-m4-plan-grill.md` → **9**, and `grep -oE "PasswordHasher|EventSourceInit|SameSite|404.*403|iterations|sliding|410"` over the grill's text is non-empty for each promised challenge — **M3 ticked a grill box that its own record proved had never mentioned `ENUM`, `timestamptz` or fan-out; that box is not getting a second chance here.**
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
