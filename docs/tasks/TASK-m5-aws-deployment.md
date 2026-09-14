# Task Record: M5 — AWS Deployment (deploy-readiness)

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment`
- **Milestone**: M5 — AWS Deployment (Level 3, `pk:ship` + human approval)
- **State**: **`in_progress`** — resumed at 14:35 UTC after `checkpoint-003`'s `handoff_ready`; **DEBT-27 closed 15:14:36Z by #92's measured merge (`09b4e0d`)**, T-03's paper design written, its §11 filled by the pass that retracted §0's uncited claim, and **the paper half MERGED 18:33:25Z in #93 (`8a10b69`)** — design only, because **I-A1-5 still forbids executing it**, and the queue is now wholly the owner's: **O-1…O-4** before T-03's execution and everything downstream. And **the field itself needed reconciling first, which is the finding worth reading before the history below.** `pk:checkpoint`'s receiver-validation duty is what caught it: checkpoint-003 declared its stop state **in `docs/STATE.md` and in its own record, but never wrote it into the Local Task Source**, which still read `in_progress`. The Task Record is the authority, so the *declaration* was the incomplete artifact — a session resuming on this file's strength alone would have started editing without validating anything. The asymmetry is the same shape as DEBT-27, three hours older: a projection and its source describing two different states. History stands: T-01's exit is met and merged (spec §12 seven of seven, PR #84), checkpoint-002 is
  merged (PR #85 → `3121b2d`, `2026-09-14T06:23:49Z`), and the owner's instruction resumed this task at **T-02**, whose
  step 0 executed at 06:31 UTC and **retracted one of this record's own premises**: the `aws` CLI is not absent on this
  machine, it is a Windows binary reachable from WSL (§"Blockers / Open"). The 2026-09-13 stop state ("no commits, no PR,
  no AWS, no IaC") was lifted in order by the owner's publication authorization; T-06 and T-07 are merged (PRs #81/#82)
  and what remains is spec §6 **T-02…T-17**, gated on three named owner facts — not on any decision, and not on any
  installation.
- **Owner / Actor**: Assistant (agent)
- **Branch**: task work lands per-PR on `main`. **Re-measured at 15:20 UTC**: `gh pr view 92` → `MERGED 2026-09-14T15:14:36Z`, merge `09b4e0d`, `merge-base --is-ancestor ed1ed5f main` → **YES**, and `git diff --stat 9f14671..HEAD` → three paths, +290/−80, all under `docs/`. **Re-measured at 18:40 UTC**: `gh pr view 93` → **MERGED 2026-09-14T18:33:25Z**, merge `8a10b69`, `merge-base --is-ancestor 7f107ee origin/main` → **YES**; `main` reconciled by `--ff-only`; the boundary sync now sits on **`docs/state-m5-t03-merged`**, cut from `8a10b69`. (Earlier, and now merged: work sat on **`docs/m5-t03-network-design`**, cut from `09b4e0d`.) **Earlier this boundary: `gh pr view 91` → `MERGED` at
  `2026-09-14T14:24:13Z`, merge commit `9f14671`, and `git merge-base --is-ancestor 9f14671 HEAD` → **YES** on the branch carrying this
  edit — so `checkpoint-003` and its projection sync are in `main`'s history, not merely pushed. `main` = `origin/main` at that SHA after
  `git pull --ff-only`; `git status --porcelain` shows only the pre-existing `.m5-parts/` (DEBT-21). **Current work sits on
  `docs/m5-debt27-t02-reconciliation`** — the DEBT-27 reconciliation, spec-side first.
  *Earlier headers recorded #85 → `3121b2d` (PRs #81–#85) and, before that, `da04ee4`; each was superseded by measurement, and the
  superseding line is kept because a branch claim that is only ever overwritten is a branch claim nobody can audit.*
- **Plan / Spec**: `docs/specs/2026-09-13-spec-m5-aws-deployment.md` — **1,379 lines** (`wc -l`, re-measured 2026-09-14 14:45 UTC; the 09-13 rewrite and the §12 answer pass grew it to 1,220, and **Amendment A1 (§14) grew it by a further 159**), §0–**§14**, **the authority for every `D-M5-*` ID and task dependency below** — with this qualification, written where a reader will hit it: **§14 reverses part of `D-M5-3` and supersedes §3.1/§3.3's TLS and listener statements**, so those sections must be read with the amendment, not instead of it
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

`in_progress`, and **the word does not loosen the Level 3 bar**: every AWS *write* is still barred — no certificate
request, no IaC apply, no `pk:ship` step — until the owner authorizes each one. What the boundary never barred, and what
T-02 step 0 was, is **read-only measurement**; the record's own rule is that the read precedes the write that leans on it,
which is how "no `aws` CLI on this machine" got caught two hours after it was written down.

## Blockers / Open

- **Spec §12 carried seven owner decisions, A–G — all answered 2026-09-14** (A `ap-southeast-1`; B option (1), zone in
  this account; C CDK; D bought `/20` + VPC endpoints; E manual `citext` pre-creation; F manual rotation; G human-run
  deploy), written into spec §12's Answer cells *before* any AWS-adjacent work, which is what the resume condition
  demanded. "Six of them block T-02…T-05" was true until this pass.
- **What now gates T-02 — re-measured 2026-09-14 twice, and the second pass falsified the first.** Full evidence:
  [`m5-deploy-log.md`](./m5-deploy-log.md), 18 read-only calls across two regions.
  **CLI and profile: settled.** AWS CLI v2 is a Windows binary callable from this WSL box
  (`aws-cli/2.36.44` at `"/mnt/c/Program Files/Amazon/AWSCLIV2/aws.exe"`) — the earlier *"no `aws` CLI on this machine
  (`command not found`)"* had tested only the **Linux `PATH`**. The owner named `loey` at 06:49 UTC; neither profile
  carries a `region` key, so every call is region-qualified. Both profiles carry *only* a `login_session` key in
  `C:\Users\jonel\.aws\config`, and no `credentials` file exists on either side of the WSL boundary — so there is no
  access-key path here, and equally nothing long-lived that a chat log could leak.
  **Session: live from 07:21:13Z — and the way it came live corrects this record.** `login_session` is a browser-login
  artifact with an expiry; at 06:50 UTC every authenticated call returned `Your session has expired. Please
  reauthenticate using 'aws login'`. This record then said renewal is *"the owner's terminal, not this shell"* — **partly
  false**: `aws login` accepts `--region`, and without it the command stops to ask, which in a non-TTY shell surfaces as
  `No Windows console found. Are you running cmd.exe?`. Given `--region ap-southeast-1` it printed `Attempting to open
  your default browser` **from this shell** and the owner only had to complete the sign-in. Agent-initiated,
  human-authenticated — the human half is the credentials, not the process launch.
  **The apex name: not a pending read — a falsified premise.** `route53 list-hosted-zones` returns `"HostedZones": []`
  in **both** profiles, and `route53domains list-domains` returns `"Domains": []`. The 07:03 pass here asserted that the
  read "returns the apex", writing the *expectation* as if it were the *result*; measurement answered with an empty list.
  So B's "a Route 53 zone already in this account" is **not true of this account**, and T-02's DNS-validation half has no
  destination. The choice is now an owner decision again, with a cost attached, not a fact to be recalled. The candidates
  on the table: register a domain in this account (money, `route53domains register-domain`, then a zone comes with it);
  point an existing third-party domain at a new hosted zone here (delegation at the current registrar, $0.50/mo);
  create the zone in whichever account really owns the domain and validate there; or drop the custom hostname from M5 and
  accept the ALB/CloudFront default DNS names — which breaks the `__Host-` cookie shape that decision B was protecting.
  **Two facts nobody predicted:** `loey` and `jonell` resolve to the **same account** (IDs compared by hashing the digits,
  not by eye), and both authenticate as **`arn:aws:iam::<ACCOUNT>:root`** with **zero IAM users** in the account. Every M5
  write would therefore run as account root. Neither blocks a read; both block calling a write plan finished.
- **A fourth constraint, newly measured, on the artifact T-02 writes into: the repository is PUBLIC**
  (`gh repo view --json visibility` → `PUBLIC`). Every read lands in `docs/tasks/m5-deploy-log.md`, and zone apex names,
  account IDs and ALB/CloudFront DNS names are precisely the literals that make a deployment recon-able. **The log
  records the command, the verdict, and a `<placeholder>` in place of any identifier** — carrying real names in a public
  file is the owner's call to make explicitly, never a default to slide into.
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
(seven of seven, 2026-09-14 — see §"Blockers / Open"): T-01's exit criterion is met, and the next task is T-02** (L1,
zone confirmation + both ACM certificates + DNS validation). What it waited on was written here as *two named facts, not
a decision* — the zone's apex name (owner) and "an `aws` CLI with working credentials on the machine that runs it
(measured absent here)". **Both halves of that sentence are now known to be wrong, and the second one twice**: the CLI is
a Windows binary reachable from WSL, the session is live from 07:21 UTC, and `list-hosted-zones` — which the 07:03 pass
here claimed "returns the apex" — **returns an empty list**. Step 0 ran; its result is not a hostname but the absence of
one. See §"Blockers / Open" and [`m5-deploy-log.md`](./m5-deploy-log.md).
Provisioning itself stays behind `pk:ship` and human approval; Level 3 is unchanged.
The decisions PR shipped: **#84 merged at `3e2f2fb` (`2026-09-14T06:07:46Z`)** — §12's answers are in `main`'s
history, and the live contract for the next session is [`checkpoint-002`](./TASK-m5-aws-deployment.checkpoint-002.md)
(including its paste-ready handover prompt and T-02's step-0 checklist).

**Step 0 has now run — all five reads, plus thirteen more — and it returned a finding rather than a hostname.**
Its outcome is the corrected blocker above and the deploy log; what follows is the same list with its results attached,
kept because *how* each line was run is now part of the record. Two things the earlier checklist fixed, and a third it
could not have known: the binary is the Windows `.exe` (there is no Linux `aws`); **`--profile` has no default to fall
back to here**; and `--region` is not one value for all services — `route53domains` lives in `us-east-1`, not A's region.

```text
AWS="/mnt/c/Program Files/Amazon/AWSCLIV2/aws.exe"        # measured: aws-cli/2.36.44, reachable from WSL
R="--region ap-southeast-1"                              # NOT optional — see the NoRegion note below
"$AWS" --profile loey $R sts get-caller-identity         # ✓ rc=0 07:21:13Z — but arn is iam::<ACCOUNT>:root
"$AWS" --profile loey $R route53 list-hosted-zones       # ✓ rc=0, and the answer is [] — no zone exists
"$AWS" --profile loey $R ec2 describe-availability-zones          # ✓ 1a/1b/1c available — ALB two-AZ rule holds
"$AWS" --profile loey $R rds  describe-db-engine-versions --engine postgres   # ✓ 18.6 offered — exact parity
"$AWS" --profile loey $R ecr  describe-repositories               # ✓ rc=0, [] — the registry must be created
# …and the two the list did not have, both added by the run: route53domains list-domains (us-east-1) → [] and
# acm list-certificates in us-east-1 + ap-southeast-1 → both [].
# The two ACM certificate requests are AWS *writes*, so they sit behind pk:ship and an explicit owner go-ahead —
# not behind this list. They are also now behind a decision this list surfaced, not behind a credential.
```

**`--region` is on every line because the first execution of this list failed without it** (2026-09-14 06:50 UTC). The
`loey` profile's config section carries only `login_session` — no `region` key — so even `sts get-caller-identity` and
`route53 list-hosted-zones`, both of which are *global* endpoints, abort with `An error occurred (NoRegion)`. `ap-
southeast-1` is A's answer, so passing it explicitly is not a new decision; the earlier draft of this list carried
`--region` only on the three `describe-*` calls and its first two commands were therefore unrunnable as written. The
second attempt, with the region supplied, returned `Your session has expired. Please reauthenticate using 'aws login'`
— and after the owner authenticated, every line above ran. **The apex the read was supposed to return does not exist**,
so the gate that remains is not a credential and not a command: it is a decision about where this app's hostname lives.

`list-hosted-zones` rather than the spec's `list-hosted-zones-by-name` is a deliberate one-command deviation that earned
itself the moment it ran: the `-by-name` form takes the apex as an argument, so with no zone in the account it would have
returned an empty list *for whatever string was guessed* and looked like a successful check. Listing everything is what
turned "not found" into "there is nothing here" — the difference between a wrong answer and a missing premise.

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

**Resume condition — met in form 2026-09-14**: the seven answers are on disk in spec §12, the owner chose to proceed to
T-02, and this record is the vehicle. Provisioning did not begin on answers held only in chat — they were written first.
The one chat-only fact that remains, B's zone apex name, *cannot* be applied from chat at all; it is T-02's checklist
item 0.

**Correction at the boundary (08:30 UTC, same day):** that last sentence is now false in its premise. Item 0 has run —
96 seconds of reads, 58 calls — and what it returned was not an apex name but **the absence of one**, plus a price list. The
residual is therefore not "a fact that cannot be applied from chat" but **three decisions only the owner can make**, named in
the section below. The sentence stands as written *about the 07:03 state of the record*, and is annotated rather than deleted.

**Correction 3 (2026-09-14 14:45 UTC, the reconciliation pass):** the three owner decisions named there have now *collapsed into two*,
 because the payment constraint settled the hostname itself. What remains owned by the human is §14.6's list — buy H-B's $0.50/mo
 **before** the first deploy (it is the cheapest thing that restores `D-M5-3`), and whether root-only identity is acceptable for the
 first write — plus **the three console numbers**, which are not a decision but a read this session cannot perform.

## T-02 hostname: the recommendation, under the owner's blanket delegation (2026-09-14 08:30 UTC)

The instruction was "do what you recommend", so the four candidates in §12 B are resolved here rather than re-asked. Two of
the four were eliminated by measurement, not taste.

**Recommended: option (1) — register a `.dev` in this account, ~$23/yr all-in.**

| | |
| :--- | :--- |
| Domain | `$17.00`/yr, register == renew (`.dev`); `.com`/`.org` `$16.00`, `.app` `$20.00`, `.io` `$71.00` |
| Hosted zone | `$0.50`/mo = `$6.00`/yr, charged at creation, free if deleted inside 12 h |
| **Total** | **≈ `$23.00`/yr**, of which **$17 cannot be paid with promotional credits** |
| Two ACM certificates | `$0` — public certificates are free; the cost is the DNS validation wait, not money |
| Excluded from the recommendation | `.io` at 4× `.dev` for the same job; `.click` at $3, cheapest but a spam-associated suffix |

Why (1) over (2) and (3), *measured*: this account has no zone and no domain (reads 3, 5), and **the repository shows no
evidence that any owned domain exists anywhere** — a repo-wide scan for domain-shaped literals returned only third-party
strings (`mcr.microsoft.com`, `tsconfig.app.json`), `gh api repos/…` has no `homepage`, and there is no Pages custom domain
(the Pages endpoint 404s). Options (2)/(3) both presuppose a domain that nobody has yet demonstrated. **If one does exist**
at Namecheap/Cloudflare/Gandi or in a second AWS account, say so and (2) or (3) immediately beats (1): the $17 disappears and
only the $0.50 zone remains.

Why not (4) — and a retraction worth its own line. I previously recorded that dropping the custom hostname "breaks the
`__Host-` cookie shape that decision B was protecting." **That was overstated.** `__Host-` requires `Secure` + `Path=/` +
no `Domain` attribute, and none of those three requirements needs a registrable domain; a CloudFront default-domain
distribution terminates TLS on a valid AWS certificate, so a host-only cookie is settable there. What option (4) actually
destroys is **this milestone's subject matter**: no ACM DNS validation, no two-region certificate constraint, no named exact
Origin allowlist, and a public URL that changes when the distribution is recreated. M5 exists to teach that machinery. Option
(4) keeps the bill at $0 and deletes the lesson — **that clause was wrong, and part (b) of it was wrong too**: option (4) zeroes only the *domain*, while `docs/m5-infra-plan.md` §7 prices the topology the domain sits inside at **~$75/month**, so the honest framing is a **scope cut worth ~$75/mo of avoided spend, not a $0 deployment** (see “T-02, part 3” below and DEBT-26). It should still be chosen *as* a scope cut, not sold as a cookie problem.

### What remains the owner's, and why delegation cannot close it

1. **The apex name.** Two obvious-form names measured `UNAVAILABLE`; two shorter personal-form `.dev` names measured
   `AVAILABLE` (names held out of the repo by the redaction rule; they are in the session record). Choosing a name is
   choosing a brand and a WHOIS identity — not a judgement an agent can make *for* someone.
2. **The card.** Registration is excluded from promotional credit. Spending real money on someone else's account is not
   inside any "do what you recommend" reading that survives inspection.
3. **`pk:ship` + the explicit write go-ahead.** Still required, unchanged by this document.
4. **Root vs scoped identity.** Reinforced by a new finding: `aws.exe` calls are not concurrency-safe against a
   `login_session` cache, so a fanned-out ladder self-revokes. A long-lived scoped identity fixes that *and* answers the
   least-privilege question. Recommendation: **create the scoped deploy identity before T-03, not during it** — that is an
   IAM write, so it waits for the same go-ahead.

### What registration asks that a hosted zone does not

Beyond money: `register-domain` requires **real registrant contact data** (name, postal address, phone, email) that is
transmitted to the registry and, depending on TLD, displayed in WHOIS — it must therefore be supplied by the owner and is
deliberately not templated into this record. ICANN requires a **registrant email verification click**, and an unverified
contact can lead to suspension; `.dev` additionally sits on the Chromium **HSTS preload list**, so browsers will refuse
plain HTTP for it before this app decides anything (documented registry property, *not* measured in this pass — verify at
apply). Availability from `get-domain-suggestions` is cached and approximate: `check-domain-availability` immediately before
`register-domain` is the gate, and availability does not survive being read.

### Prepared ladder — **do not execute a line of it** before owner items 1–4 above are answered

Every step is **serialised**: no `&`, no `wait`, no fan-out — see the `login_session` concurrency finding. Step 3 is the
only irreversible, money-spending line; everything after it is reprovisionable or deletable.

```text
AWS="/mnt/c/Program Files/Amazon/AWSCLIV2/aws.exe"
APEX=<owner's chosen apex>            # e.g. something.dev — never committed before the owner picks it

# 0. re-auth (owner completes the browser; region is required or the command hangs on a prompt)
"$AWS" login --profile loey --region ap-southeast-1
"$AWS" login --profile loey --region us-east-1        # registrar + the CloudFront cert both live here

# 1. prove the session before the first write, then re-prove it after any gap > ~1 h
"$AWS" --profile loey --region ap-southeast-1 sts get-caller-identity

# 2. availability LAST, immediately before registration — cached suggestions lie
"$AWS" --profile loey --region us-east-1 route53domains check-domain-availability --domain-name "$APEX"

# 3. SPEND ~$17/yr, IRREVERSIBLE without repurchase; needs registrant contact fields (owner-supplied, not templated)
"$AWS" --profile loey --region us-east-1 route53domains register-domain \
  --domain-name "$APEX" --auto-renew <registrant/admin/tech/contacts>   # confirm before running

# 4. hosted zone ($0.50/mo, charged at creation) — registration does not create it for you; verify at apply
"$AWS" --profile loey --region us-east-1 route53 create-hosted-zone \
  --name "$APEX". --caller-reference "$(date -u +%Y%m%dT%H%M%SZ)-t02"

# 5. two certificates, two regions, one SAN each — D-M5-10's shape; both free, both DNS-validated
"$AWS" --profile loey --region us-east-1     acm request-certificate --domain-name "$APEX" \
  --validation-method DNS --key-algorithm RSA_2048 --idempotency-token t02-cf
"$AWS" --profile loey --region ap-southeast-1 acm request-certificate --domain-name "$APEX" \
  --validation-method DNS --key-algorithm RSA_2048 --idempotency-token t02-alb

# 6. one CNAME record per cert (change-resource-record-sets, 2 writes), then poll until both read ISSUED
"$AWS" --profile loey --region us-east-1 acm describe-certificate --certificate-arn <ARN> \
  --query 'DomainValidationOptions[0].{n:ResourceRecordName,v:ValidationRecord}' --output json
```

Two guardrails inside the ladder itself: `--idempotency-token` makes a retried step 5 reuse the pending certificate instead
of minting a second one, and step 6 must read `ISSUED` — not `PENDING` — before T-04's CloudFront distribution may reference
the ARN. `register-domain` is also the point at which the **root-identity** question stops being theoretical: it is a
`route53domains:*` action taken as account root, on a billing-bearing account, and the least-privilege answer is cheaper to
apply one step earlier than one step later.

---

## T-02, part 3 — the owner has no card: the $0 answer (2026-09-14 10:04 UTC)

Owner: apex **chosen**, “**i dont have money to attach real card is there alternative free???**”, “**go with your
recommendations**”. One sentence moved the binding constraint from *information* to *money* — and the money was never
in the domain.

### The correction, before anything else

The section above asserts option (4) “keeps the bill at $0”. **False as written**, and now corrected in place: it
zeroes the $17 registration and $0.50 zone while the same plan calls for ~$75/month of always-on resources. The $17/yr
domain that consumed two passes and ~58 read-only calls is **23% of a single month** of the architecture it enables.
Recorded as **DEBT-26 (P1)** — a cost table nobody was ever asked whether it could pay.

| §7 line | as written | proposed | why |
| :--- | :--- | :--- | :--- |
| NAT gateway | ~$32 | **$0** | the table’s own footnote three lines below already permits public subnets |
| ECS Fargate (0.25 vCPU / 512 MB, 1 task) | ~$12 | ~$9–12 **[verify-at-apply]** | scale desired-count to 0 between sessions |
| RDS `db.t3.micro` | ~$13 | **~$2** **[verify-at-apply]** | `stop-db-instance` suspends compute for up to 7 days; storage keeps billing |
| ALB | ~$16 + data | ~$0.02/hr **[verify-at-apply]** | cannot be stopped — delete it between sessions, or accept it |
| Secrets Manager | ~$0.40 | **$0** | SSM Parameter Store `SecureString`, same `secrets` block in the task definition |
| CloudFront + S3 | ~$1.50 | ~$0 | 1 TB + 10M requests always-free; the bundle is 61 kB gzipped |
| **Total** | **~$75/mo** | **~$43/mo always-on, or ~$1–3 per practice session** | teardown discipline, not a cheaper region |

**The caveat that governs that whole column:** the CLI session died at the re-probe (`sts get-caller-identity` →
`INVALID_REQUEST`), so **no unit price in it was measured.** Every row is a thing to measure next, not a number to
believe — which is the same discipline that caught §12 B’s $12 estimate being 33–42% low.
### The three hostname routes, priced and named

|  | route | cost/yr | keeps | deletes |
| :-- | :--- | :--- | :--- | :--- |
| **H-A** | CloudFront default domain `dxxxxxxxxxxxxx.cloudfront.net` | **$0** | `__Host-` cookies (`Secure`+`Path=/`+
no `Domain` — none of the three ever needed a registrable domain), ECS + ECR + S3/OAC, cache behaviours, SSE through the
edge, origin allowlisting **via the CloudFront managed prefix list**, migrations-at-boot | ACM DNS validation; the
us-east-1-vs-workload-region cert split; apex→app redirect; Route 53 delegation; **a public URL that survives recreating
the distribution** |
| **H-B** | free third-party subdomain whose **nested `NS` record delegates into a Route 53 hosted zone** | **$6** | H-A
’s deletions, nearly all of them: real ACM DNS validation inside a zone this account controls, the two-region cert
constraint, named exact origins, stable public hostname | the registrable-domain branding — and it **puts this
milestone’s cookie boundary on infrastructure a volunteer project can revoke or rename** |
| **H-C** | register a `.dev` | **$23** | everything | **blocked**: promotional credit cannot pay registry fees, and
there is no card |

H-B is not a guess about somebody’s marketing copy — it was checked against their generator. `is-a.dev`’s
`dnsconfig.js` emits `A AAAA CAA CNAME DS MX NS SRV TLSA TXT URL`, and records are proxied only if the registration JSON
asks: `var proxyState = data.proxied ? CF_PROXY_ON : CF_PROXY_OFF`. **An unproxied nested `NS` therefore resolves
publicly**, which is exactly what ACM DNS validation requires. Two unknowns remain and both are cheap: whether a
*nested* file name (`app.<mine>`) is accepted by their human reviewers at all, and whether a login-capable app fits
their “personal websites” framing — their README also warns “*Do not use AI to generate your request, it WILL
always get it wrong and will delay you getting a domain*”, which is an instruction to the owner and not something to
outsource. **`eu.org` — the other classic free option — could not be verified this pass**: `help.eu.org` refused to
connect and `eu.org` served an **expired certificate**, so it stays a candidate and not a recommendation.

### The decision, taken under “go with your recommendations”

**H-A now** so T-01 → T-03 proceed with $0 committed. **H-B filed in parallel as a non-blocking side quest**, because
its wait time is somebody else’s calendar and it must not sit on the critical path. **H-C the moment a card exists**
— and then it must be a ~30-minute change, not a migration. That last clause is now a requirement on T-01 rather than
a hope: **the apex lives in exactly one config value**, threaded through CloudFront aliases’ origin behaviour, the ACM
ARNs, `Cors__AllowedOrigins`, and the JWT `iss`/cookie scope, so that paying $17 later is a redeploy and not a redesign.

### Two things deliberately not done

1. **The chosen apex is not written into this repository.** `gh repo view --json visibility` → **PUBLIC**, the name is
   **unregistered**, and `check-domain-availability` output does not survive being read. Committing the string would
   advertise a $17 snipe to whoever reads the repo — the one failure mode here that is cheaper to avoid than to
   recover, since recovery costs the money that is the whole reason for the decision. The name lives in the session
   record; it enters the repo in the commit that registers it.
2. **No AWS write, and steps 2–6 of the ladder are withdrawn** (step 3 was their only irreversible line). Related, and
   unresolved by me on purpose: `aws.amazon.com/free` states the Free plan carries “**up to $200 in credits**” for
   “**up to 6 months**” with “**no charges and no surprise overages**”, and that beyond always-free limits
“**credits are automatically applied to cover the costs**” — which means **existing credit may already pay for
   H-A/H-B compute with no card attached**, since only *registration* is credit-excluded. Whether that is this account’s
   reality is **not something to assert**: the AWS billing user-guide 404’d on three attempted URLs this pass, so the
   gate is now three console reads — **plan type, remaining credit balance, payment methods present or absent**.

### Revised ladder status

Steps 0–1 (re-auth, prove session) stay. **New step 2, before any resource is created:** read the three console numbers,
then measure every `[verify-at-apply]` figure above with serial `aws pricing` calls — serially, because `aws.exe` calls
sharing a `login_session` cache are not concurrency-safe and a fanned-out ladder self-revokes mid-deploy.

## DEBT-27 reconciliation pass (2026-09-14 14:45 UTC) — the spec and the record were made to agree, and one security decision came back reversed

**Why this section is a record and not just a diff.** The pass was routed from `checkpoint-003` §8 as *"merge, then `pk:plan` the DEBT-27
divergence"*. The merge happened (**#91** → `9f14671`, `2026-09-14T14:24:13Z`, re-read before the pull and asserted an ancestor of this
branch afterwards), and the divergence was real: §12 B recorded a hosted zone the account does not have, §6's `T-02` row asked for two
certificates nobody can request, and the owner's actual answer lived only here.

**What was checked before anything was written, and what it changed.** The claim worth attacking was not *"is the spec stale"* — it was
*"does H-A really cost the encrypted origin hop, or was that an unexamined inference?"* The optimistic hypothesis (CloudFront exempts ELB
origins from certificate validation, so TLS-to-the-target survives with a self-signed cert and no domain) was researched against AWS's own
Developer Guide and is **false**: CloudFront *"verifies that the certificate was issued by a trusted certificate authority"*, *"You can't use
a self-signed certificate for HTTPS communication between CloudFront and your origin"*, no ELB exemption exists anywhere in the guide, and
`https-only` against a certificate-less ALB returns **502**. So the reversal stands, and §14.4 exists because of it. The second finding cut
the other way — the mechanism §12 B named for keeping the ALB closed was **wrong in detail**: the AWS-managed list is
`com.amazonaws.global.cloudfront.origin-facing` (global, weight 55 of 60 rules), the regional `com.amazonaws.<region>.cloudfront.origin`
form is **not documented**, and AWS's primary restriction method is a **shared secret custom-origin header with a default-`403` listener
rule** — with AWS's own warning that HTTPS is what protects the header's value, which HTTP:80 does not provide. Both are written into
§14.3's citation records with publisher, title, URL and access date, per I-A1-6.

**Spec surfaces amended (two commits, `7e2060b` then `776732b`):** §0's forward pointer · `D-M5-3` (struck-through amendment block with its
expiry condition) · §3.1's paragraph and its Route 53 / ACM / CloudFront rows · §3.3's first and last bullets · §6 `T-02` (restated, not
deleted) and `T-09` (HTTP:80 + header check, with the proof rewritten as header/no-header pairs) and `T-03` (endpoints, not NAT) ·
§12's heading, its *"B is the long pole"* preamble, and B's Answer cell + mechanism correction · **new §14** (Amendment A1: decision
record `DECISION-m5-aws-deployment-010`, six citations, two `UNCERTAINTY` records including the region contradiction between the ELB guide
and the CloudFront guide, the FMEA row, invariants **I-A1-1…6**).

| Item | Status at this boundary | Evidence |
| :--- | :--- | :--- |
| **DEBT-27** | **closed on disk, `main`-pending** — this row's own closure condition is the amendment *merging*, not existing | both spec commits above; CI on this branch's head carries the gate verdict |
| **T-02 step 0** (measure the account) | **done**, merged (#86–#88) | `m5-deploy-log.md`, 58 read-only calls, zero writes |
| **T-02 decision** (hostname) | **done** — H-A, $0, H-B parallel, H-C on a card | §14.2/§14.3, owner 2026-09-14 |
| **T-02 remaining substance** | the three `[verify-at-apply]` price cells **re-measured serially behind a live session**, and `UNCERTAINTY-010-b`'s region question is deferred to whoever lands H-B/H-C | §14.2's caveat: no unit price in the earlier table was measured, because the CLI session died at the re-probe |
| **M5 writes** | **still forbidden.** `pk:ship`, the human merge gate, and **I-A1-5** (no write before the three console numbers) all stand, unchanged by §14 | §9/R-9.2, I-A1-5 |

**Exactly one next action:** **T-03 on paper** — the CDK stack shape for VPC + ECR + log group under §12 D (bought `/20`, endpoint egress,
**no NAT**), priced from `[verify-at-apply]` cells and §14's citations rather than from `docs/m5-infra-plan.md` §7, which DEBT-26 indicts.
It changes no platform, asks for no credential, and is the first task that can start without an owner answer. **The owner's two §14.6
decisions are requested alongside it and do not block it.**

**Zero AWS calls in this pass** — zero reads, not merely zero writes: a citation pass needs no session, and the standing instruction is
that the session is not claimed live. No source file, no config, no dependency: `runtime deps 3 / dev deps 19`, unchanged since M0.

## T-03 on paper (2026-09-14 15:25 UTC) — the only M5 task that needs no credential, no card, and no owner answer

**Why this one.** `checkpoint-003` §8 and then §3A's live bullet both named it: T-03 (VPC, ECR, log group) is the
first provisioning task whose *design* is unblocked even while **I-A1-5** blocks its *execution*. Writing it down is
also the cheapest way to find out whether §12 D's "no NAT, endpoints instead" answer is actually *sufficient* — that
question gets decided by a document and confirmed by one task reaching `RUNNING`, and the gap between those two is
where first deploys die.

**Produced:** [`docs/specs/2026-09-14-spec-m5-t03-network.md`](../specs/2026-09-14-spec-m5-t03-network.md) —
address plan (`10.0.0.0/20`, six `/24`s, four deliberately held), three route tables with the fail-closed claim stated
as a test, the four-SG matrix including **the rule-weight trap** (the CloudFront prefix list costs **weight 55 of 60**
rules, so `sg-alb` has about five slots for its lifetime and a deploy can fail on arithmetic), the **endpoint set**
with eight rows and the one row that decides the design (whether the ECS agent can register a task with no egress
beyond its endpoints), the CDK layout with **each dependency justified or refused**, eight `assertions`-based template
tests each named for the mistake it catches, the serialised `[verify-at-apply]` commands, and the bootstrap/destroy
ordering.

**Boundary stated where it bites:** a synth test never dials an endpoint, so the suite pins the *shape* and cannot
prove the *sufficiency* the design rests on. Anyone reading §8 of that document as proof of §5 has read the wrong
section as the guarantee — the failure mode this repository has already documented twice.

**Two things the design changed about the milestone rather than about itself.** `apexValue = null` under H-A is now an
explicit supported state with its own test (**T-N7**), because that is what makes §14.6's "H-C is a redeploy, not a
redesign" a property with a check behind it instead of a wish; and **VPC flow logs were left out deliberately**, named
as open item **O-3** — §14.4 lists them as the detector for the accepted cleartext risk, so omitting them is a decision
and not an oversight.

**Repaired on the way, because the reconcile reflex keeps paying:** spec §3.1's VPC row still specified
`private with NAT (tasks)` and still called §12's VPC question open, both superseded by §12 D hours earlier — the
DEBT-27 shape one cell smaller. Fixed in its own commit, with §6's `T-03` rationale amended to say what networking now
defends: **two** unencrypted edges, not one.

**§11 was filled after the first draft, by a primary-source pass at 18:30 UTC — and the pass failed, so the section's
content is the failure.** Both AWS pricing pages returned HTTP 200 to this machine with **zero numbers** (the
per-region tables are client-rendered); two follow-up static-doc reads hit the session's web ceiling. What the draft
had asserted at its own §0 — that §11's prices "came off AWS's own pages on 2026-09-14" — was a citation with no read
behind it, and §11.1 retracts it at the line, house style. Every price row is now `[verify-at-apply]` with the serial
read that closes it, and §11.2 argues the part that matters: **no number forms this design** — the endpoint-vs-NAT
choice is privacy-argued (§12 D) and test-enforced (T-N1), so the failed pricing pass changes no CIDR and no SG rule.
The pass also grep-verified that the parent §12 D's `~$0.01105/hr` and `~$32/mo` figures both trace to the indicted
`m5-infra-plan.md` §7 table — directionally right, provenance-wise unusable.

**Status:** design only. No `cdk`, no dependency installed, **zero AWS calls — zero reads as well as zero writes**
(the 18:30 pass read two *pricing web pages*; the control plane got nothing, then and now). It
executes when **O-1…O-4** clear: H-B before-or-after, root-vs-scoped identity, flow logs, and the three console
numbers. There is no agent-side task behind it that touches a platform: T-04 and T-05 depend on T-03 being provisioned,
so the queue belongs to the owner until those four answers arrive.

## T-03 paper merged — #93 (`8a10b69`, 18:33:25Z) — and the race that merge lost to rule eleven

**Merged as measured:** `gh pr view 93` → `MERGED 2026-09-14T18:33:25Z`, merge commit `8a10b69`;
`merge-base --is-ancestor 7f107ee origin/main` → **YES** — all four ladder commits (engine v1.7.0 sync; design
spec + §6's link; this record; the 18:35 projection) are on `main`. The task stays `in_progress` because T-03's
*execution* and T-04…T-17 remain, gated on **I-A1-5** and **O-1…O-4**.

**The race, recorded because AGENTS.md's rule eleven is a list of failures and this is its newest entry.** The head
assertion ran at *create* — `OPEN`, head `7f107ee` equal to `git rev-parse HEAD`, both true at read time — and then
the human merged inside the two-minute window in which the next commit was naming #93 in `docs/STATE.md`. That
commit's push printed **`* [new branch]`** (the merge had deleted the remote head — the exact signal the PR #60
receipt records) and its post-push assert contradicted: PR head frozen at `7f107ee`, local head `17c366b`, naming
commit stranded on the recreated orphan branch. **The half of rule eleven that was skipped was the re-read
immediately before the push; the half that ran is what caught it.** The stranded commit is superseded by STATE.md's
18:45 entry rather than rescued, and the amended form at future boundaries: **one push per boundary; the PR number
lives in the PR and enters the projection at the next boundary, never in the branch that carries it.**

**Queue state:** the agent side of M5 is empty. Next actor: the owner — O-1…O-4, in one reply; `m5-deploy-log.md`
starts filling only after that, with the first reads serial (I-A1-4). This section is the session's checkpoint
content; the Session-Endurance rule added by v1.7.0 names this boundary as the moment to prefer a fresh session.
