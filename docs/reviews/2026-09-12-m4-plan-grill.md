# M4 Spec Grill — 9 Questions, Two Decisions Flipped, One Premote Shown to Be False

**Date:** 2026-09-12 · **Workflow:** `pk:grill` · **Target:** [`SPEC-m4-authentication`](../specs/2026-09-12-spec-m4-authentication.md)
· **Reviewer:** Assistant (self-grill, adversarial) · **Record created as the agent act PR #35's merge authorised**
(`459d087`)

> **Method, stated because M3's grill was better for it.** Every question below that *can* be answered with a command was
> answered with a command, not with recollection. Three of M3's five integrity findings were claims about a document I
> had truncated or mis-remembered, so "I believe `EventSourceInit` only has `withCredentials`" is not good enough when an
> entire milestone's credential format rests on it. **Two premises did not survive, and one of them was load-bearing for
> a decision record.**

---

## Q1 · Is the credential-format claim real, or is it a strong opinion about browser APIs?

**Measured.** `node_modules/typescript/lib/lib.dom.d.ts:688`:

```ts
interface EventSourceInit {
    withCredentials?: boolean;
}
```

**One member. No `headers`.** The claim that shaped `DECISION-001` is now backed by a type definition rather than by my
memory of one, and it survives.

**But the grill's real question is the one the spec dodged: is "Bearer is impossible" true?** No. It is *expensive*. A
`fetch` + `ReadableStream` reader can send an `Authorization` header and parse SSE frames by hand. The accurate statement
is: **Bearer does not fit the transport `-040` already ships, and adopting it means re-owning reconnect, backoff and frame
parsing that `EventSource` gives for free.** `DECISION-001` is amended to say that, because a decision justified by a false
"impossible" is one nobody should trust when the next real constraint appears. **Verdict: conclusion stands, argument
corrected — "keeps `-040` intact", not "no alternative exists".**

## Q2 · Is PBKDF2-by-hand really required by the no-new-dependency rule?

**No. This premise is false, and it was doing work in a decision record.** Measured:

```
/home/heyloey/.dotnet/shared/Microsoft.AspNetCore.App/<ver>/Microsoft.AspNetCore.Identity.dll
                                                        Microsoft.Extensions.Identity.Core.dll   ← PasswordHasher<TUser>
                                                        Microsoft.Extensions.Identity.Stores.dll
```

All three ship **inside the ASP.NET Core shared framework**, and `api/src/JobTracker.Api/JobTracker.Api.csproj` is
`<Project Sdk="Microsoft.NET.Sdk.Web">` → `PasswordHasher<TUser>` is available with **zero `PackageReference` added**.

**And the deeper error: "runtime dependencies = 3" is an npm count.** The API carries **5** `PackageReference`s already
(measured). So `DECISION-003` justified a *backend* crypto choice using a *frontend* budget number that was never about the
backend — a category error, in a spec section whose whole purpose is naming premises. **Withdrawn.**

**What survives is the part the argument was confusing.** Identity splits cleanly:

| Half | Cost | Verdict |
| :--- | :--- | :--- |
| **`PasswordHasher<TUser>`** (in `Microsoft.Extensions.Identity.Core`) | Zero packages; PBKDF2-SHA256; format `{PRF$iterations$salt$subkey}` — **already self-describing, which is the property 003 said it wanted** | **Adopt.** Raise `PasswordHasherOptions` iterations above the 100k default explicitly; don't inherit folklore |
| **ASP.NET Identity's EF stores** (`IdentityUser`, claims, logins, roles — ~7 tables) | Tables and concepts `ASSUMPTION-002` puts out of scope | Still declined — **`DECISION-002` stands, but for the stores, not the hasher** |

**The lesson is the conflation itself:** `DECISION-003` rejected a maintained, zero-dependency implementation because it
had been bundled with a heavyweight one it *had* correctly rejected. Hand-rolled crypto parsing is the thing to avoid, and
I had argued myself into it with a number from the wrong layer.

## Q3 · Will M3's own CORS test suite break when credentials are enabled?

**Checked by listing its assertions, not by grepping for an absence** — an empty grep is how M3's gap-10b false charge was
made. `CorsContractTests.cs` asserts: `Access-Control-Allow-Origin` equals the configured origin (`Assert.Equal`), `PUT`
and `DELETE` in `Access-Control-Allow-Methods`, a permissive `Allow-Headers` check, and `200` on preflight. **No assertion
that credentials are absent.**

`Program.cs:38` already anticipates this, in a comment written for M3: *"`AllowCredentials` is deliberately absent… a
wildcard origin combined with credentials is rejected outright by browsers, **so adding it later would break this quietly
rather than loudly**."* Because the policy is `WithOrigins(allowed)` — specific origins, never a wildcard —
`.AllowCredentials()` is **legal**. **Verdict: safe, but the promised loud failure has been traded for a quiet one, so
`pk:test` must add an AC asserting `Access-Control-Allow-Credentials: true` *and* that the origin is echoed rather than
`*`** (ASP.NET won't reject the dangerous combination; the browser will, silently).

## Q4 · Does the dev harness's topology make AC-12 fail for a reason that isn't the product?

**Yes, and this is the finding most likely to cost a day if left ungrilled.** Measured from `tests/browser/httpCrossTab.mjs`:

```
const APP = process.env.E2E_APP ?? 'http://127.0.0.1:4173'
const API = process.env.E2E_API ?? 'http://127.0.0.1:5080'
```

Defaults are same-host/different-port → **same-site** (Chromium's site = scheme + host; **port is ignored**), so
`SameSite=Lax` attaches. But M3's actual runs could not use those defaults: the sandbox **cannot route into** the Docker
network, so the built `dist` had to be served inside `jt-bridge` while the API stayed on the host, reached as
`http://<sandbox-ip>:5080` — i.e. **document host `127.0.0.1`, API host `172.23.x.x` = two different sites.** A
`SameSite=Lax` cookie would then be **dropped on every request**, and AC-12 would report "authentication broke SSE" when
nothing in the product is broken at all.

**Resolution required before Slice 2, and it is a real trade:** serve the built app **from the API origin itself** in
harness mode (static files + fallback, environment-gated) → **same-origin, cookies attach trivially, SSE testable** —
but then the credentialed-CORS property **stops being exercised in the browser at all**, and it must be proven at the
`WebApplicationFactory` level instead (Q3's new AC). **The alternative — a second container on the same host as the API —
buys the browser proof and costs the topology constraint M3 spent a day discovering.** This is recorded as an open
harness decision, not smuggled into a slice.

**Corollary:** `localhost` and `127.0.0.1` are **different sites**. Whatever the answer, the recipe must freeze **one**
loopback spelling; a spec that says "localhost" while the harness types `127.0.0.1` has built a cross-site trap into its
own docs.

## Q5 · Sessions are server state now. Who cleans them up?

Nobody, in the spec as written. `sessions` grows one row per login forever, and `expires_at` without a sweep means an
expired row is merely *disagreeable*, not gone — while revocation depends on the row still existing. **Single-instance,
no cron, no background service is the right shape here: prune expired and revoked rows opportunistically on login.**
Amortised, no new moving part, no scheduler to test. **Added to Slice 2 as a behaviour.** A design that needs Redis TTLs
to be respectable is a design that needs them; this one doesn't.

## Q6 · Sliding or absolute expiry?

The spec says "sliding expiry + revocation" in the FMEA and never decides. **Absolute with a bounded max lifetime**, plus
revocation on logout: sliding is what a stolen cookie wants, and the honest reason to allow *some* slide is that a
single-user local tool that logs you out mid-form is a tool you stop using. Resolution: **idle window AND a hard cap**,
both from configuration, both tested. Recorded as a decision to make at `-052`, not silently inherited.

## Q7 · Does `404`-not-`403` break M3's client contract in a way that matters?

`DECISION-004`'s choice is right — **a 403 is an existence oracle.** But M2a's offline queue treats `not-found` as "the
record is gone, stop retrying". Today that's correct because `not-found` means genuinely deleted. **Once ownership
exists, `404` will also mean "not yours", and a queued edit to another user's row is silently discarded rather than
surfaced.** Under `ASSUMPTION-002` (single user) that state is unreachable, so **the choice is safe now and needs the
distinction recorded as M4→M5 residue**: a two-user system needs either a `410`-shaped "gone vs not yours" split or a
re-fetch-and-reconcile rule on the client. **Named here so it isn't discovered as a bug report.**

## Q8 · Can a 5 ms bound over 50 samples separate a missing 400 ms verify?

**Not defensibly as written.** A dummy verify is a *~400 ms* signal; a 5 ms bound is a claim about noise, and no run has
shown the noise floor. Asserting it would be §2.8's original sin in a new costume: **a threshold nobody measured, in a
document whose preamble promises measured thresholds.** Restated:

- **Asserted:** bodies byte-identical, status codes identical, **and the dummy verify's presence proven structurally**
  (a wrong-email attempt must take a path that performs a verify — measurable without timing).
- **Reported, not asserted:** the timing distribution, with `n`, min/median/max, warm-up included.
  **An assertion you cannot yet separate from noise is not a gate; it is a coin flip with a green check.**

## Q9 · Is "no new dependency" still a decision, or has it become a reflex?

Asked as promised. **Answer: it is a real rule applied to the wrong layer.** Its justification — 3 npm runtime deps — is
a *frontend* fact; the API already carries 5 packages, and .NET's shared framework supplies far more without a package at
all (`Q2`). The rule earns its place for React/TS, where bundle bytes and audit surface are user-visible (M3's `1.21 kB`
delta being the actual, measured reason it matters). For C# it should read: **prefer what the framework already ships;
add a package only when it removes code, not when it saves typing** — which is exactly how `Npgsql`/`EF Core` earned their
places. **Proposed AGENTS.md amendment**, scoped wording, one mirror edit in the same commit (DEBT-12): the count is
`3 (npm)`, and it constrains `src/**`, not `api/**`.

---

## Consequences, in one place

**Applied to the spec now:** `DECISION-001`'s argument corrected from "impossible" to "keeps `-040` intact" ·
`DECISION-003`'s **false premise withdrawn and the decision reversed to `PasswordHasher<TUser>` with raised iterations** ·
`DECISION-002` narrowed to *stores*, not *hasher* · **AC-3 restated** per Q8 (structural assertions, timing reported) ·
§7's grilling box ticked, **with the before/after grep as its evidence** — it was the only unchecked box that was
agent-owned.

**Deferred to `pk:test` / the ladder, deliberately not edited into the task record this pass** — so the counts there stay
true and nothing gets a number written from intent: credentials-on-CORS AC (Q3) · session prune + idle-window/hard-cap
behaviours (Q5/Q6) · the harness-origin decision (Q4), which **must** be settled before Slice 2 · the
`404`-gone-vs-`404`-not-yours split as M5 residue (Q7) · the AGENTS.md layer-scoping amendment (Q9).

**What the grill did not resolve, named:** whether Chrome accepts `__Host-` over plain `http://` on loopback in this
build (`ASSUMPTION-001` — browser-only, deferred to `-051`) · whether `citext` is available in the pinned PostgreSQL image
(`psql`-measurable, needs a container exec the sandbox gates behind approval) · Identity's exact table count (~7 by
memory, unverified — **and it no longer matters**, since Q2 removed the reason anyone was counting).

**Integrity ledger addition:** `DECISION-m4-auth-003` was recorded with a justification drawn from the wrong layer's
dependency count, and the shared framework already contained the alternative it rejected. **Found by running `ls`, not by
reading.** That is the fourth M4-era case of a claim about a tool that was checkable and unchecked — and the first where
the check took ten seconds.
