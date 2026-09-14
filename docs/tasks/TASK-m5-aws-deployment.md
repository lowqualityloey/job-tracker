# Task Record: M5 — AWS Deployment (deploy-readiness)

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment`
- **Milestone**: M5 — AWS Deployment (Level 3, `pk:ship` + human approval)
- **State**: **decisions recorded 2026-09-15 — spec §12 is seven of seven answered, so T-01's exit criterion is met.**
  The 2026-09-13 stop state ("no commits, no PR, no AWS, no IaC") was lifted in order by the owner's publication
  authorization; T-06 and T-07 are merged (PRs #81/#82), the checkpoints are merged (#83), and what remains is
  spec §6 **T-02…T-17: T-02 is next**, gated on the two facts named in §"Blockers / Open", not on any decision.
- **Owner / Actor**: Assistant (agent)
- **Branch**: `docs/state-m5-deploy-readiness-merged` @ `da04ee4` *(the previously recorded
  `feat/m5-deploy-readiness` no longer exists; `origin/main` = `80918be` and is contained in HEAD)*
- **Plan / Spec**: `docs/specs/2026-09-13-spec-m5-aws-deployment.md` — 1,221 lines, §0–§13, **the authority for every
  `D-M5-*` ID and task dependency below**
- **Decision record**: `docs/aws-deployment.md` — cited as ratified, **but §11 of the spec marks five of its rows stale**;
  do not re-derive infra facts from it until T-15 lands.
- **Checkpoint / Handoff**: `TASK-m5-aws-deployment.checkpoint-001.md` · `TASK-m5-aws-deployment.handoff-001.md`

## Objective
Make the application deployable to AWS (ECS/Fargate + ALB + RDS Postgres + CloudFront) while preserving the
existing auth cookie posture (`__Host-JT*` same-site, CSRF gate), with fail-fast boot and an unauthenticated
health endpoint for the ALB.

## Decisions

**Numbering authority: spec §2, which carries nine decisions, `D-M5-1` … `D-M5-9`.** This record previously listed four
under the same IDs, two of which now mean something else (`D-M5-2` = SSE through CloudFront, `D-M5-3` = TLS terminates at
the ALB too). The collision was removed on 2026-09-13 rather than patched around. The four owner-settled *constraints*
this record originally carried, restated against the spec's numbering:

- **Topology** (spec **D-M5-1**): ECS/Fargate + ALB with TLS, RDS PostgreSQL, one CloudFront distribution with one public
  origin and two origins behind it. Consequence: production `Cors:AllowedOrigins` is legitimately **empty**.
- **Single instance** (spec §5, `ASSUMPTION-m3-backend-api-002`): preserves the **in-process** SSE bus
  (`ApplicationEventBus.cs:30-31`). Scaling out is an architecture task on PG `NOTIFY`, not a number in a task definition.
- **Migrations at boot** (spec **D-M5-4**): `db.Database.Migrate()` at **`Program.cs:160`** (measured today; this record
  cited `139-152`, which is stale), inside the startup scope after `BootGuard.EnsureSafeToStart` at `Program.cs:155`.
  No separate migration step; the choice expires the moment `desiredCount > 1`.
- **Data protection keys** stay in the `-074` RDS table (`PostgresKeyRing`), so a task restart does not invalidate cookies.
- **Image identity** (spec **D-M5-5**): tag with the git SHA, **deploy by digest**.
- **Rolling update** (spec **D-M5-7**): ALB-target-group gated with the circuit breaker; `deploymentAlarms` required, or
  the breaker is inert.

## Work breakdown

> **Status 2026-09-13**: all three items below are **delivered and merged** (PRs #77/#78) — this section is kept as the
> original scope record, not as open work. Item 3 is implemented at `Program.cs:143`; the "enable it" instruction that
> lived in `docs/aws-deployment.md:55` is one of the five stale rows in spec §11.

1. **`GET /api/health`** (new, unauthenticated) — ALB health probe; DB reachability check. Placed so it is not
   caught by `UseSessionGate`/`UseAntiforgeryGate` (those protect `/api/applications*` and `/api/auth/*` minus
   login; `/api/health` is outside that scope, verified in `SessionGate.cs`).
2. **`BootGuard` fail-fast for DB** — extend `EnsureSafeToStart` to assert `ConnectionStrings:Default` is present
   in Production (and non-empty). Crash at startup with a named key, not after a user request.
3. **Forwarded headers** — `app.UseForwardedHeaders()` trusting the ALB/CloudFront via config-driven
   `ForwardedHeaders:KnownNetworks` (no hardcoded CIDR). So `Request.Scheme` reflects HTTPS behind the ALB.

## Changed files (planned)
- `api/src/JobTracker.Api/Program.cs` — health endpoint + forwarded headers
- `api/src/JobTracker.Api/Auth/BootGuard.cs` — DB-connection fail-fast assertion
- `docs/aws-deployment.md` — note `ForwardedHeaders:KnownNetworks` must list the VPC/ALB CIDR

## Acceptance (from spec AC1–AC6)
- AC-1: `GET /api/health` → 200 with body `{"status":"ok","database":"reachable"}` (`Program.cs:203` + `:207`, measured
  2026-09-13). **There is no latency / `dbPing` field** — a smoke test asserting one fails against working code (spec §11).
- AC-2: single-origin browser auth works, no CORS preflight
- AC-3: missing `ConnectionStrings__Default` → boot crash with named error
- AC-4: migrations applied; `data_protection_keys` present
- AC-5: redeploy previous image recovers, no DB change
- AC-6: no `SameSite=None` cookie; `-073`/`D-3` remain unimplemented

## Verification

Re-executed at the `pk:checkpoint` boundary on 2026-09-13 (17:02–17:06 UTC) — see §4 of
[`TASK-m5-aws-deployment.checkpoint-001.md`](./TASK-m5-aws-deployment.checkpoint-001.md) for the full table:

- Frontend: **`npm run verify` → exit 0** (typecheck · lint · lint:css · **227 tests / 23 files** · build, `61.23 kB gz`).
- Container: **`docker build --no-cache-filter build -f api/Dockerfile api` → exit 0**, digest `sha256:ed0d92c0…`, restore
  re-executed with **zero `NU1015`**. The earlier incremental build was also green but reported the restore step
  `CACHED`, so it proved nothing about the fix; that rerun is the citable one.
- API suite (`dotnet test`): **not re-run** — no `api/` code changed this session. The last recorded count,
  169 passed / 0 failed / 0 warnings, is history from an earlier boundary and is not re-claimed here.
- Docs: `docs/STATE.md` §1/§2/§3/§3A/§5/§6/§7/§8 regenerated from live commands. Structure proven preserved — **§4 is
  byte-identical to HEAD** (`cmp` clean, 55 lines), `## ` headings 8 = 8, and all 17 deleted lines accounted for as
  replaced projections. **10 broken relative links repaired**: 8 pointing at `../tasks/…` and 2 at `../spikes/…`, which from
  `docs/` resolve outside the tree. A path-existence check over every non-URL link target in the file now reports **0**
  unresolved (was 10 — one of them introduced by this session copying the neighbouring style).

## T-06 executed — R-4.2 SSE keep-alive heartbeat (2026-09-14, **committed on this branch**)

Level **L1**, as routed. Two files moved: the `GET /api/applications/events` handler in
`api/src/JobTracker.Api/ApplicationCatalog.cs` (`MapApplicationCatalog`), and a new
`api/tests/JobTracker.Api.Tests/EventStreamKeepAliveTests.cs` — 8 cases (5 facts + a 3-row theory).

**What ships.** Every stream writes a comment frame — `: keep-alive\n\n`, *not* `event: keep-alive`, which every browser would
hand to `onmessage` as a message — on an interval read from `Sse:KeepAliveSeconds`
(`ApplicationCatalog.KeepAliveConfigKey`), defaulting to `ApplicationCatalog.DefaultKeepAliveInterval` = 20 s. Waiting for the
next owner-scoped change and waiting for the next beat are one `Task.WhenAny`, so a quiet catalog is the loop's normal state
instead of a blocked read. **No shipped `appsettings` carries the key**: the code's default is the live value, and §4.2 derives
it from the proxy ceilings rather than from per-environment taste. Absent, unparseable, zero and negative all fall back to the
default — `Task.Delay(0)` in this loop is not a fast heartbeat, it is a hot loop on every open stream in the process.

**Two findings, both measured, both of which changed the code.**

1. `Task.WhenAny` cannot distinguish *"the interval elapsed"* from *"the request was cancelled"*. A cancelled token makes
   `Task.Delay` return at once, so a bare reference comparison turns every disconnect into a heartbeat written as fast as the
   pipe drains — the same failure the config guard exists to prevent, arriving by another route. The loop now awaits the delay
   task before writing: it rethrows `OperationCanceledException` on cancellation and costs nothing on a real beat.
2. **`TestServer` cannot deliver a client hangup to the handler.** Two client-side forms were tried; neither fired
   `HttpContext.RequestAborted` within 5 s — cancelling the read on the response stream, and cancelling the token passed to
   `SendAsync`. The disconnect is therefore played server-side through `IHttpRequestLifetimeFeature.Abort()`, the call Kestrel
   makes when a socket breaks, observed by an `IStartupFilter` probe the test host registers. That proves the handler stops
   writing and unwinds cleanly on cancellation; it does **not** prove a closed tab reaches `Abort()` — that is ASP.NET Core's
   contract, not this repository's. Recorded here because this file's first draft asserted the opposite as fact, and the probe
   refuted it in one run.

**Evidence** — Red → Green, same machine, same filter family, UTC:

| Step | Command | Result |
| :--- | :--- | :--- |
| Red | `dotnet test api/tests/JobTracker.Api.Tests --no-build --nologo --filter "FullyQualifiedName~EventStreamKeepAlive"` against the config seam without a heartbeat loop | **4 failed / 4 passed** (21 s) — the four heartbeat-existence cases; the ceiling and config-fallback cases passed, as they must against a seam-only build. Log `/tmp/t06-red-keepalive.log` |
| Green, focused | filter `"FullyQualifiedName~EventStream"` (3 files, 16 cases) | **0 failed / 16 passed** (23 s), exit 0. Log `/tmp/t06-green3-sse.log` |
| Green, focused — repeated | same filter, unchanged tree, ~10 min later | **0 failed / 16 passed** (21 s), exit 0. Log `/tmp/t06-green4-sse-final.log`. Run twice because these are the suite's only cases that wait on real wall-clock delays, and a heartbeat test that passes once on a quiet machine is the classic flake |
| Green, full API | `dotnet test api/tests/JobTracker.Api.Tests` | **0 failed / 200 passed** (1 m 44 s; 01:16:37 → 01:18:47 UTC), exit 0. Log `/tmp/t06-green-full.log` |
| CI parity | `dotnet build api/JobTracker.slnx -p:TreatWarningsAsErrors=true` | **0 warnings / 0 errors**, exit 0 — restore re-executed, not cached. Log `/tmp/t06-build-final.log` |

R-4.1 / BEHAVIOR-068 owner-scoping is unchanged and still asserted: `event: change` is produced by the same branch it always
was, and one case now asserts a *foreign* owner's id never appears on the wire while ≥12 heartbeat frames prove the pipe was
live — the first negative SSE assertion in this repo that carries its own liveness control, where the earlier one needed a
second tab to write something to prove the stream wasn't merely dead.

**One exit criterion met by equivalence, stated precisely.** §6's first clause reads "*a 5-minute idle stream* emits ≥ 12
comment frames". The test instead measures 18 intervals of a 50 ms heartbeat across 900 ms, and pins the number that ships in a
separate assertion carrying §4.3's ceiling arithmetic (20 s < CloudFront's 60 s origin-response timeout; ALB's 120 s idle timeout
÷ 20 s = 6 beats between the ceiling and the re-register margin). A literal five-minute case would be cut off by the `verify-api`
job's own `timeout-minutes: 15` before it finished, so it would never run. What the compressed form cannot catch is a *slow*
failure — a heartbeat that stalls after four minutes — and that residual is named rather than waved at: this measures the loop's
structure, and the real timeout relationship stays a `[verify-at-apply]` item for T-09 and T-13.

**Not done at the time of writing** (superseded below): nothing was committed, pushed, or PR'd when this section was
first drafted, and the boundary still held. **It has since been overtaken by fact** — the ladder ran, and T-06's three
commits (`67e5d32` seam, `41183bb` red, `18ae35f` green) are merged in PR #81.
The `/tmp` logs are local and perishable; the counts in this table are the citable record.

## T-07 executed — R-4.4 bounded health probe (2026-09-14, **merged in PR #82**)

Level **L1**, as routed. Three commits, Red / Green kept apart per `AGENTS.md`:
`a5fc654` `chore(api)` (the `HealthProbe` seam: config key, default, guarded reader, pass-through probe + 6 config cases) ·
`4799856` `test(api)` (the 4 bound cases; **2 failed / 8 passed**) · `a42070e` `feat(api)` (the race; handler rewired).

**What ships.** Spec §4.4's option (2): the probe answers *"still thinking"* as unhealthy at its own ceiling, so the ALB
reads a verdict, never a silence. `HealthProbe.ProbeAsync` links the request token with a `CancelAfter(bound)` source, hands
the callee that token, and races the probe against `Task.Delay(bound, CancellationToken.None)` — the bound is a promise this
method keeps regardless of whether `CanConnectAsync` obeys a cancellation, which is exactly the black-holed-route case §4.4
measures (refused answers in 0.09 s; *dropped* packets leave the call inside Npgsql's connect timeout). The timeout is
`Health:ProbeTimeoutSeconds`, default 5 s, and the guard's blast radius is bigger than T-06's: a bound of `0` would make
every probe answer unhealthy immediately, emptying the target group, so absent/unparseable/non-positive all fall back to
the documented default.

**Three findings, each of which changed the code.**

1. T-06's lesson generalises: the bound cannot be a token the callee may ignore, so the ceiling is a *separate delay task*,
   and "which reason fired" is decided afterwards by asking the sources — never by comparing completed tasks.
2. The linked source is deliberately **not** disposed on the abandon path: a `using` there would deregister the cancellation
   the still-alive probe is registered on — disposing the walkaway's own escape hatch. Every path where the probe completed
   disposes as usual. The abandoned task's eventual fault is observed by a `ContinueWith`, so a late cancellation cannot
   resurface at finalizer time as an `UnobservedTaskException` log line for a request already answered.
3. An *obedient* probe that throws `OperationCanceledException` at the bound is also a health verdict, not a crash: without
   the catch, it reaches `UseExceptionHandler` as a 500 where the ALB expects a 503. The catch filter is
   `when (!requestAborted.IsCancellationRequested)` — a genuine client hangup still propagates, because nobody is left to
   read the 503.

**One exit criterion met by equivalence, named not waved at.** §6's clause is about the endpoint's behaviour, but no
`TestServer` case can drive a black-holed datasource through the HTTP pipeline: boot's `db.Database.Migrate()` consumes the
same connection string first, so a hang kills startup, not the probe. The bound is therefore pinned at the probe seam with a
never-returning fake; the live relationship to the ALB's 10 s target-group timeout stays `[verify-at-apply]` with §4.3
(T-09/T-13).

**Evidence** — focused tests and the CI-parity build passed locally; **the full API suite did not, and the reason is the
machine's Docker nesting, not this code**:

| Step | Command | Result |
| :--- | :--- | :--- |
| Red | `dotnet test api/tests/JobTracker.Api.Tests --filter HealthProbeBoundTests` at the pass-through stub | **2 failed / 8 passed** — both failures "did not answer within the test budget — the bound is not being kept" |
| Green, focused | same filter, full tree | **10 passed / 0 failed** (199 ms), exit 0 |
| CI parity | fresh `obj/bin`, `dotnet build api/JobTracker.slnx -p:TreatWarningsAsErrors=true` | **0 warnings / 0 errors** |
| Green, full API | `dotnet test api/tests/JobTracker.Api.Tests` | **NOT RUN GREEN.** Five invocations (host bridge × Ryuk on/off × `TESTCONTAINERS_HOST_OVERRIDE`) all fail inside Testcontainers bootstrap: `ResourceReaperException : Initialization has been cancelled.` (60 s × collections, 10 m 19 s per run) or, with Ryuk disabled, `Npgsql … Connection refused` to a mapped port whose container the daemon had not finished starting. The identical signature appears at `a5fc654` — a seam-only commit that changes no fixture code — so the failure enters before this branch's behaviour does. Manual control: a bare `postgres:18.6` container published here in ~5 s and answered TCP. Same machine, 01:16 UTC the *same suite* ran 200/200; the daemon/image state degraded mid-day. Tracked as **DEBT-23**; full-suite verification transfers to CI's `api` job (native Docker) on this PR |
| CI, on PR #82 | `verify-api` job (native Docker, full suite) | **Passed! 0 failed / 210 passed** (1 m 12 s) — the transfer worked; the DEBT-23 row records the outcome |

## State boundary (Level 3)

`handoff_ready`, self-imposed: it bars implementation — no AWS calls, no IaC, no `api/`/`src/` edits, no commits, no PR,
no task switch — until the resume condition below is met or the owner overrides this record. It does **not** bar the
unblocked code task in §Next action, and it does not bar read-only measurement.

## Blockers / Open

- **Spec §12 carried seven owner decisions, A–G — all answered 2026-09-15** (A `ap-southeast-1`; B option (1), zone in
  this account; C CDK; D bought `/20` + VPC endpoints; E manual `citext` pre-creation; F manual rotation; G human-run
  deploy), written into spec §12's Answer cells *before* any AWS-adjacent work, which is what the resume condition
  demanded. "Six of them block T-02…T-05" was true until this pass.
- **What now gates T-02, in place of §12:** (i) the zone's **apex name** — B decided the shape, not the literal, and unlike
  the decisions it cannot be applied from chat; (ii) **no `aws` CLI on this machine** (`command not found`, measured
  2026-09-15) and no credential path proven — T-02's first commits are installing and proving that, before any
  `describe-*` or certificate request.
- **A = workload region** gates the `us-east-1` ACM pairing and every `describe-*` measurement.
  **B = domain / DNS host / zone owner** is the long pole: DNS validation costs minutes-to-hours that no retry shortens.
  **C = IaC tool** decides the *form* of every task in §6, and everything downstream of it is rework if it flips.
- **Publishing hazard: retired 2026-09-14.** The `da04ee4`/`c8bd912` orphan-SHA pair was superseded by the ladder, which
  ran and merged as PR #81 (`dfe018c` → `b4c2888`); `origin/main` now contains all ten commits. The discipline the hazard
  taught survives in `AGENTS.md`: assert `headRefOid` after every `gh pr create`.
- Not blockers: **T-06 and T-07** depend on nothing (`Depends: —` in spec §6) and touch no AWS surface.

## Next action

**Published, green, merged.** PR #82 (`0986e53` → merge `7e99d7e`, `2026-09-14T05:33:17Z`); the post-create
`headRefOid` assertion held, and CI's native-Docker `verify-api` ran the full suite the local machine lost:
**210 passed / 0 failed** (1 m 12 s). T-06 and T-07 — spec §6's only `Depends: —` items — are delivered. **§12 is answered
(seven of seven, 2026-09-15 — see §"Blockers / Open"): T-01's exit criterion is met, and the next task is T-02** (L1,
zone confirmation + both ACM certificates + DNS validation). It waits on two named facts, not on a decision — the zone's
apex name (owner) and an `aws` CLI with working credentials on the machine that runs it (measured absent here).
Provisioning itself stays behind `pk:ship` and human approval; Level 3 is unchanged.

T-06 is done (§ above) and **committed** — the ladder below ran, and both records were merged in PR #81
(`dfe018c` → merge `b4c2888`, 2026-09-14 03:46:01Z). Its Red → Green is recorded; the Refactor step of the triple was
taken *during* Green rather than as a fourth commit-shaped pass — the frame strings became named constants and the interval read became one
private helper while the tests were being written, so there is no pending cleanup left in that handler. One pre-existing wart
was found and deliberately **not** touched: a stray over-indentation at `ApplicationCatalog.cs:98`, present in HEAD, unrelated
to this change.

**The ladder ran** (owner authorized publication on 2026-09-14). The four concerns in
[`TASK-m5-aws-deployment.handoff-001.md`](./TASK-m5-aws-deployment.handoff-001.md)'s ladder table carry T-06 as
**three** commits rather than one, because Red and Green sit apart from each other — and the Red number below was
re-measured against the seam commit's exact shape instead of being quoted from the earlier run:

| # | Commit | Carries | Measured at that step |
| :-- | :-- | :-- | :-- |
| 1 | `87c2ba2` `fix(api)` | `api/Dockerfile` + `api/.dockerignore` | one behaviour, deliberately unsplittable |
| 2 | `165e61f` `docs(spec)` | the measured rewrite, +1,189/−66 | — |
| 3 | `9d48e7f` `chore(promptkit)` | gitlink `9ef20b8 → c7199c3`, published on the engine's own `main` | — |
| 4 | `67e5d32` `chore(api)` | the SSE config seam only, no behaviour | builds clean under `TreatWarningsAsErrors` |
| 5 | `41183bb` `test(api)` | the 8 heartbeat cases | **4 failed / 4 passed**, 23 s, exit 1 → `/tmp/t06-red-2-seam.log` |
| 6 | `18ae35f` `feat(api)` | the beat | **16 passed / 0 failed**, 23 s, exit 0 → `/tmp/t06-green5-seam-to-loop.log` |
| 7 | `docs(state)` | this record + `docs/STATE.md` + the two 2026-09-13 records | last on purpose: §3/§5/§7 are projections |

The four failing cases at step 5 are precisely the four that need a beat on the wire; the four passing are the seam's
own answers (the 20 s default, the proxy-ceiling inequality, and the three unusable-interval rows). Commit 7's own
SHA cannot appear in its own message, so it is the tip of this branch rather than a row here.

**Resume condition — met in form 2026-09-15**: the seven answers are on disk in spec §12, the owner chose to proceed to
T-02, and this record is the vehicle. Provisioning did not begin on answers held only in chat — they were written first.
The one chat-only fact that remains, B's zone apex name, *cannot* be applied from chat at all; it is T-02's checklist
item 0.
