# Technical Design Document (RFC): M5 — AWS Production Deployment (measured)

> **Status map**: `Author`, `Status`, `Created`, `Target Release` are **Required**. Planning inputs are **Required**
> (Minimal depth). The deployment/none-schema scope means the Full-Planning and most architecture sections are
> **Not applicable** with a reason. Material tech decisions record decision + rationale.

- **Author**: Assistant (`pk:plan`), re-derived from repository, code and container measurements
- **Status**: **Ready for owner ratification** — nine decisions recorded in §2, seven owner questions open in §12
- **Created**: 2026-09-13 · **Revised**: 2026-09-13 (same day; the draft's seven wrong claims are listed in §13)
- **Target Release**: M5 — AWS Deployment (Level 3, `pk:ship` + human approval)

<a id="PLAN-m5-aws-deployment"></a>

---

## 0. How to read this document

Every factual statement below carries the command or the `file:line` that produced it, measured in
`/home/heyloey/personal/job-tracker` on branch `docs/state-m5-deploy-readiness-merged` on 2026-09-13. Where a number
cannot be measured from a repository I own (AWS service limits, managed-policy contents), it is marked
**[verify-at-apply]** and a task exists to measure it against the live account instead of against documentation prose.

> **Amended once since the owner's answers, and §14 is the amendment.** It reverses part of `D-M5-3`, and it is the only section of
> this document that was written against AWS's own text rather than against the account: §14.3 carries the publisher, title, URL and
> access date behind each claim, and §14.6 states the one place two AWS pages contradict each other. **Read §14 before trusting any
> certificate or TLS statement in §2, §3.1, §3.3, §6 or §12** — those sections were amended in place with strikethroughs pointing here.

The morning draft of this spec was written from an inherited planning record whose inputs had been lost. That record
asserted infrastructure existed. It did not. §1.1 is the list, and it stays in the document on purpose: the recurring
failure mode here is not missing capability, it is **prose that outlived the thing it described** — the same class as
DEBT-11, and DEBT-13 before it.

Three items below are **already fixed**, not proposed: the container restore failure, the container port contract,
and the missing build-context filter (§1.4, B-01/B-02/B-03). The fix is in the working tree as you read this —
`git status --porcelain` lists `api/Dockerfile` and `api/.dockerignore` as uncommitted, which is stated rather
than assumed because §11 is entirely about prose that outran the thing it described.

---

## 1. Measured baseline

### 1.1 Repo reality: what does *not* exist

| Artifact | Claimed by | Measured 2026-09-13 |
| :--- | :--- | :--- |
| `.github/workflows/deploy.yml` | `docs/aws-deployment.md` §9, `docs/m5-infra-plan.md` | **Absent.** Only `ci.yml`. No workflow pushes an image anywhere. |
| `infra/docker-compose.prod.yml` | `docs/m5-infra-plan.md` §1 | **Absent.** Only `api/docker-compose.yml` (local dev, Postgres 18.6). |
| `infra/scripts/bootstrap-secrets.sh`, `deploy.sh` | `docs/m5-infra-plan.md` §1 | **Absent.** There is no `infra/` directory of any kind. |
| `.env.production.example` | `docs/m5-infra-plan.md` | **Absent.** No `.env*` file is tracked anywhere in the repo. |
| CloudFormation / CDK / Terraform | `docs/aws-deployment.md` §9 | **Absent.** `find` for `template.yaml`, `template.json`, `*.tf`, `cdk.json`, `serverless.yml` → no hits. |
| "ACM/DNS/ECR/S3/ALB/IAM stacks already created, commit `46c0e0c`" | owner context, 2026-09-13 | **No such commit** (`git cat-file -t 46c0e0c` → could not find object), and no template for those stacks to live in. |
| "`GET /api/health` (does not exist yet)" | `docs/aws-deployment.md` §5 | **Stale in the other direction** — it exists (§1.2). |
| "Extend `BootGuard` to assert `ConnectionStrings:Default`" | `docs/aws-deployment.md` §3 | **Already done** — `Auth/BootGuard.cs:33-43`, inside `EnsureSafeToStart`, above an `// M5 fail-fast` comment and *before* the `!IsProduction()` early return at 45-47, so it applies in every environment. Verified by running the image with the key blank: `System.InvalidOperationException: Refusing to start: ConnectionStrings:Default is not set.` |
| "`UseForwardedHeaders` needs enabling" | `docs/aws-deployment.md` §2 risk table | **Already done** — `Program.cs:129-143`, driven by `ForwardedHeaders:KnownNetworks`. |

**Conclusion**: M5 has a decision record, a task breakdown, a release checklist and a container file. It has **no
infrastructure definition and no deploy pipeline**. §6's T-02…T-05 and T-08…T-12 are new work, not
"run the scripts".

### 1.2 Runtime contract as actually built (`api/src/JobTracker.Api/`)

Complete route table, measured by enumerating `Map*` registrations in `Program.cs` plus `Auth/AuthCatalog.cs`:

| Method | Path | Auth | Notes |
| :--- | :--- | :--- | :--- |
| `GET` | `/` | public | 23-byte text marker, not the SPA |
| `GET` | `/api/health` | **public** | `200 {"status":"ok","database":"reachable"}` (38 bytes) when `CanConnectAsync` succeeds. When it fails the handler returns a bare `Results.StatusCode(503)`, and `UseStatusCodePages` + `AddProblemDetails` fill the empty body: measured **`503`, `application/problem+json`, 172 bytes**, `{"type":"…rfc9110#section-15.6.4","title":"Service Unavailable","status":503,"traceId":"…"}` — i.e. **not** `{"status":"degraded","database":"unreachable"}`, which is what the first draft of this table claimed. No command timeout is configured anywhere (`grep CommandTimeout api/src` → no hits), so a DB that *refuses* the connection answers in 0.09 s (measured); a DB whose packets are *dropped* instead holds the request for Npgsql's connect timeout, documented as 15 s but **not measured here** — that is the shape that would meet CloudFront's origin-response ceiling (§4.3). Outside `SessionGate.IsProtected` (`Auth/SessionGate.cs:47-49`), so it answers with no cookie. |
| `GET` | `/api/applications` | session | collection |
| `GET` | `/api/applications/{id}` | session | single read |
| `GET` | `/api/applications/events` | session | **SSE**, `text/event-stream`, no `Content-Length` |
| `POST` | `/api/applications` | session + CSRF | |
| `PUT` | `/api/applications/{id}` | session + CSRF | optimistic concurrency via `revision` |
| `DELETE` | `/api/applications/{id}` | session + CSRF | |
| `POST` | `/api/auth/login` | public | the only credential-accepting route |
| `POST` | `/api/auth/logout` | session | |
| `GET` | `/{*path:nonfile}` | public | **dev-only** SPA fallback: `MapGet` of a catch-all inside `if (app.Environment.IsDevelopment())` (`Program.cs:224` opens the `IsDevelopment()` block, `285` registers the catch-all), so Production registers no fallback at all. There is **no `MapFallbackToFile` anywhere in the file** (`grep -c` → 0), contrary to the implication in `docs/aws-deployment.md` §6 |

Measured invariants that constrain the topology:

- **`/live`, `/ready`, `/healthz` do not exist** and there is **no `MapHealthChecks`** anywhere in `api/src`. Both the
  planning record and the first draft of the release checklist probed `/health`. The only health surface is
  `/api/health`.
- **Cookies are unconditionally `Secure`** — and they are **hand-written `Set-Cookie` headers**, not the framework's
  cookie-policy options: `Auth/AuthCatalog.cs:126-127` emits
  `__Host-JTSession={id}; Secure; HttpOnly; SameSite=Lax; Path=/` and 135-136 emits the CSRF cookie from
  `Antiforgery.CookieAttributes`, which carries the same `Secure`/`Path`/`SameSite` and no `Domain`. `grep
  CookieSecurePolicy api/src` → no matches, so anyone looking for `CookieSecurePolicy.Always` (as the first draft of
  this row did) will not find it. No HTTPS, no login, in any environment; the dev loop survives only because browsers
  treat `http://localhost` as a secure context.
- **Both cookies are `__Host-` prefixed with no `Domain` attribute** → host-only and same-site. Putting the SPA on
  `app.example.com` and the API on `api.example.com` is **not deployable as written**: `__Host-` forbids `Domain`, and
  `SameSite=Lax` will not attach the cookie to a cross-site XHR.
- **`EventSource` is constructed without `withCredentials`** — `src/data/httpApplicationRepository.ts:223`. Same-origin
  that is fine; **cross-origin it sends no session cookie**, so the stream route refuses it. This closes the
  "API on its own subdomain" option without client-code changes.
- **The SPA is not served by Kestrel in Production.** The dev-only fallback exists so `npm run dev` and the API form a
  one-stop local loop. Any statement that the API "serves the SPA in prod" describes Option B, and Option B is not
  what is built.
- CORS binds `Cors:AllowedOrigins` as a **string array** (`Program.cs:106`); the comment block at 100-105 records the
  failure mode it was written for — an unread key yields `[]`, and "no CORS policy at all" means "every cross-origin
  request silently fails". Under the single-origin topology of §2, an **empty array is correct**, which makes
  `docs/aws-deployment.md` §3's proposal to have `BootGuard` *assert non-empty* `Cors:AllowedOrigins` a change that
  would reject a valid production config. Dropped; §12.2 keeps it out.

### 1.3 The frontend's build-time input, and the trap inside it

`src/data/applicationStore.ts:29-45` picks the repository implementation from one variable:

```ts
function configuredApiBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL
  return typeof configured === 'string' ? configured.trim() : ''
}
// createApplicationStore(): baseUrl !== ''  →  HTTP adapter
//                            baseUrl === ''  →  localStorage / in-memory seed repository
```

Two consequences, and the second is the most dangerous property of the entire deployment:

1. **The bundle is environment-specific.** Vite inlines `import.meta.env.*` at build time, so `dist/` cannot be
   promoted between staging and production — the SPA must be **rebuilt** per environment (§3.5, T-10 and T-11).
2. **A production build with no `VITE_API_BASE_URL` silently never calls the API.** It renders, it persists, it feels
   correct — to Web Storage on the visitor's device, and every §7 probe that only checks "the page loaded" passes.
   Same shape as the CORS bug `Program.cs:100-105` documents, on the other side of the network, and currently
   **unguarded**; the guard is T-11 and §9.2's item 1.

The value that makes same-origin work with no absolute URL is a single slash: `VITE_API_BASE_URL=/`. It survives the
`!== ''` test, `createHttpApplicationRepository` strips trailing slashes to `root = ''` (line 231), and every call
becomes `fetch('/api/…')` / `new EventSource('/api/applications/events')` — relative to the document origin, therefore
same-origin, therefore cookies attach and CORS stays inert. This is a **deploy contract** and it must be asserted in
CI, not remembered (T-11).

### 1.4 Container: what was broken, and what is now true

Both defects were **reproduced** from a clean build context (`docker build -f api/Dockerfile api`), so neither is a
reading of the file — each is an observed exit status.

**B-01 — restore could not run (CLOSED).** The `build` stage copied exactly one file before `dotnet restore`. With
Central Package Management on .NET 10 (`api/Directory.Packages.props`: `ManagePackageVersionsCentrally=true`,
DECISION-m3-backend-api-003), the `.csproj` declares `<PackageReference>` items with **no** `Version`; NuGet walks up
from the project to find the props file, finds no copy in the restore layer, and fails all four:

```
error NU1015: The following PackageReference item(s) do not have a version specified:
  Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design,
  Microsoft.EntityFrameworkCore.Relational, Npgsql.EntityFrameworkCore.PostgreSQL
```

Local builds hid this because they run from the repo root, where the walk succeeds. Fix: copy
`Directory.Packages.props` before the project file. **Verified after the fix** — restore `9.0s`, publish `14.0s`,
`JobTracker.Api -> /app/publish/` — both from that one rebuild.

**B-02 — the advertised port was not the listening port (CLOSED).** `EXPOSE 5000` and a probe of
`http://localhost:5000/api/health`, while the base image sets the listening port to 8080:

```
$ docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 env | grep -i aspnetcore
ASPNETCORE_HTTP_PORTS=8080
ASPNETCORE_URLS=http://+:8080
```

Kestrel bound 8080 while the image declared 5000 and probed 5000. A refused connection is a failed health check, so
the container reported `unhealthy` forever. Worse: under `--network host` on a machine that already had a listener on
5000, the probe **answered from the wrong process**, which is exactly how this stayed invisible for as long as it did.
Fix: the listening port is declared in the image (`ENV ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`) and the probe is
`http://127.0.0.1:8080/api/health` — `127.0.0.1`, not `localhost`, so it does not depend on resolution order inside
the task's network namespace. **8080 is now the contract** for §3.2, §3.4 and §4. Changing it later means editing one
file.

**B-03 — no build-context filter (CLOSED).** `COPY . .` had no `.dockerignore`, so host `bin/`/`obj/` trees were
uploaded and merged into the image's source directory, making the build neither clean nor reproducible — the general
class DEBT-11 documents. `api/.dockerignore` now excludes `**/bin/`, `**/obj/`, `**/*.user`, IDE directories,
`tests/`, `docker-compose.yml`, `README.md`, `dotnet-tools.json`.

**Closed after that draft, and the image has been rebuilt and run to prove it.** Two of the four "left open" items are
now resolved, and the resolution is load-bearing for §3.4 and D-M5-7:

```
docker build -f api/Dockerfile -t job-tracker-api:m5fix api     → exit 0, 218 MB
docker inspect --format '{{.Config.User}}' job-tracker-api:m5fix  → 1654:1654
docker inspect --format '{{.Config.Healthcheck}}' …               → <no healthcheck>
ps -eo pid,user,args (in-container)                             → 1  app  dotnet JobTracker.Api.dll
listening sockets (in-container)                                → 172.17.0.4:8080, owned by uid 1654
GET /  (no cookie)                                              → 200 "JobTracker API is running"
GET /api/health (no cookie, DB up)                              → 200 {"status":"ok","database":"reachable"}
GET /api/health (no cookie, DB down)                            → 503 application/problem+json (172 bytes, 0.09 s)
GET /api/applications (no cookie)                               → 401 {"type":"job-tracker/unauthorized",…}
POST /api/auth/login                                            → 204 + two __Host- cookies, Secure; HttpOnly; SameSite=Lax; Path=/, no Domain
```

- **The `curl` dependency is gone, and so is the `HEALTHCHECK` instruction.** Health is enforced *outside* the image:
  by the ALB target group in production (§3.4) and by a host-side `curl` in the local smoke test. That removes the
  package that existed only to serve an in-container probe, and it removes the fiction that ECS cares about a
  `HEALTHCHECK` line — it does not; the container instance's Docker daemon may mark a task `unhealthy` and ECS keeps
  running it. With no in-image check there is nothing to disagree with.
- **`USER $APP_UID` is applied, after `COPY`.** The base image ships `APP_UID=1654` and an `app` user; the directive is
  placed last so the published bytes stay root-owned and world-readable — the process can serve its binaries but
  cannot rewrite them. Nothing needs to be writable: the Data Protection key ring that encrypts session cookies lives
  **in the database** (`Auth/PostgresKeyRing.cs` → `data_protection_keys`, migration
  `20260912221747_AddDataProtectionKeyRing`, wired at `Program.cs:62` with the reasoning in the comment block 48-61),
  not under `$HOME`, and logs go to stdout.
  Key-ring durability across a deploy is therefore a *database* concern, not an EFS/EBS one.
- **`EXPOSE 8080` is the only port in the file.** The compose mapping and dev-script ports (below) still need aligning.

**Still left open (T-1):**

- No provenance labels, and no build arg for the git SHA (T-3 adds `GIT_SHA` as a label so a running task can be
  traced to a commit without correlating digests by hand).
- `api/docker-compose.yml` has **no API service at all** — only `db` (postgres:18.6, loopback-bound 5432) — so nothing
  in it maps a port for the API. The stale port claims are in the *docs*, not the compose file:
  `docs/m5-infra-plan.md` §4 specifies the ALB target group on **port 5000** in three places (rows for target group,
  port, and security group), and `api/README.md:33` comments the dev URL as `http://localhost:5000` while
  `Properties/launchSettings.json` binds the `http` profile to **5039**. Both contradict the 8080 contract above.
  §11 carries the corrections; T-15 owns the dev-side wording.

### 1.5 Database and migrations

- Migrations live in **`Migrations/`**, not `Data/Migrations/`: 12 files, from
  `20260909125540_InitialCreate` through `20260911110000_job-application-source-details`, including the `-074`
  data-protection key-ring migration.
- `api/docker-compose.yml` pins **`postgres:18.6`**, so "RDS PostgreSQL 18.6" is a *parity* choice with the dev
  container rather than an aspiration. **[verify-at-apply]** that 18.6 is offered in the chosen region for the chosen
  instance class, and where it sits on the RDS deprecation schedule — a major version that is merely "available
  today" can be scheduled out before this app is stable.
- The schema requires the **`citext` extension** (`InitialCreate` calls `CreateExtension("citext")`). On RDS that needs
  the extension available *and* a role with privilege to create it; the standard answer is to run migrations as a
  role granted the needed privilege on that database rather than as the plain app role. This is a **first-deploy**
  blocker, not a steady-state one, and it is the most likely reason `Migrate()` succeeds in Docker and fails on RDS.
- `pgcrypto` is created by `20260910054851_add-auth-tables` **only when DP-3 key storage is enabled** (guarded,
  idempotent), so it is conditional and needs no parameter-group assumption.
- Migrations are applied **at boot**: `Program.cs:157-160` resolves the `AppDbContext` and calls
  `db.Database.Migrate()` synchronously, after `BootGuard.EnsureSafeToStart` (155) and before `app.Run()` (310). The
  first draft of this row described a helper named `MigrateWithRetryAsync` with "bounded retries and backoff" — **no such
  code exists**; `grep -riE 'retry|backoff' api/src` returns only unrelated comments. A migration failure is therefore
  terminal, and measured twice as such: blank connection string → the named-key `InvalidOperationException`; wrong host →
  `Unhandled exception. Npgsql.NpgsqlException: Name or service not known`, with no "Now listening on" line anywhere in
  the output either way. A container that cannot migrate **cannot** serve `/api/health`, which is what makes the ECS
  deployment circuit breaker (D-M5-7) able to catch a bad schema change rather than a half-started service. That
  boot-time migration is the mechanism §8's rollback ladder relies on, and the reason "run `dotnet ef database update` as
  a separate release step" is a *change* of behaviour rather than a documentation update — see D-M5-4.

---

## 2. Decisions (each: decision, why, what was rejected)

### D-M5-1 — One CloudFront distribution, one public origin, two origins behind it

`https://app.<domain>` is the only public address the browser ever sees. CloudFront routes `/api/*` to the ALB and
everything else to the S3 origin holding `dist/`.

**Why.** Not latency or cost — §1.2/§1.3 measured three independent properties that all require a single origin:
`__Host-` cookies cannot carry `Domain`; `SameSite=Lax` will not attach them to a cross-site XHR; and `EventSource` is
built without `withCredentials`, so a cross-origin stream carries no session cookie at all. Any split-host topology
needs at least one code change and a CORS posture. Single-origin needs **zero**, and it keeps `Cors:AllowedOrigins`
legitimately empty rather than accidentally empty — the distinction `Program.cs:103-107` was written about.

**Rejected.** (a) `api.<domain>` + `app.<domain>`: three code changes (`withCredentials`, dropping `__Host-`,
`SameSite=None` plus a CSRF posture the app has never had) and it re-opens `-073`/D-3, which the repo has deliberately
left unimplemented. (b) Option B, API serves the SPA: in Production **no fallback is registered** (`Program.cs:224`),
so that needs a code change today, and it forfeits the CDN. Kept as the documented escape hatch if a CDN turns out to
be unwanted. (c) S3 website endpoint as a public origin: still no HTTPS custom domain without CloudFront.

### D-M5-2 — SSE goes through CloudFront, and only because the API grows a keep-alive

`/api/applications/events` shares the `/api/*` behaviour with caching disabled, which is CloudFront's configuration
for streaming responses (a response with no `Content-Length` is forwarded in chunks as it arrives).

**Why a code change is mandatory, not optional.** The endpoint lives in `ApplicationCatalog.cs:71-113`: it sets
`text/event-stream` + `no-cache`, writes exactly one comment frame (`": open"`), and then blocks in
`await foreach (var id in changes.Reader.ReadAllAsync(ct))` on its own channel from `ApplicationEventBus`. After that
opening frame the connection carries **zero bytes until somebody changes a row**, and there is no timer anywhere in the
API source (`grep -rn 'WaitToReadAsync\|heartbeat\|keep-alive\|Timer'` over `api/src` → no matches).

Measured against the built image rather than read off the source. An authenticated `curl -N` on
`/api/applications/events` received `: open` at t≈0 and then nothing for the full 22 s the probe ran; a second probe
that wrote a row at t≈+4 s saw `event: change` and `data: {"id":...}` arrive in the same second. Silence between events
is the steady state, not an edge case.

**And the silence survives being silent — the limiter is never the application.** A third probe held one authenticated
stream open with **no bytes in either direction** and then wrote a row from a separate client at t≈167 s. The change
frame arrived **immediately**. So nothing in Kestrel, the SSE handler, the channel, or the EF change-detection loop
puts a ceiling on stream lifetime; the only things that can are the two proxies in front of it (§4). That is what makes
this a *mandatory code change* rather than a tuning question: there is no server-side timeout to discover and align
with, and a quiet database therefore holds an idle connection through two proxies that both close idle connections.
The symptom is a stream that dies in exactly the situation it exists for: the other tab is the only one making
changes, and this tab never learns.


The fix is small: write a comment frame (`: keep-alive\n\n`) every N seconds on a background loop whose cancellation
token is the request's, so the loop dies with the request. Event clients ignore comment lines by spec. **No protocol
change, no client change.** It belongs in the API rather than in proxy tuning because raising every timeout to
outlast silence also delays *detection* of a genuinely stuck task.

**Rejected.** Raising every proxy timeout past the longest plausible quiet period (delays failure detection, holds a
connection per client, and still meets the ALB ceiling in §4); and pointing the browser at the ALB directly for the
stream only, which splits the origin that D-M5-1 exists to keep whole.

### D-M5-3 — TLS terminates at the ALB as well as at CloudFront

CloudFront↔browser uses the ACM certificate, which **must be issued in us-east-1** for CloudFront regardless of where
everything else lives. CloudFront↔ALB uses HTTPS via a second ACM certificate on the ALB's 443 listener. ALB↔target
stays HTTP on 8080, restricted by security group to the ALB's own SG.

**Why.** The `Secure` cookie flag is a browser-side statement about a response; it encrypts nothing. If the origin hop
is plain HTTP, any path that reaches the ALB directly — a leftover A record, a stale DNS entry, an IP-based scan — can
obtain a session cookie over cleartext. Encrypting the origin hop makes "direct to the ALB" useless, and it makes the
`X-Forwarded-Proto: https` the app trusts (§3.3) a truthful statement from a listener rather than an unauthenticated
guess.

**Rejected.** HTTP-only origin hop protected only by a CloudFront-source SG rule: one fewer free certificate, and it
depends on every future actor honouring a rule that is trivial to widen.

> ### ⚠️ D-M5-3 **partly reversed by §14 (2026-09-14) — the rejected option is now the selected one, and it was not a free choice.**
> The reversal's mechanism is the ALB's *name*. CloudFront validates the certificate on an encrypted origin hop, and will not accept a
> self-signed one ([CIT-A1-4]); ACM will not issue a public certificate for an Amazon-owned domain ([CIT-A1-3]); and the only name
> the ALB has under **H-A** is `*.elb.amazonaws.com`. So the 443 listener has no certificate it can legitimately present, the origin
> connection drops to **HTTP:80**, and the sentence above — *"one fewer free certificate, and it depends on every future actor honouring
> a rule"* — describes the deployed design rather than the rejected one.
> **What was NOT weakened, and is the reason this reversal is survivable:** the hop is inside the VPC and unreachable from the internet.
> The defence changes shape from *encryption* to *unreachability plus authentication*: the ALB keeps no public route to :80 except through
> CloudFront, ingress is filtered to the CloudFront origin-facing prefix list, **and** the origin sends a secret custom header the ALB's
> listener rule requires, defaulting to a fixed `403` — which is AWS's documented *primary* method, not an extra
> ([CIT-A1-5]). The `Secure` cookie is still never carried in cleartext to a browser; it is carried in cleartext between two AWS
> facilities, over a path with no route to it.
> **Expiry condition, so this is not permanent damage:** restoring the encrypted hop costs **a hostname**, and §14.2 prices the two
> routes that yield one (H-B ≈ $0.50/mo, H-C ≈ $17/yr). When either lands, **D-M5-3 revives verbatim** — this block is then the record of
> a constrained window, not a design preference.

### D-M5-4 — Migrations stay at boot; there is no separate migration step

`db.Database.Migrate()` runs **inline at boot** (`Program.cs:157-160`), after `BootGuard.EnsureSafeToStart` (155) and
before `app.Run()` (310). There is no `OnStarted` hook, no `FailFast`, and no `HostAbortedException` filter — the
earlier draft of this row described all three, and none of them exist. The mechanism differs from that description;
the consequence does not: an unhandled exception before the port is opened kills the process, measured as
`Unhandled exception. Npgsql.NpgsqlException … Name or service not known` with no "Now listening on" line.

**Why keep it.** It is already written, already ordered correctly relative to the Data Protection key ring that the
session middleware needs, and its failure mode is the one ECS understands: the task never reaches `RUNNING`, the
service's first task fails, and the circuit breaker (D-M5-7) rolls the deployment back. A separate migration *step*
would need its own task definition, its own copy of the secret, and its own VPC placement — three new things that can
drift from the app — to move work that already happens at the right moment. For a single-writer personal app where the
first deploy is also the only deploy, that is pure ceremony.

**Rejected.** (a) A one-off `aws ecs run-task` migration job before each service update: correct for concurrent
deployers, which we do not have yet, and it makes "did the schema or the code deploy first?" a manual question.
(b) `Database.Migrate()` on a lower `IHostedService`: same race, worse failure mode — a migration failure after the
port is open means the app serves 500s rather than exiting.

**Revisit when** more than one task can start against the same database at once: `desiredCount > 1` with a rolling
update that brings up new tasks before stopping old ones, autoscaling, or blue/green where both revisions point at one
RDS instance. That is the moment two runners can race the same migration, and it is the moment this decision expires.

### D-M5-5 — Tag the image with the git SHA, deploy it by digest

CI builds `job-tracker-api:<git-sha>` and also records the `sha256:<digest>` that ECR returns; the task definition
revision is created referencing **the digest**, not the tag.

**Why.** A tag is a mutable pointer; a digest is the content. `docker push` of the same tag twice replaces what the
previous tag meant, and the service's "previous deployment" (which the circuit breaker rolls back to) then points at
bytes nobody reviewed. Recording the digest in the task definition means the pair *(task definition revision, RDS
automated backup window)* is enough to reconstruct exactly what ran — which is the entire rollback story for an app
with one database and no blue/green. The tag survives for humans, because `git log` → `docker pull` is how you find a
build again in ECR.

**Rejected.** Tag-only deploys: one line fewer of CI, and it silently breaks the "previous task definition revision is
still bootable" assumption that D-M5-7 leans on. Digest-only with no readable tag: pushes the debugging cost onto every
future incident.

### D-M5-6 — Secrets Manager holds one full connection string; no automated rotation in v1

One secret, JSON `{"ConnectionString":"Account Host=...;Username=...;Password=...;Database=jobtracker"}`, handed to the
task as the **plain text** value of `ConnectionStrings:Default` (`valueFrom` pointing at the secret ARN without a
`::key` suffix), and read by exactly the code path that exists today (`Program.cs:65`, `Configuration.GetConnectionString("Default")`).

**Why plaintext-value rather than `{{resolve:secretsmanager:arn:...:ConnectionString}}`:** the `{{resolve:...}}` form is
expanded by the *ECS agent / task execution role*, which means it only works for containers ECS pulls up — and the
operations runbook (§10.2) needs the same string inside a plain `docker run` on a laptop or a CI runner, where no resolver
exists. One secret, two consumers, identical value. It also keeps `BootGuard`'s startup-time failure mode honest: a bad
secret surfaces as a connection failure at boot, which D-M5-7 can roll back, instead of as a task that never starts.

**Why no rotation.** Rotating the master password of a single-instance RDS while tasks hold pooled connections — one of
which is an SSE stream that may sit open for hours (§4) — needs either dual credentials or a drain, and this app has no
data that justifies either. The documented manual path is `aws rds modify-db-instance --master-user-password`, update
the secret, redeploy.

**Rejected.** (a) RDS-managed IAM authentication (`Token=` in the connection string): eliminates the stored password,
but it needs an outbound network config change and every client of the database (including `dotnet ef`) to mint a token;
a large change for a personal app. (b) Storing one secret per field (`Host`, `Username`, `Password`, `Database`) and
composing them at startup: more moving parts, no security gain inside the same account, and it makes the migration
runbook string harder to produce. (c) SSM Parameter Store instead of Secrets Manager: works, and the choice is almost
free here — Secrets Manager was picked because the `aws/secretsmanager` integration is the documented .NET path and
because the secret ARN is the only credential the task role ever needs.

### D-M5-7 — Rolling update with the circuit breaker, gated on the ALB target group

```
desiredCount: 1
minimumHealthyPercent: 0      maximumHealthyPercent: 100
deploymentCircuitBreaker: { enable: true, rollback: true }
healthCheckGracePeriodSeconds: 60
target group health check: HTTP GET /api/health on 8080, healthy 2 / unhealthy 2, interval 15s
```

**Why this shape.** With one task, the default `minimumHealthyPercent: 100` makes ECS start the *new* task before
stopping the old one — two tasks, therefore two concurrent `Database.Migrate()` runners, which is exactly what
D-M5-4 defers. `minimumHealthyPercent: 0` / `maximumHealthyPercent: 100` makes the update stop-then-start: one runner
at a time, at the cost of a short window where no task serves traffic. For a single-user app that window is free; for
anything with concurrent users it is the first thing to revisit.

**Why the grace period.** The new task must open the port *and* run migrations before the check can pass, and
`/api/health` answers `503` whenever the database is unreachable (`database:"error"` in its response body). A probe
that fires during the first seconds of a cold boot is a false negative the breaker would count as a failure. 60s is a
generous budget for four migrations; `[verify-at-apply]` the real boot time from the first deploy's task-stopped
reasons and CloudWatch rather than trusting this number.

**What the breaker actually rolls back to** — quoting the AWS docs, because the wording matters: *"When the deployment
circuit breaker determines that a deployment failed, it looks for the most recent deployment that is in a COMPLETED
state. This is the deployment that it uses as the roll-back deployment."* Not "the previous task definition revision" —
the most recent *successfully completed* deployment. And if none exists: *"the circuit breaker does not launch new tasks
and the deployment is stalled"*, leaving the old tasks serving and waiting for a human. Both outcomes are acceptable
here; the second is why a deploy is something you watch, not something you fire.

**One measured consequence of removing the in-image health check.** The breaker's stage 2 validates *"Elastic Load
Balancing, AWS Cloud Map service health checks, and container health checks"*. This image has no container health check
(the runtime stage installs no HTTP client — see the Dockerfile note), so the stage-2 signal can only come from the ALB.
That is the reason the target-group health check is not decoration: it is the only thing standing between a task that
boots, opens 8080, and then cannot reach RDS, and a service that looks healthy while returning 503s.

**Threshold floor.** The default threshold type is bounded below at 3 failures, so a one-task service needs three
failures before it trips. A `COUNT`-type threshold takes the value directly and is the better fit here
(`[verify-at-apply]` that the region's ECS API exposes `failureThreshold` under `serviceDeploymentCircuitBreaker` — it
is newer than the enable/rollback pair). Create the EventBridge rule on `SERVICE_DEPLOYMENT_FAILED` that the same docs
recommend; without it, a stalled deployment is silent.

**Rejected.** (a) Blue/green with CodeDeploy: instant rollback and proper migration sequencing, at the cost of a second
target group, a second listener rule set, CodeDeploy itself, and an obligation to solve concurrent migrations for real.
Revisit with a second user. (b) `REPLACE` deployment type: no breaker at all, so one bad task definition leaves the
service with zero tasks and no automatic way back.

### D-M5-8 — CI builds and pushes; a human deploys

The workflow does everything up to and including pushing the image and uploading `dist/`; **`aws ecs update-service`
and the CloudFront invalidation stay manual in v1**, and the workflow ends by printing the exact commands to run.

**Why that seam, and not one line further.** Deploy automation without rollback automation is how you get a broken
production service with no way back, and rollback here is not yet a button: because boot-time migrations are the chosen
path (D-M5-4), *a deploy and a schema change are the same event*. Rolling the service back to the previous task
definition does not un-apply a migration, so the old image may now be talking to a schema it does not recognise. Until
there is a way to tell "reversible" from "not" automatically, a human makes that call. This also matches the repo's own
rule that deploying, tagging, and releasing are human-only decisions (`AGENTS.md`, Branch & PR workflow).

**What CI must fail on.** `npm run verify` already gates typecheck → lint → lint:css → test:run → build. Two additions,
each earned by a measured failure mode:

1. **The production frontend build must fail if `VITE_API_BASE_URL` is unset or empty.** §1.3 traced what happens
   today: the SPA falls back to the localStorage/in-memory mock store and the site looks healthy while never issuing a
   single API call. A green build producing a fake app is the worst possible CI outcome, and one assertion in the build
   step prevents it. The same step should assert the value is exactly `/`, because any absolute URL re-opens the
   cross-origin problem D-M5-1 exists to close.
2. **`docker build` must pin `--platform`.** A buildx run on an arm64 laptop producing an arm64 image pushed to a
   repository whose task definition asks for `X86_64` fails at *deploy* time, not build time. Pin the architecture in
   the workflow (and note that `linux/arm64` on Fargate is both supported and cheaper — picking it is a cost decision,
   changing it later is a rebuild of everything).

**Rejected.** (a) Push-to-`main`-deploys: removes the human gate the workflow is built on and the one place a
schema-moving deploy can pause for a backup. (b) Manual builds too: the image a laptop produces is not the image CI
produces, and D-M5-5's digest argument only means something if the digest came from a reproducible builder.

### D-M5-9 — Infrastructure is code, in this repo, before the console

The IaC tool is **open (§12.1-C)**. That the stack must be code, and that its state must live outside this laptop, is not.

**Why.** Three of the facts that decide this topology are the kind typed into a console once and never read again:
which security group the RDS ingress rule allows, which path the ALB target group health check uses, and whether the
certificate the 443 listener references was issued in us-east-1. A console-built stack has no diff, and the rollback
plan in §8 has nothing to apply. Worse, it invites exactly the failure this rewrite corrected: `docs/aws-deployment.md`
described an `infra/` directory, a deploy workflow, and created ACM/ECR resources that **do not exist in the
repository** (§1.1). Documentation written from intent rather than measurement is the reason §1 of this spec measures
everything it claims.

**Rejected.** (a) Terraform or CDK because they scale to teams: this is one service in one region; the deciding factor
is which plan the owner can read and defend in an interview, so §12.1-C asks instead of assuming. (b) SAM: CloudFormation
plus serverless opinions, and there is no serverless piece here. (c) "Console first, code later": later never comes, and
the first thing that needs rolling back is the thing nobody wrote down.

---

## 3. Topology

One public hostname, one browser-visible certificate, and a path split that keeps both `__Host-` cookies valid without
touching the client.

```
                        ┌──────────────────────────────┐
   browser ── HTTPS ───▶│  CloudFront (single distro)  │   cert: ACM in us-east-1 (D-M5-1)
                        └───────────┬──────────────────┘
                                    │
                     /api/*  ───────┼───────▶ origin 2: ALB (HTTPS:443, own ACM cert)
                     /*      ───────┴───────▶ origin 1: S3 bucket (private, OAC, static SPA)
                                    │
                        ┌───────────▼──────────────────┐
                        │ ALB, internet-facing, 2 AZs  │
                        │  rule prio 1: /api/* → tg-api│
                        │  default: tg-notfound (503)  │
                        └───────────┬──────────────────┘
                                    │ HTTP:8080, SG ingress = ALB SG only
                        ┌───────────▼──────────────────┐
                        │ ECS Fargate service, 1 task  │  container :8080, runs as uid 1654 (§1.4)
                        └───────────┬──────────────────┘
                                    │ TLS:5432, SG app→db only
                        ┌───────────▼──────────────────┐
                        │ RDS PostgreSQL (single-AZ,   │  secret: Secrets Manager
                        │ snapshots on, no public IP)  │  → `valueFrom` in the task def (§3.7)
                        └──────────────────────────────┘
```

**Nothing in this diagram is optional, and the two edges that look optional are the ones people cut.** ~~HTTP:80 on the
ALB exists only to fail loudly (no listener ⇒ a cleartext path is refused, not served), and the ALB's own 443
certificate is what makes CloudFront→origin not-cleartext for the session cookie.~~ **Amended by §14 (2026-09-14): under H-A those two
edges swap jobs.** :80 is the *serving* origin path — because the ALB has no certificate it can present for its own name — and the
diagram's "not-cleartext" claim is withdrawn rather than defended. What replaces it is the reachability argument in the D-M5-3 block
above: the :80 listener is reachable **only** from CloudFront's origin-facing address ranges, and only requests carrying the shared
origin header are forwarded at all. The `443` listener returns when a hostname does. D-M5-1 chose the single public origin
because both cookies are `__Host-` prefixed with no `Domain` and `EventSource` is opened without `withCredentials`
(§1.2): an `api.<domain>` split is **not deployable as written**, and the fix for it is more client changes than the
split is worth.

| Resource | Requirement | Why this and not the simpler thing |
| :--- | :--- | :--- |
| Route 53 | ~~Alias `app.<domain>` → CloudFront~~ **Under H-A (§14): no record at all — the public name is CloudFront's default distribution domain, and `route53 list-hosted-zones` measured `[]`, so there is no zone to write into.** The second sentence stands unchanged, and matters more now: **no record points at the ALB.** | A record at the ALB is a route to a `Secure`-cookie session that skips everything in front of it. The `[verify-at-apply]` was resolved by measurement on 2026-09-14: the zone is not in this account. |
| CloudFront | One distribution; S3 + ALB origins; viewer protocol policy **redirect-to-https**; **origin protocol policy for the ALB origin is `http-only` under H-A (§14)** — `https-only` returns `502` with no matching certificate ([CIT-A1-4]). | Two distributions double the certificate and header surface and buy nothing this app asked for. |
| S3 | `dist/` only; Block Public Access all-four on; access via OAC. | The deep-link fix (§3.5) is a function, not a bucket policy; the bucket stays dumb deliberately. |
| ACM | ~~two certificates~~ **Under H-A (§14): zero.** CloudFront's default domain is served by CloudFront's own AWS-managed certificate ([CIT-A1-2]), and no certificate can be requested for the ALB's Amazon-owned name ([CIT-A1-3]). | The us-east-1 asymmetry that made this a classic first-deploy stall **stops being a constraint under H-A** — which is the one respect in which the route is simpler than the plan, and §14.6 flags an unresolved AWS-internal contradiction about the *origin-side* half of that rule. |
| VPC | ~~Public subnets (ALB) + private with NAT (tasks) + private (RDS), 2 AZs.~~ **Answered 2026-09-14 (§12 D), and this row had not been re-read against that answer: bought `/20`, 2 AZs, public (ALB) + private-with-**endpoint**-egress (tasks) + private (RDS) — no NAT gateway.** | §12 no longer leaves "default VPC vs bought CIDR" to the owner — it is answered, and the ~$32/mo NAT was removed deliberately, not opportunistically: **this app makes zero outbound HTTP calls**, so the only egress consumers are the ECS agent pulling from ECR, Secrets Manager, and CloudWatch Logs, each of which has a PrivateLink endpoint at roughly a hundredth of the NAT's monthly cost. What survived the change is the sentence that mattered anyway: **the ALB's SG is the only ingress to the tasks.** T-03's design spec enumerates the endpoint set; its `[verify-at-apply]` rows are the ones §12 D left open. |

### 3.2 ECS (Fargate) service

- **Launch type `FARGATE`**, platform version `LATEST`, `awsvpc` network mode. Worth stating plainly because §1.4
  removed the in-image `HEALTHCHECK`: **ECS does not act on a Dockerfile `HEALTHCHECK` at all**, so nothing the fleet
  relied on was deleted.
- **One container per task**, `portMappings: [{ containerPort: 8080, hostPort: 8080 }]`. In `awsvpc` the task *owns* the
  ENI, so `hostPort` is a claim rather than a mapping; 8080 is the number the image sets
  (`ENV ASPNETCORE_HTTP_PORTS=8080`) and the number measured listening in §1.4.
- `cpu: 256` / `memory: 512` as the opening size. **[verify-at-apply]** against a measurement, and the measurement worth
  taking is **memory per open SSE stream**, not memory at boot: the loop in `ApplicationCatalog.cs:71-113` holds a task,
  a channel and a reader for as long as a client stays connected (§4), so `desiredCount × connections-per-task` is the
  number that can actually OOM a task.
- `desiredCount: 1`, with `minimumHealthyPercent: 0` / `maximumHealthyPercent: 100` — **this is D-M5-7's shape
  restated for infrastructure, and §3.2 defers to D-M5-7 wherever the two could disagree.** One task at a time means
  **exactly one process can ever reach `Migrate()`**, which is the whole reason D-M5-4 (migrations stay at boot) was
  accepted over a separate migrator step rather than alongside it. The cost is a deploy window in which nothing
  serves: the gap is one cold start, and its floor is not a request latency but the boot sequence in §1.5 — port
  opens only after `Migrate()` returns, and a failed migration kills the process before it opens. D-M5-7 budgets
  60 s of `healthCheckGracePeriodSeconds` for it; **[verify-at-apply]** the real figure from the first deploy's
  task-stopped reasons rather than trusting that number.
- **Going to `desiredCount: 2` is the revisit, and it is D-M5-4's expiry trigger** — see §8.2 and §7 step 9.
- **Task role and execution role are distinct and neither is a wildcard.** Task role: `secretsmanager:GetSecretValue`
  on the single ARN in §3.7 (needed for `valueFrom`). Execution role: the managed
  `AmazonECSTaskExecutionRolePolicy` plus `logs:CreateLogStream`/`logs:PutLogEvents`. **No RDS permission on either
  role** — this app authenticates to Postgres with a password inside the connection string, not with `aws rds iam`, so
  an `AmazonRDSFullAccess` grant would be a privilege nobody can remove later without an outage someone remembers.
- **`healthCheckGracePeriodSeconds` must exceed cold boot + `Migrate()` + the first target-group probe**, and §4.3 does
  that arithmetic instead of rounding a guess. The process cannot serve `/api/health` before `Migrate()` returns (§1.5:
  migration is inline at boot and failure is terminal), so a grace period shorter than that makes ECS stop a task that
  was about to become healthy — the exact false positive that teaches an operator to switch the circuit breaker off.
- Log config: one group, `/ecs/job-tracker-api`, stdout only (§1.4: the app logs to stdout and writes nothing to disk).
  **Set `retentionInDays` explicitly**; the log group is where a rolled-back deploy's evidence lives, and an unbounded
  group is the resource that quietly accrues cost.

### 3.3 ALB, listeners, and the forwarded-header trust boundary

- Internet-facing, in ≥2 AZ subnets. ~~**HTTPS:443** listener with the workload-region ACM cert (D-M5-3). The HTTP:80
  listener's only job is `redirect-to-https`; if it is absent, a mistaken HTTP path fails loudly, which is better than
  serving it.~~ **Amended by §14: under H-A the ALB serves on HTTP:80 and has no 443 listener**, because no certificate can be
  requested for its Amazon-owned name. The loudness guarantee that sentence carried is replaced by two others: the :80 listener's
  SG ingress is the CloudFront origin-facing prefix list **only**, and a request that does not carry the shared origin header is
  answered with a fixed `403` by the listener's default rule ([CIT-A1-5]). The 443 listener returns with H-B or H-C.
- Listener rules by priority: **1)** `/api/*` → `tg-api`; **2)** default → `tg-notfound`, a target group with **no
  registered targets**, which answers 503. The API has exactly nine routes (§1.2) and the SPA is not one of them, so
  "send everything else to the API" would be routing traffic to a service that has no handler for it.
- **`ForwardedHeaders:KnownNetworks` is the trust boundary and it already exists** (`Program.cs:129-143`). Set it to the
  VPC CIDR. Left empty, `X-Forwarded-For`/`-Proto` are ignored and `HttpContext.Connection.RemoteIpAddress` is the ALB
  node — which makes "we are behind HTTPS" an assumption instead of a fact, and `Secure` cookies are only as strong as
  that fact (§1.2: the flags are unconditional, so a cleartext path is also a broken login).
- **ALB→target is HTTP:8080 and nothing else may reach the tasks.** SG ingress on the task SG allows `8080` from the ALB
  SG *only*. This is the sentence that makes "last hop is cleartext" defensible: it is not that cleartext is fine, it is
  that there is no path to it that isn't the ALB. **§14 extends the same argument one hop up and records the cost of doing so:**
  under H-A the sentence becomes *"there is no path to the ALB's :80 that isn't CloudFront, and CloudFront's requests are the ones
  carrying the shared header"* — a reachability-plus-authentication claim where D-M5-3 used to make an encryption claim. Prefix-list
  membership is L3/L4 filtering and can be widened by one console edit; the header is the part that actually authenticates, and it is
  the reason the SG rule is listed as optional in AWS's own procedure ([CIT-A1-5]).

### 3.4 Target group, health checks, and what "healthy" means to a deployment

- Target type **`ip`** (there are no container instances to register).
- **`healthCheckPath: /api/health`**, **`matcher.httpCode: "200"`**. Not `/`, not `/health`, not `/live`:
  - `/` returns a **23-byte text marker** unconditionally — *including while the database is down* (§1.2). **A target
    group pointed at `/` marks a broken service healthy**, and every rollback signal in D-M5-7 then reads "fine" while
    users get 500s. This is the one row in §3 that can silently invert the meaning of the entire deployment.
  - `/live`, `/ready`, `/healthz` **do not exist**; there is no `MapHealthChecks` in `api/src` (§1.2). The ASP.NET health-check middleware is not an option that exists today, which matters because
    `docs/releases/m5-aws-deployment.md:51` expects a `200` that comes from a hand-written handler, and the same
    document asserts a `{"dbPing": "<latency>"}` field that handler never emits (`Program.cs:207` returns `status`
    and `database` only — §1.2, reconciled in §11).
  - Measured answers: `200 {"status":"ok","database":"reachable"}` with no cookie; `503`
    `application/problem+json` (172 bytes) when the DB is unreachable. **A body-shape assertion on the health check is
    therefore not available** — the 503 body is generated by `UseStatusCodePages`/`AddProblemDetails`, and its
    `title`/`type` are framework text. Match on the status code.
- `healthyThresholdCount: 2` / `unhealthyThresholdCount: 2`, matching D-M5-7; interval/timeout in §4.3.
- **Deregistration delay needs an explicit choice, not a default.** `EventSource` streams are long-lived by design; ALB
  deregistration drains *connections*, and at the deadline it closes them. A short delay tears every open stream on
  every deploy (which the client survives — §4 — but which makes each deploy visible to users); a long delay slows
  rollback by the same amount. §4.3 states the number and §8 the consequence.
- **The circuit breaker's stage-2 signal is this target group and nothing else** (D-M5-7). With no in-image
  `HEALTHCHECK` (§1.4) and ECS already known to ignore it, the target group is the only component in the stack that can
  say "this new task is not serving" — which is why the check path above is a correctness requirement rather than
  hygiene.

### 3.5 S3, CloudFront behaviours, and the two things that break quietly

- **`/api/*` behaviour**: origin = ALB, `DefaultTTL/MinTTL/MaxTTL = 0`, cache disabled (`DisableCache` / no
  `ForwardedValues` caching), viewer protocol policy HTTPS-only, all methods GET/HEAD/OPTIONS/PUT/POST/DELETE
  (`CachedMethods` stays GET/HEAD). `AllowedMethods` matters: a `PUT /api/applications/{id}` that CloudFront refuses is
  a 403 that looks like an application bug.
- **Query-string forwarding** must be `All` (or field-level allow-list) for `/api/*`. With the default `None`, a future
  `?filter=` or cache-buster is silently dropped **at the edge, with no log line anywhere** — this class of bug is the
  same one as the `VITE_API_BASE_URL` trap in §1.3: correct-looking behaviour that no test observes. **[verify-at-apply]**
  against a request that carries a query string, and confirm it arrives, before this is called done.
- **SPA deep links need a custom error response**: `403/404 → /index.html` with `200` (CloudFront Function at
  Viewer-Request, or origin-request rewrite). Required because **Production registers no fallback at all** (§1.2) — the
  dev-only `MapGet("/{*path:nonfile}")` is exactly the thing S3+CloudFront must replace. Handle **403 as well as 404**:
  S3 answers a missing key in a private bucket with **403**, so a 404-only rule leaves deep links broken in the shape
  that looks like a permissions problem.
- **Invalidation is part of the deploy.** `dist/` filenames are content-hashed by Vite, so only `/index.html` needs an
  invalidation. `/*` on every deploy is the lazy version; it works, and it turns a personal project's bill into a
  lesson about TTLs.
- **The build-time trap is release-blocking** (§1.3): the production bundle must be built with `VITE_API_BASE_URL=/`,
  and **CI must fail if that variable is unset, empty, or anything other than `/`** (§9). Without the assertion, the
  artifact that ships is a working-looking SPA that writes to `localStorage` on the real domain: it passes every
  probe in §7 that only checks "the page loaded", shows the user their own saved data, and has never once called the
  API. That is the most dangerous property of this deployment, and a build-time `test -n "$VITE_API_BASE_URL"` plus an
  equality check is the whole fix.

### 3.6 RDS PostgreSQL

- **Parity, not aspiration**: `api/docker-compose.yml` pins `postgres:18.6` (§1.5), so 18.6 is the choice that matches
  the dev container. **[verify-at-apply]** that the chosen region offers 18.6 for the chosen instance class **and**
  where it sits on the RDS deprecation schedule — a version that is merely "available today" can be scheduled out
  before this app is stable, and an EOL-major forces an upgrade on someone's worst weekend.
- **Not publicly accessible**, in the private subnets, `storage_encrypted = true`, `backup_retention_period ≥ 7`,
  `deletion_protection = true` set **after** the first successful deploy (setting it before means the destroy path in
  §10's cleanup is a support ticket).
- **`citext` is a first-deploy blocker, not a steady-state one** (§1.5): `InitialCreate` calls
  `CREATE EXTENSION citext`, which needs the extension available *and* a role permitted to create it. The usual answer
  is to run the boot-time `Migrate()` as the master/user that owns the database rather than as the app login. This is
  **the most likely single reason the image that works in Docker fails on RDS**, and it will fail at boot — which is at
  least the loud half of D-M5-4.
- `pgcrypto` is created **conditionally** (`20260910054851_add-auth-tables`, guarded and idempotent, §1.5), so it needs
  no parameter-group assumption.
- Connection draining on failover/restart is *not* the app's problem, but **the pool is**: nothing in `api/src` sets a
  maximum pool size or a command timeout (§1.2 measured no `CommandTimeout`), and each open SSE stream holds a
  connection-checkout path for its whole life. **[verify-at-apply]** the count of `pg_stat_activity` rows against
  `desiredCount × open streams` before raising `max_connections`.

### 3.7 Secrets Manager, and the one place this spec changes code

- **One secret, one value: the whole Npgsql connection string** (D-M5-6). Not host/port/db/user as four fields — the
  app reads `Configuration.GetConnectionString("Default")` (`Program.cs:65`), and `BootGuard` refuses to start without
  it (§1.1), so any decomposition would need a resolver that *builds* the string, which is new code with new failure
  modes standing between "the secret is correct" and "the app boots".
- Injected via **`secrets`/`valueFrom` in the task definition**, so the value never appears in a `docker run` command
  line, a CI log, or `appsettings.Production.json`. **The `appsettings.Production.json` file is not created by this
  milestone** — §1.1 measured that no `.env*` or production settings file is tracked, and a file that holds a
  connection string is a file that ends up in a commit.
- **Rotation: none automated in v1** (D-M5-6), stated as a decision with its cost: rotating the DB password by hand
  while tasks are running means the old secret still lives in the running task's environment until it is replaced. The
  mitigation is in §10's runbook (update secret → force new deployment), not in a rotation lambda that this app has no
  way to test locally.
- **A rotation-or-typo event is observable in exactly one place**: `/api/health` → `503` (§1.2), because the health
  handler calls `CanConnectAsync`. That is the signal the target group turns into an ECS-level action; §4.3's timeouts and §7's step 4 are the only
  thing that makes the latency of that path tolerable.

### 3.8 ECR, images, and provenance

- One private repository, `imageTagMutability: MUTABLE` **only because** D-M5-5 tags with an immutable-by-convention
  git SHA; `IMMUTABLE` would be safer and is **[verify-at-apply]** as a first-deploy choice, with the caveat that it
  makes `:latest` unusable — so if `IMMUTABLE` is set, no template or command may reference `:latest` anywhere.
- **Lifecycle policy** to keep the last N (say 20) SHA tags plus anything referenced by a running task. ECR storage is
  cheap; the *reason* for the policy is that a repository with 400 untagged manifests makes "which image is in prod"
  a browsing exercise during an incident.
- **Deploy by digest, ever** (D-M5-5): the task definition references
  `repo@sha256:…`, and §9's CI step records the digest in the task record so "what is running" is answerable from the
  repository rather than from the console.
- Scan-on-push is **enabled but not gating** in v1, and that must be written down rather than discovered: an unscanned
  image is not a decision to skip CVEs, it is a decision to read them after the fact. **[verify-at-apply]** whether the
  owner wants a gate; if yes, §9's build job needs a second step and a documented "what do I do when a Critical CVE
  blocks a deploy" answer, which is currently nobody's plan.

---

## 4. Timeouts, and the SSE keep-alive that §4.3's numbers depend on

### 4.1 What the stream actually does (measured, not read)

`ApplicationCatalog.cs:71-113` is the whole endpoint: it sets `text/event-stream` + `no-cache`, writes one comment frame
(`: open\n\n`, line 92), and then blocks in `await foreach (var id in changes.Reader.ReadAllAsync(ct))` (line 97) on its
own `Channel` from `ApplicationEventBus`. After the opening frame it emits **nothing until a row changes**. Three probes
against the built image, in order of how much they changed this spec:

| Probe | Result |
| :--- | :--- |
| `curl -N /api/applications/events` with a session cookie, no writes | `: open` at t≈0; **zero bytes for 22 s**, connection still open when closed. |
| Same, with a `POST /api/applications` from a second client at t≈+4 s | `event: change` + `data: {"id":…}` arrived **in the same second**. Delivery works; the silence was the idle state, not a stall. |
| Same, row written from a second client at **t≈167 s** | Frame arrived **immediately**. **The application never closes an idle stream.** |

The third row is the one that turns this from tuning into code. A stream that stays byte-silent for 167 s and still
delivers means no Kestrel limit, no handler timeout, no channel deadline and no EF poll-loop ceiling is in play — the
**only** components that can end it are the two proxies in front of it. So the open question in the first draft of this
spec, "does the app time out streams too, at some number we should align to?", has the answer *no*, and what remains is
what the proxies do with a connection carrying no bytes.

A row delete belonging to the **other** owner produced no frame on this stream (the handler filters per owner). Expected,
and worth recording because during a keep-alive test "no bytes" and "no events for me" are indistinguishable from the
client side, and mistaking one for the other is how a working heartbeat gets declared broken.

### 4.2 Requirement R-4.2 — the API must write a comment frame on a timer

> **R-4.2.** While an SSE request is open, the handler must write `": keep-alive\n\n"` to the response body every
> **20 s**, on a loop driven by the request's own cancellation token (`ct` is already threaded through 71-113), and stop
> when the client is gone. Comment frames only — no new event names, no protocol change, no client change.

Why a **comment** frame: the SSE spec makes a line beginning with `:` a comment that clients discard, so the
`EventSource` at `httpApplicationRepository.ts:223` needs no change and the wire format is untouched. This is also why
R-4.2 is *not* the change §1.3 proved impossible: D-M5-2 had to be re-scoped because fixing the trailing-slash bug
**forced** a client change, and R-4.2 deliberately chose the one mechanism that doesn't.

Why the loop must ride on `ct` rather than a `Timer`: `: open` is written inside the request, so the handler already runs
for the request's lifetime. A `Timer` holding a captured `HttpContext` can fire after the response is completed, which
is an `ObjectDisposedException` on the exact path where a browser tab was closed — a bug that reproduces in production
and not in a test. `ChannelReader.WaitToReadAsync(ct)` raced against `Task.Delay(20 s, ct)` with `Task.WhenAny` keeps a
single cancellation path and adds no type.

**Why 20 s, and why that number is not a taste call:** the heartbeat is the thing that keeps a byte-silent stream alive
through a proxy, so it must be **below the shortest idle ceiling in the path** with margin for one lost frame. The
ceilings in order of which will strike first:

```
heartbeat 20 s  <  CloudFront origin read/inactivity timeout [verify-at-apply]  ≤  ALB idle_timeout (this spec sets 120 s)
```

- Against the **ALB** it is 6× margin on a 120 s idle timeout (§4.3), so a single delayed frame cannot kill a stream.
- Against **CloudFront** it is chosen to stay under the commonly documented ~30 s origin-response timeout **with a
  margin, not at its edge** — that value is `[verify-at-apply]` (§4.3) rather than designed on, because the direction
  of the constraint is what the spec needs and the number is what the deploy must read.
- Against the **client** 20 s is short enough that a laptop whose tab was suspended mid-stream is detected in one
  interval instead of one ceiling.
- Cost: 13 bytes every 20 s ≈ **56 KB per stream per day**. At this app's real concurrency (single user, occasionally
  two) the answer to "is this cheap enough" is yes by three orders of magnitude; the number is written down only so the
  next reader doesn't have to re-derive it.

**Validation for R-4.2 (T-06).** Open the stream through the local proxy route and assert (a) frames arrive at the
configured interval, (b) an unrelated owner's write produces no frame, (c) a change by the right owner produces a real
`event: change` **interleaved with** the comments, and (d) after the client disconnects the handler stops writing.
(b) and (d) are the two that catch an implementation that looks right in a happy-path test.

### 4.3 The timeout matrix

Two kinds of row: a number **this spec sets**, and a number the deploy must **read from AWS at apply time**. The second
kind is marked `[verify-at-apply]` with the command that reads it, because a spec that guesses an AWS default is worse
than one that admits it doesn't know — a wrong guess here surfaces as an SSE stream that dies at a *reproducible*
interval, which looks exactly like an application bug and sends the debugging to `Program.cs`.

| Surface | Knob | Value | Constraint it must satisfy | Status |
| :--- | :--- | :--- | :--- | :--- |
| Browser → CloudFront | — | — | One break costs a reconnect + full list refetch, not data loss (`EventSource` retries by default). | Behaviour, not config |
| CloudFront `/api/*` | `DefaultTTL`/`MinTTL`/`MaxTTL` | `0`, cache disabled | `/api/*` must never be cached, and a response carrying `Set-Cookie` must never be stored. | Set by §3.5 |
| CloudFront origin | origin **read/response** timeout | `[verify-at-apply]` — `aws cloudfront get-distribution-config --id …` → `Origins.Items[].OriginReadTimeout` (documented 5–60 s, default 30 s) | `/api/health`'s **first byte** must arrive inside it; SSE's `: open` at t≈0 satisfies it, and R-4.2's 20 s keeps the open stream inside the inactivity reading of it. | §4.4 |
| CloudFront origin | origin **keep-alive / inactivity** timeout | `[verify-at-apply]`, same call | The ceiling R-4.2 exists to beat. **Confirm which timeout CloudFront applies to an already-open streaming response** — they are different numbers, and only one of them kills a silent stream. | §4.4 |
| CloudFront | `ResponseCompletionTimeout` | `[verify-at-apply]`, same call | Governs the **whole** response, so a stream that never "completes" must be checked against it, not assumed exempt. The likeliest home for "SSE is fine for 15 minutes, then every client reconnects in unison". | §4.4 |
| ALB → client | `idle_timeout.timeout_seconds` | **120 s** | > 6 × heartbeat, so one late frame cannot drop a stream. The 60 s default works with a 20 s heartbeat but leaves no margin for a GC pause or a slow client. | Set here |
| ALB | `request_timeout.timeout_seconds` | leave default | Bounds a *request*, not an open response; `/api/health` answers in 0.09 s measured (§1.2), so this row is not load-bearing. | Not load-bearing |
| ALB → target | `deregistration_delay.timeout_seconds` | **60 s** | Long-lived streams make a draining target look immortal; the deploy and the rollback in §8 both have to finish. A torn stream is a reconnect plus a full refresh, which is what the client already does. | Set here |
| ALB target group | `health_check.interval` / `.timeout` | **15 s / 10 s** | D-M5-7's interval; `timeout` must exceed the worst-case `/api/health`, which §4.4 showed is unbounded without R-4.4, so 10 s is a budget until the bound exists; `interval > timeout`. | Set here, **cost-flagged below** |
| ALB target group | `healthy_threshold_count` / `unhealthy_threshold_count` | **2 / 2** | Unhealthy ⇒ ~30 s to stop routing; healthy ⇒ ~30 s before a task counts as up, which is the number `healthCheckGracePeriodSeconds` (60 s, D-M5-7) must clear (§3.2). | Set here |
| ALB target group | `slow_start.enabled` / duration | **off** | With `desiredCount: 1` (§3.2) and D-M5-7's 60 s grace + 2-success floor, the task is proven before it receives traffic; slow start adds an under-provisioned delay for no gain. | Set here |
| VPC | NAT gateway idle timeout | 300 s, AWS-fixed | **Not** on the SSE path — that traffic is inbound through the ALB. Listed so nobody "fixes" the wrong hop. | Fixed |
| App | Kestrel `KeepAliveTimeout` | **not set** | Measured irrelevant: the app never closes an idle stream (§4.1, 167 s of silence). Recorded so a later reader doesn't tune this expecting it to affect SSE. | Decided, not guessed |
| App | Npgsql `Timeout=` / `CommandTimeout` | **not set today** | The one path where a *dropped* packet (wrong SG, not wrong password) makes the health endpoint slower than the proxy waiting on it. §4.4. | Gap, not decision |
| Container | `stopGracePeriod` | **30 s**, ≤ deregistration delay | SIGTERM → `IHostApplicationLifetime`; must not outlast the ALB's patience or ECS's willingness to wait. | `[verify-at-apply]` against measured cold start |
| ECS | `healthCheckGracePeriodSeconds` | **≥ 180 s** | cold boot + `Migrate()` (first run creates 4 tables + 2 extensions) + healthy threshold × interval (90 s). Derive it from a measured cold start, not from this cell. | §8.2 |

**The health-check row is a cost decision as well as a latency one.** ALB target health checks are billed per check
beyond a free allowance, so the interval is the one knob in this table where a 5× change is also a 5× bill change on a
personal project. **`[verify-at-apply]`: read the price of the chosen interval before choosing 6 s over 30 s** — an
aggressive interval buys roughly 24 s of detection speed and can cost more than the two Fargate tasks it is watching.
If faster detection is wanted later, buy it from the circuit-breaker thresholds rather than from the probe interval.

### 4.4 Requirement R-4.4 — bound the health check's worst case

§1.2 measured that no command timeout is configured anywhere in `api/src`. So `GET /api/health` against a database whose
packets are **dropped** — a security-group mistake, a route-table change, an RDS instance that stopped accepting writes —
can hold the request open for Npgsql's connect timeout instead of answering in the 0.09 s measured for a *refused*
connection. That is the one path where the endpoint the whole rollback ladder trusts is slower than the proxy waiting on
it, and it is slow in a way that produces **no log line**: a health check that expires at the target group is recorded as
an unhealthy target, not as a hung request.

Two acceptable fixes, one task (T-07):

1. **Set `Timeout=5` in the connection string** — one field of the Secrets Manager value (§3.7), zero code. But
   **[verify-at-apply]** whether Npgsql's `Timeout` keyword bounds connect only or connect *and* command, because the
   difference is precisely the failure above, and the documentation has read both ways in different versions.
2. **Or give the health handler its own `CancellationTokenSource(TimeSpan.FromSeconds(5))`** and treat "still thinking at
   5 s" as unhealthy — more honest, because it measures the thing the caller actually cares about, and it is a code
   change with a test rather than a string with a folklore problem.

This spec prefers **(2)**, and says why: the ALB's 10 s timeout is *a proxy's* patience, and the application should not
outlive it by accident. Until T-07 lands, **`[verify-at-apply]` includes exactly one measurement — how long does
`/api/health` take when the database is unreachable-by-route rather than refused?** Five minutes of work, and it decides
whether D-M5-7's breaker reports a stalled deploy in ~60 s or after several expired probes, which is the difference
between a rollback and an outage that looks like one.

---

## 5. Why the numbers in §4 are `[verify-at-apply]`

The first draft of this section carried four AWS timeouts as if they were known: ALB idle 60 s, ALB request 60 s,
CloudFront "idle 60 s, hard max 15 min", target-group interval 30 s / timeout 5 s / 5-2 thresholds, and a
"`keepAliveTimeout` > 60 s" Kestrel setting that does not exist in this codebase. **Three of the five are plausible
defaults that no one had read, and the fifth was invented to fill a gap** — the same failure mode as §1.3's invented
`baseURL.replace(/\/+$/, '')`, and the reason this policy exists.

So the rule, applied to every row in §4.3 marked `[verify-at-apply]`:

- **Never ship a guessed number.** Guesses get read as facts by whoever deploys this, and a guessed timeout is the
  expensive kind of wrong: it doesn't crash, it produces a reproducible-interval stream death that reads as an
  application bug and sends the debugging to the wrong file.
- **Record the read, not the assumption.** At apply time each cell gets replaced with `value — source, date` (a CLI
  response or a console screenshot in `docs/tasks/m5-deploy-log.md`), and the command to read it is already in the row.
- **State the constraint even when the value is unknown.** The *relationship* is what this spec is for, and it survives
  a changed default: `heartbeat < CloudFront origin timeout < ALB idle < deregistration + grace < ECS grace arithmetic`.
  If AWS moves a default, the constraint still tells you which cell to re-read.
- **One measurement closes most of the uncertainty:** a 20 s heartbeat clears any timeout in the documented
  CloudFront range with margin, so R-4.2 does not need the numbers first. Deploy the keep-alive, *then* read the
  timeouts, rather than the reverse.

---

## 6. Task breakdown (T-01 … T-17)

Ceremony follows AGENTS.md: **L2** = needs `docs/tasks/<id>.md` before code (persistent data, public contract,
multi-component); **L1** = inline planning suffices. "Prove it by" is the artifact that closes the task — if it cannot be
produced, the task is not done, however healthy the console looked.

| # | Task | Depends | Cer. | Prove it by | Why it cannot be skipped |
| :--- | :--- | :--- | :--- | :--- | :--- |
| T-01 | Resolve the 7 owner decisions in §12 and write the answers into §3's `[verify-at-apply]` cells | — | L1 | §12 has zero `Open` rows | Region decides the `us-east-1` ACM certificate (§3.1); the IaC choice decides the *form* of every task below. Everything inherits these. |
| T-02 | ~~Register/confirm the zone, request **both** ACM certificates, complete DNS validation~~ **Restated 2026-09-14 (§14): settle the hostname route on paper, and measure the prices the dead session blocked.** Under **H-A** there is no zone to confirm, no certificate to request, and nothing to validate — the task's remaining substance is §14's `[verify-at-apply]` cells plus the free-tier numbers only the owner's console can read. | T-01 | L1 | ~~`aws acm describe-certificate` → `Status=ISSUED` in both regions~~ **(amended)** §14's answer cells carry no `Open`; **I-A1-1** is demonstrated by a single config key in the CDK stack, not by a certificate; the three console numbers are recorded with their source and date. **If H-B lands**, this row regains its DNS-validation half — and *only* that half, which is why the row was restated rather than deleted. | It was the only dependency in the milestone that wasn't ours to fix, and it stopped being one the moment measurement showed there was no zone to wait on. Deleting the row would hide that; restating it keeps the reason visible for whoever reads this after a failed deploy. |
| T-03 | Provision VPC (2 AZ: public + private + **VPC endpoints, no NAT** — §12 D), ECR repository, CloudWatch log group **with retention** | T-01 | L2 | `aws ec2 describe-subnets` shows 6 subnets across 2 AZs; a private-subnet task reaches **ECR, Secrets Manager and CloudWatch Logs through interface endpoints, and has no route to `0.0.0.0/0`**; log group has `retentionInDays` set | ~~Networking is what makes "the last hop is cleartext" acceptable (§3.3).~~ **Amended twice over: §14 made the *second-to-last* hop cleartext too** (CloudFront→ALB is HTTP:80 under H-A, because the ALB has no name it can be certified for), so the networking here defends **two** unencrypted edges — the ALB's :80 by CloudFront prefix-list ingress plus the shared origin header with a default-`403` rule, and the tasks' :8080 by SG membership on the ALB's SG alone. That is also why this task cannot be retrofitted: **a route table and a security group are the only things standing between a session cookie and the public internet on both hops**, and retrofitting them under a live service means editing them while traffic flows. Retrofitting a VPC under a live service is the worst version of this task. |
| T-04 | Create RDS PostgreSQL 18.6 (private, encrypted, backups on) and **prove `citext` + `pgcrypto`** | T-03 | L2 | Boot the image against it once and see `Applied migration '20260903075739_InitialCreate'` in stdout | §1.5: the most likely first-deploy blocker, and it fails at boot. Proving it here means D-M5-4's loudness never has to fire in anger. |
| T-05 | Create the Secrets Manager secret holding the **whole** connection string; grant task-role `GetSecretValue` on that one ARN | T-04 | L2 | A task-definition revision with `valueFrom` that reaches `RUNNING`; role policy reviewed as one secret, one action | D-M5-6. Doing it before the service exists avoids the window where a task runs on an env-var secret — the refactor that always leaves one behind. |
| T-06 | **Implement R-4.2 heartbeat** in `ApplicationCatalog.cs:71-113`, with the four assertions in §4.2 | — | L1 | A 5-minute idle stream emits ≥ 12 comment frames and **zero** `event:` lines; a right-owner write still produces exactly one `event: change`; an unrelated-owner write produces nothing; disconnect stops the writes | Without it the SSE feature is likely to break in production while remaining invisible locally — the exact asymmetry §4.1 measured. Independent of AWS: start here. |
| T-07 | **Implement R-4.4** health-endpoint bound + a test that a `CanConnectAsync` which never returns yields unhealthy within the bound | — | L1 | `/api/health` returns 503 inside the configured window against a datasource that hangs forever | §4.4: the breaker trusts this endpoint, and trust with no bound is how a stalled deploy reads as a slow one. |
| T-08 | ECS cluster, task definition (container :8080, `stopGracePeriod`, separate task/execution roles), service with circuit breaker **and** `deploymentAlarms` | T-05 | L2 | `describe-services` output showing `deploymentConfiguration.deploymentAlarms.enabled = true` with the target-group ARN listed; first deploy reaches `RUNNING` | D-M5-7's mechanism lives here. Built without alarms, the breaker is inert and §8's rollback story is fiction. |
| T-09 | ~~ALB + HTTPS:443 listener~~ **Under H-A (§14): ALB + HTTP:80 listener** + `/api/*` rule + **the shared origin-header check whose default action is a fixed `403`** + `tg-api` (check `/api/health`, matcher `200`) + `tg-notfound` + SG chain. **Restore the 443 listener when H-B or H-C lands — that is the whole of the reversal, and it is why this row was restated instead of rewritten** | T-08 | L2 | ~~`curl -kv https://<alb>/api/health`~~ **(amended)** `curl -s -o /dev/null -w '%{http_code}' http://<alb>/api/health` **with no header → 403**; with the shared header → 200; `curl` from the public internet to the ALB's :80 → **no answer** (prefix-list ingress only); a non-`/api` path → 503 | §3.3–3.4. The check path is the one row in the milestone that can invert the meaning of every signal downstream. |

| # | Task | Depends | Cer. | Prove it by | Why it cannot be skipped |
| :--- | :--- | :--- | :--- | :--- | :--- |
| T-10 | S3 (Block Public Access ×4) + OAC + CloudFront per §3.5: TTL 0 on `/api/*`, all `AllowedMethods`, query string `All`, deep-link error override on **403 and 404** | T-02, T-09 | L2 | A hard refresh on a deep link returns the SPA with `200`; a request carrying `?per_page=2` reaches the ALB **with** the query string in its access log | §3.5's two quiet failures: both stay invisible until a bookmark or a future filter breaks, and neither produces a server-side error to find. |
| T-11 | **Frontend build guard (§9)**: fail CI unless `VITE_API_BASE_URL === "/"`, then build and sync `dist/` to S3 | T-10 | L1 | CI red on a branch that unsets it, green with it; the served `index-*.js` contains `"https://api"` zero times; the loaded page issues a real request to `/api/health` | §1.3: unset means a polished-looking SPA running on `localStorage` at the real domain, passing every "did the page load" probe. Highest-severity item in the milestone, and the fix is one `if`. |
| T-12 | Docker image pipeline: build pinned `--platform=linux/amd64`, push tagged with the git SHA, **record the digest** | T-03 | L1 | The digest is in `docs/tasks/m5-deploy-log.md` beside its SHA; `docker run` of that digest reproduces §1.4 (uid `1654`, listens on 8080, `401` on protected routes, no `HEALTHCHECK`) | D-M5-5. SHA tag plus digest reference is the difference between "roll back" meaning one command and meaning a memory. |
| T-13 | First end-to-end deploy **plus the §7 checklist, executed and recorded** | T-06…T-12 | L2 | All §7 steps green, raw output in the deploy log | The checklist exists because a deployment that looks healthy in the console can fail steps 3, 6 and 8 for three unrelated reasons. |
| T-14 | **Rollback rehearsal**: deploy a deliberately failing revision, watch the breaker restore the previous set, write the schema-vs-code answer | T-13 | L2 | The ECS events sequence captured (§8.3), plus an explicit "would a schema rollback have been needed here?" per observed step | Two load-bearing behaviours in §8 (advisory lock on `Migrate()`, breaker stall) were reasoned about and never observed. The cheap time to find out is a rehearsal, not an incident. |
| T-15 | Reconcile the stale docs in §11 | T-13 | L1 | §11's greps return only correction notes — no live claims of `MigrateWithRetryAsync`, in-image `curl`, container port 5000, or `MapFallbackToFile` | Six claims in four files were wrong in ways that made the docs disagree with the code. Left alone, the next session re-derives them wrongly. |
| T-16 | EventBridge rule → notification on ECS `Service Deployment` state `SERVICE_DEPLOYMENT_FAILED` | T-08 | L1 | A synthetic stall produces exactly one notification naming the service and stop reason | §8.4: the breaker *stops*, it does not *page*. A stalled-but-still-serving deployment is indistinguishable from a healthy one unless someone is told. |
| T-17 | Update `docs/STATE.md`: M5 subtask table from §6, the measured §1 findings, any new debt | T-13 | L1 | STATE.md's M5 rows agree item for item with §6 | STATE.md is this project's memory. A spec with no STATE.md entry reads as a plan nobody executed, and the next agent re-plans it from scratch. |

**Ordering that is not arbitrary.** T-01 → T-02 → T-03 serialised on external waits (certificates, subnet IDs) — **amended 2026-09-14: under §14's H-A there is no certificate wait, so T-02 no longer gates T-03 and the remaining serial dependency is subnet IDs only.** **T-06 and
T-07 are the only code changes and can start immediately**, in parallel with all provisioning, because they don't touch
AWS — deliberate, since they are also the two items whose *absence* is invisible from a local browser. T-11 gates the
whole frontend story: nothing in §7 step 8 can pass without it. T-14 is not optional, for the reason in its last column.

**Suggested commit ladder** (Conventional Commits, one concern each, `pk:commit`): `feat(api): SSE keep-alive heartbeat`
(T-06) → `fix(api): bound the health check with an explicit timeout` (T-07) →
`chore(ci): fail production build unless VITE_API_BASE_URL is /` (T-11) → `chore(infra): <resource>` per provisioning
task → `docs(m5): reconcile stale infra docs` (T-15) → `docs(state): record M5 progress` (T-17). T-06 and T-07 land as
**Red / Green / Refactor triples**, so history proves the failing test drove the implementation.

---

## 7. Validation checklist (the 12 steps that prove it)

Run top to bottom after T-13; record raw output in `docs/tasks/m5-deploy-log.md`. Each step names the failure mode only
*it* catches — steps that overlap are steps nobody will run twice.

1. **Image contract.** `docker build --platform=linux/amd64 -f api/Dockerfile .` succeeds;
   `docker inspect <image> --format '{{.Config.User}}'` → `1654:1654`; `docker history` shows **no** `HEALTHCHECK`
   instruction (§1.4). *Catches a rebuild that quietly re-added the root user, or the dead check.*
2. **Local smoke, DB up.** `docker compose -f api/docker-compose.yml up -d db`, then run the image with
   `ConnectionStrings__Default` pointed at it: `GET /` → the 23-byte marker; `GET /api/health` →
   `200 {"status":"ok","database":"reachable"}`; `GET /api/applications` with no cookie → **401** with a `problem+json`
   body. *Catches an image that passes CI and fails on a real kernel — §1.4's `nuget.org unreachable` is the same class.*
3. **Local smoke, DB down.** `docker stop jt-pg-dev`; `/api/health` → **503** within the T-07 bound, and **`/` still
   answers 200 with its 23-byte marker**. *Catches nothing in the deployment, and teaches the reviewer why §3.4's health
   path is a correctness requirement: this step is the evidence that `/` would have lied.*
4. **Session + CSRF from the deployed origin.** Register → login; `Set-Cookie` shows `__Host-JTSession` and
   `__Host-JTCsrf` with `Secure; HttpOnly; SameSite=Lax; Path=/` and **no `Domain`**; `POST /api/applications` without
   `X-CSRF` → **403**, with it → **201**. *Catches any hop that breaks the `Secure` premise (§1.2) — a route to HTTPS
   that isn't HTTPS fails here rather than at 3 a.m.*
5. **Deep link.** Open the live URL, create an application, hard-refresh: the SPA returns `200`, not 403/404.
   *Catches §3.5's S3-answers-403 case, which a 404-only rewrite silently misses.*
6. **Query string survives the edge.** Request any `/api` path with a query parameter and find it, verbatim, in the ALB
   access log. *Catches CloudFront's default `Forward: none`, which drops it with no error anywhere in the stack.*
7. **SSE end to end.** Authenticated stream on the deployed hostname: `: open` immediately, then `: keep-alive` every
   ~20 s, and a `POST` from a second session delivers `event: change` in the same second (§4.1's probes, repeated under
   production conditions). *Catches everything in §4 that a local test cannot see.*
8. **The SPA is really talking to the API.** In devtools → Network: a mutation produces an `/api/…` request **from the
   page**, and the served `index-*.js` contains no `"https://api"` string. *Catches §1.3's trap — the one failure mode
   where every other step on this list still passes.*
9. **Migration at boot, and only one runner.** With `desiredCount: 1` (D-M5-7) there is exactly one task and one
   `Migrate()` call — confirm **exactly one** `Applied migration` line in that task's log (§3.2).
   *Catches the assumption D-M5-4 was chosen on.*
   **[Revisit variant]:** when `desiredCount` moves to 2, run the same check across both task logs: one `Applied` line and one waiter, not two. If both apply, the advisory-lock assumption (§3.2) was wrong and D-M5-4's basis is gone.
10. **Rollback rehearsal.** Deploy a revision that cannot boot (bad secret value, or a deliberate throw): the service
    stalls, the previous task set keeps serving, and **`/` and `/api/health` answer from the old tasks throughout**.
    Record the events. *Catches the difference between reading D-M5-7 and having it work.*
11. **The stall is announced.** During step 10, T-16's rule fires exactly once. *Catches "it stopped, but nobody knew".*
12. **`npm run verify` on the commit that produced the deployed artifacts**, and CI green on the PR that changed
    `Dockerfile`. *Catches a container built from repo contents nobody type-checked — the gap that let six stale claims
    about this image reach a spec, all caught by reading the file instead of the doc.*

If step 9, 10, or 11 cannot be executed, the milestone is not complete: those three are where the reasoned-about
behaviour becomes observed behaviour, and D-M5-4's whole design rests on what happens at boot under two tasks.

---

## 8. Rollback

### 8.1 What "roll back" can mean here, and which of them are fast

Five distinct actions, ordered by how quickly a person can do them at 22:00:

| # | Rollback | Command shape | Time | Needs |
| :--- | :--- | :--- | :--- | :--- |
| R1 | Frontend only | re-sync the previous `dist/` to S3 + `/index.html` invalidation | ~2 min | the previous build kept — T-12's artifact or a downloadable CI artifact |
| R2 | API to the **previous task-definition revision** | `aws ecs update-service --task-definition <prev>` | ~4–6 min | revisions recorded in the deploy log (§10) |
| R3 | API to the **previous image digest** | register a revision pointing at the old `@sha256:…`, then R2 | ~6 min | §9 records digests; a mutable tag alone would make this a guessing game (D-M5-5) |
| R4 | Infra / misconfiguration | revert the IaC commit and let it re-apply | 10+ min | T-01's tool choice, and the discipline that nothing was clicked in the console |
| R5 | **Data** | RDS point-in-time restore to a new instance, re-point the secret | hours, and it is an outage | a restore that has never been tested — **[verify-at-apply]**, §8.5 |

**R2 is the one to reach for first**, and that is a design fact rather than a preference: the image is immutable and the
task definition is a versioned pointer (§3.8), so "undo the deploy" is a pointer move with no build inside it. Everything
slower than R2 is paying for something §7 should have caught.

### 8.2 The boot failure mode — what D-M5-4 actually buys

Measured shape (§1.5): `BootGuard.EnsureSafeToStart` runs at `Program.cs:155`, `db.Database.Migrate()` at **160**, and
`app.Run()` at **310**. A bad migration or a missing configuration key therefore makes the process exit **before it ever
listens**: the target group never sees it healthy and never routes to it. That is the whole argument for boot-time
migration — the failure is *early and structural* rather than *late and user-visible*.

**Be exact about what that catches, because the sentence above is easy to over-read.** It catches "the new task cannot
serve". It does **not** catch "the new task serves the wrong thing": a migration that succeeds and is *semantically*
wrong — a column re-typed, a constraint tightened, a seed that shouldn't have run — yields a perfectly healthy target
and a broken product. Nothing in §3–§4 detects that; only §7's steps 4 and 8 do, and only for the paths they happen to
exercise. That is the honest residual risk of D-M5-4, and the reason §8.4's additive-only policy exists.

Two consequences to settle before the first deploy:

- **With `desiredCount: 1` and D-M5-7's stop-then-start, only one process ever reaches `Migrate()`.** That is the
  conservative shape, and it is why the concurrent-migration question is deferred rather than answered. The revisit
  at `desiredCount: 2` (§3.2) reintroduces it: rolling updates bring the new task up before the old one stops, so two
  runners can call `Migrate()` at once. The claim that saves it — EF takes a Postgres advisory lock, so the second
  waits rather than racing {D} is **[verify-at-apply]**, and unverified today: TF-2026-09-13-001 proved that an
  *interrupted* migration rolls back, not that a *concurrent* one serialises. §7 step 9 exists to observe whichever
  shape is live, in the version the service actually runs.
- **If the migration is what fails, the failure is invisible to the health check**, because the process never serves
  `/api/health`. The only signal is ECS's deployment state — which is why T-16's notification is a requirement, not a
  nicety (§8.3).

### 8.3 How the circuit breaker behaves, and the two settings it needs

D-M5-7 adopted ECS's deployment circuit breaker. The reasoning is worthless without its prerequisites:

- **A task that exits before listening is still caught.** With no `deploymentAlarms`, the breaker reacts to tasks that
  fail to reach `RUNNING`/healthy — which is precisely the boot-migration failure — and re-points the service at the
  previous task set. Adding `deploymentAlarms` for `/api/health` covers the second shape: a task that starts, listens,
  and serves 503.
- **`deploymentMaximumPercent` / `deploymentMinimumHealthyPercent` are the concurrent-migration dial**, and this spec
  does **not** guess them. The default 200 %/100 % starts two new tasks before the old two stop, which is exactly the
  overlap above; 100 %/100 % looks like the fix but can *stall* a rolling update whose new tasks cannot start until old
  ones stop. **[verify-at-apply]** the pair, and say which race it removes. Note the honest implication: **if the
  overlap turns out to be intolerable, the rejected Option A (a separate one-shot migrator) comes back** — D-M5-4's
  ranking is conditional on the advisory lock, not unconditional.
- **Rollback is a state ECS reaches, not an outage event.** The service looks healthy minutes after a failed deploy,
  because that is the breaker working. So *without T-16's `SERVICE_DEPLOYMENT_FAILED` notification, "the breaker
  restored the old tasks" and "the deployment is wedged" are indistinguishable from outside the console.* That sentence
  is the entire justification for a one-rule EventBridge resource.
- The rollback does **not** undo a migration that already succeeded. §8.4.

### 8.4 Schema vs code: the rule that makes R2 safe

A rolled-back image still points at the same database, which now carries the **newer** schema. For that to be
survivable:

> **Policy: migrations in this project are additive-only** — new table, new nullable column, new index, new constraint
> validated in a separate step. A rename is add → backfill → dual-read → drop, and **the drop ships later than the code
> that stopped reading the old name**. A destructive change (drop column, narrow type, non-null without a default)
> requires a comment in the migration file saying what rollback looks like, and never ships in the same release as a
> changed API contract.

Why a rule rather than a tool: EF has no unattended down-migration story for Postgres that anyone should trust, and
`Database.Migrate()` **skips migrations the running assembly does not know about** — so an old image against a new schema
does not error, it simply ignores the added objects. That is what makes additive-only safe, and it is exactly why a
narrowed type or a dropped column breaks R2 *silently*. **[verify-at-apply]** that skip behaviour on a scratch database
before it is relied on: it is the sentence the policy rests on, and it is the kind of claim this spec has already been
wrong about seven times (§13).

M5 itself **adds no migrations** — `InitialCreate` plus the three auth ones already exist, so §1.5's extension parity is
the whole database story here. This policy is for M5-and-later, written now because a rollback rehearsal is a good time
to have a rule and a bad time to invent one.

### 8.5 What does not get rolled back

- **Rows written between the bad deploy and the rollback stay written.** Correct behaviour, stated so nobody reaches for
  R5 reflexively: R5 trades a five-minute wrong-behaviour window for an hour of downtime plus everything after the
  restore point.
- **RDS snapshots and `deletion_protection`** (§3.6) are not part of any rollback path; R5's first step is a restore
  rehearsal that has not been run, and a restore never tested is not a recovery plan.
- **The frontend and the API roll back independently**, which is itself a risk: an old SPA against a new API is the one
  combination §7 never tests. Mitigation is sequencing, not tooling — **deploy the API first, the frontend second**, so
  the disagreement window contains no user-visible action. **[verify-at-apply]** whether R1 alone is ever taken without
  considering R2, and record the answer in the deploy log the first time it happens.

---

## 9. CI/CD contract for v1

**Deployment stays manual (D-M5-5).** The reason is stated in `ci.yml`'s own `permissions:` block — *"Read-only: this
workflow builds and checks. It never deploys, publishes, or writes to the repository, because tagging, releasing, and
pushing to `main` are human decisions."* M5 does not weaken that, so CI's job is to make the **artifacts** trustworthy and
the human's deploy command a copy-paste — not to run the deploy. Corollary: **no AWS credentials in CI in v1 at all**, not
OIDC and not access keys. Add OIDC when (not if) deploys become automatic; never add a long-lived key "temporarily",
because that temporary state is the one that outlives the project.

### 9.1 What already exists, and the hole in it

Three jobs today: `verify` (frontend — `npm run verify`, the AC-14 bundle gate, and the DEBT-11 "build must not emit a
config shadow" assertion), `bundle-baseline-recheck` (manual), and `api`/`verify-api` (`dotnet build
-p:TreatWarningsAsErrors=true` + the Testcontainers suite, **skipped when no `api/` path changed** — and reporting
`success` when it skips, deliberately, per grill F-10). The `api` job is why §1.4's Dockerfile edit gets built and tested
rather than eyeballed.

**The hole.** `verify` runs `npm run build` with **`VITE_API_BASE_URL` unset**, so every green frontend build in this
repository's history has been a build of the `localStorage`/mock path (§1.3). CI has never produced the artifact that
ships, and the job would stay green while the deployed app talks to nothing. That is not a criticism of the existing gate
— it is the same failure the `verify-api` summary note was written against (*"read this note, not the check mark"*),
applied to a variable nobody thought to look at.

### 9.2 Required changes

1. **A named production build with a guard that fails, in the shape CI already uses.** `package.json` gains
   `"build:prod": "VITE_API_BASE_URL=/ npm run build"` — POSIX-only, which is fine because the project's verification
   commands in `AGENTS.md` are already run on Linux/macOS, and saying so beats adding a `cross-env` dependency for one
   variable. Then a step modelled on the DEBT-11 assertion:

   ```yaml
   - name: Assert the production build has a real API base URL (§1.3)
     run: |
       set -euo pipefail
       test "${VITE_API_BASE_URL:-}" = "/" || {
         echo "::error::VITE_API_BASE_URL must be exactly \"/\" for the artifact that ships."
         echo "::error::Unset or empty builds an app that answers from localStorage on the real"
         echo "::error::domain and never calls the API — and every \"did the page load\" check passes."
         echo "::error::See docs/specs/2026-09-13-spec-m5-aws-deployment.md §1.3."
         exit 1; }
   ```

   It must **fail**, not skip. `verify-api` documents precisely what a skipping-but-green job does to a reviewer's
   attention, and §1.3's trap is a silently-green failure by construction.
2. **`dist/` as an artifact with recorded provenance** — SHA, the `VITE_API_BASE_URL` value used, and the destination
   bucket, in the artifact's metadata step, so "what should I sync" is a lookup and not a bucket-browsing session.
3. **An image job that builds and pushes on `main` only**, with `platforms: linux/amd64`,
   `tags: <repo>:<git-sha>`, `push: true`, and the **digest echoed into the job summary**; PRs get `push: false` (the
   build must compile; artifacts must not multiply). Because `api/Dockerfile`'s context is the **repo root** with
   `COPY api/ …` (§1.4), the job must set `context: .` and `file: api/Dockerfile`. Setting the context to `api/` is the
   one edit that makes the build fail in a way that reads like a Dockerfile bug.
4. **`npm run verify` proven on the merge commit that produced the deployed artifact** (§7 step 12) — a `main`-only image
   job can otherwise publish a build of a commit nobody type-checked.
5. **Deploy commands live in §10, not in CI.** A `workflow_dispatch` "promote this digest" button is tempting and
   explicitly out of scope (§12.2): the same human action, plus a new failure mode — a workflow that can deploy is a
   workflow that needs credentials.

### 9.3 Bundle-gate interaction

`tests/build/bundleGate.sh` compares `dist/` sizes against a fixture built with **no** `VITE_API_BASE_URL`. Building with
`/` inlines a one-character difference into the served asset, so the gate stays green and AC-14's budget does not move.
The gate must **not** be re-baselined as part of this work: if it ever reports a delta afterwards, that delta is
information about a source change, not noise to absorb.

---

## 10. Operations runbook

Assumes §7 passed once. The point is to answer "what do I do now" without reading this spec.

### 10.1 First deploy, in order

1. Log in to ECR; build **locally** with `--platform=linux/amd64 -f api/Dockerfile .` **from the repo root** (that is
   where `COPY api/` expects to start, §1.4); smoke-test §7 steps 2–3 against `docker compose -f api/docker-compose.yml
   up -d db` — the only service that file defines (§1.5); push, and **write the digest down next to the SHA** in
   `docs/tasks/m5-deploy-log.md`.
2. Register the task-definition revision: `containerDefinitions[0].image` = the **digest**,
   `portMappings.containerPort = 8080`, `secrets[].valueFrom` = the §3.7 secret ARN, `logConfiguration` = T-03's group,
   `cpu`/`memory` = §3.2, `stopGracePeriod` = §4.3. **Leave any container-level `healthCheck` unset** — the same argument
   that deleted the Dockerfile `HEALTHCHECK` applies to the task-definition copy of it (§1.4): there is no shell tool in
   the image to run, and the only component that must decide "is this instance serving" is the target group on
   `/api/health` (§3.4).
3. `aws ecs create-service` (or `update-service`) with that revision, `desiredCount: 1`, `minimumHealthyPercent: 0`, `maximumHealthyPercent: 100` (D-M5-7),
   `deploymentCircuitBreaker: {enable: true, rollback: true}` **plus `deploymentConfiguration.deploymentAlarms`** listing
   the target group's alarm ARNs. Without the alarms half, D-M5-7 covers boot failures only (§8.3).
4. Confirm target health **before** touching DNS: `aws elbv2 describe-target-health` shows the task `healthy` (1/1, §3.2). With `desiredCount: 1` there is no spare; the deployment is the target.
5. `aws s3 sync dist/ s3:<bucket> --delete` with `--cache-control "public,max-age=31536000,immutable"` for the hashed
   assets, and a separate `--cache-control "no-cache"` copy of `index.html`. `--delete` is what stops a route removed
   months ago from still being reachable.
6. `aws cloudfront create-invalidation --paths /index.html` — not `/*` (§3.5).
7. Run §7 steps 4–11 against the live hostname. Then record, in one commit: **`VITE_API_BASE_URL=/` used, image digest,
   task-definition revision, distribution ID + deployment ID, invalidation ID, date.** That line is the difference between
   the next rollback costing four minutes and costing an evening.

### 10.2 Day-2 operations

| Question | Command shape | Notes |
| :--- | :--- | :--- |
| "What is running?" | `describe-services` → revision → `describe-task-definition` → image **digest** | §10.1 step 7 turns this from an investigation into a lookup. |
| "Promote a new image" | register revision N+1 with the new digest → `update-service` → watch events | One deploy, one log line. Never edit a revision in place — that is how §8.1's R2 stops being available. |
| "Roll back" | R2: `update-service --task-definition <previous revision>` | §8.1. Reach for this before R5 every time. |
| "Rotate the DB password" | put the new value in the secret → `update-service --force-new-deployment` | **Rotation without a forced deployment changes nothing**: the old value is already in the running tasks' environment (§3.7). This is the entry most often done half-way, and the symptom is a service that works until the next natural task restart, when it does not. |
| "Why is the fleet at 1 healthy / 1 draining?" | `aws ecs describe-services` → `deployments` (rollout state) → `events` | The `events` list is the only place a circuit-breaker rollback narrates itself (§8.3). |
| "Logs" | `aws logs get-log-events --log-group /ecs/job-tracker-api …`, or Insights | stdout only (§1.4). If a future change starts writing files, the container will not ship them — and it has no writable `$HOME` anyway. |
| "Who can deploy?" | one IAM role with `ecs:UpdateService` + `iam:PassRole` on the two task roles, read-only elsewhere | `PassRole` scoped to the two specific role ARNs; unscoped `PassRole` is a silent path to privilege escalation and costs nothing to get right at creation. |
| "Budget guard" | `aws budgets create-budget` with an email alert | On a personal project the honest control is a budget alarm, not a policy suite. The one surprise to guard against is a second ALB or a Multi-AZ RDS created by a template you did not read. |
| "Tear it down" | IaC destroy in order: service → ALB → target groups → RDS (**final snapshot first**; clear `deletion_protection` only at this step) → VPC → secrets | §3.6. Running the destroy once on a scratch account is the cheapest way to learn the ordering — and it is the same rehearsal that makes §8's R-path credible. |

---

## 11. Stale-document reconciliation

Seven of the claims in the morning draft of this spec were wrong, and every one of them traced back to a planning document
rather than to the code. **The documents below still carry the same class of claim.** Each row was grepped from the file
today, not remembered; "Action" is what T-15 does with it.

| File : line | Claim today | Measured reality | Action |
| :--- | :--- | :--- | :--- |
| `docs/m5-infra-plan.md:68` | "Target group \| ECS task (**port 5000**, HTTP)" | Container listens on **8080** (`ENV ASPNETCORE_HTTP_PORTS=8080`, §1.4) | change to 8080 |
| `docs/m5-infra-plan.md:79` | "Port \| **5000** (Kestrel HTTP)" | same | change to 8080, and name the env var that sets it |
| `docs/m5-infra-plan.md:85` | "Security group \| Allow **5000** from ALB SG" | same | change to 8080 — a literal `iptables`-shaped mistake that would be applied as IaC |
| `docs/releases/m5-aws-deployment.md:116` | "target group (**port 5000**, HTTP), health check on `/api/health`" | 8080; health path is **right** | port only; leave the check path alone |
| `docs/releases/m5-aws-deployment.md:118` | "task definition: `job-tracker-api:**latest**` (256 CPU, 512 MB, port 5000)" | D-M5-5 pins **digest** (§3.8, §8.1 R3); 8080 | replace `:latest` with the digest, port 8080 |
| `docs/releases/m5-aws-deployment.md:59` | expects `200 { "status": "ok", "dbPing": "<latency>" }` | body is `{"status":"ok","database":"reachable"}`; **no latency field exists** (`Program.cs:207`) | drop `dbPing`, or file it as a feature request; a smoke test asserting `dbPing` fails against working code |
| `docs/releases/m5-aws-deployment.md:71` | "ALB target health: **1/1** healthy" | matches D-M5-7's `desiredCount: 1` | **correct as-is** — stale parts are port and `:latest` tag in the same paragraph, not the count |
| `docs/aws-deployment.md:46` | `ConnectionStrings__Default` \| "AWS Secrets Manager → **task env**" | D-M5-6 chose `secrets[].valueFrom` (§3.7); a plaintext `environment` entry is the thing `BootGuard`'s own comment warns against | rewrite the row |
| `docs/aws-deployment.md:55` | "Enable `UseForwardedHeaders` for the ALB" | **already enabled**, reading `ForwardedHeaders:KnownNetworks` (§1.2) | change "enable" to "configure the CIDRs" |
| `docs/aws-deployment.md:57` | "`BootGuard.EnsureSafeToStart` (**Program.cs:137**)… **Extend it** to also assert `ConnectionStrings:Default` is present and `Cors:AllowedOrigins` is non-empty" | both asserts **already implemented**; call site is `Program.cs:155` (§1.1) | delete the to-do, correct the line, keep the "crash at startup" rationale that it gets right |
| `docs/aws-deployment.md:71` | "**Add `GET /api/health`** (does not exist yet)" | it exists, and is the pivot of §3.4 | rewrite as "already present; the target group points at it" |
| `docs/aws-deployment.md:10` (§3 table) | `ForwardedHeaders:KnownNetworks` = "CIDR(s) of the **ALB/CloudFront**" | CloudFront never reaches the task — it terminates at the ALB, so only ALB/VPC addresses can ever appear as the direct peer (§3.3) | drop "/CloudFront"; leaving it invites someone to paste CloudFront's published prefix list and silently break scheme detection |
| `docs/aws-deployment.md:29` | Option B = "have the API serve the SPA in Production too by flipping the `IsDevelopment()` guard" | **accurately describes** the code (§1.2) — and D-M5-1 rejected it, because it changes the cookie and CORS surface | annotate "Rejected — see D-M5-1/§2"; the sentence reads like a cheap shortcut otherwise |
| `docs/m5-infra-plan.md:200`–211 | Cost: "Fargate (256/512, **1 task**) ~$12 … **Total ~$75/mo**" | matches D-M5-7's single task; ALB health-check pricing is unbudgeted (§4.3's flag) | re-cost at 2 tasks + health checks; keep §12's NAT decision visible next to it |
| `docs/m5-infra-plan.md:213` | "skip NAT gateway (use public subnets for ECS) to reduce cost" | a real ~$32/mo saving (43 % of that estimate), but it contradicts §3.1's private-subnet design | **promote to an owner decision** (§12.1-D), with the trade written down rather than discovered at apply time |
| `docs/tasks/TASK-m5-aws-deployment.md:39` | "AC-1: `GET /api/health` → 200 **with DB ping**" | the 200 proves a successful `CanConnectAsync` and carries no latency | reword AC-1; the acceptance criterion is otherwise sound |
| `api/README.md:33` | `dotnet run … # http://localhost:**5000**` | `launchSettings.json` binds **5039** (and `7053` for https) | correct the comment; note in the same line that **8080** is the *container* port, so dev and prod stop being conflated |

Two of these rows are worth reading twice, because they are the ones that would have shipped: **the three `5000` rows in
`m5-infra-plan.md` become real, applied infrastructure** the moment an IaC tool is chosen (§12.1-C) — a wrong port in a
table is free, and a wrong port in a template is an outage with a confusing error. And `aws-deployment.md:57`, which tells
the next reader to **extend code that is already written**; had this spec's morning draft trusted it, §1.1's fail-fast row
would have "proposed" a change that exists, and the review would have been about a diff nobody should write.

**One stale claim survives in this spec's own text** and must not be quietly deleted: `docs/m5-infra-plan.md:196` says
"Database — No rollback (Expand-Contract, D-M5-4)". That is still the right rule (§8.4), and it is left alone. Naming it
matters as much as naming the wrong ones: after correcting fifteen rows, a reader needs to know which four the audit
checked and **kept**.

---

## 12. Owner decisions — all seven answered 2026-09-14, **and B re-answered the same day under a constraint nobody had written down (§14)**; the six blockers stay cleared

Everything above is specified to the level this repository can support. The seven rows below cannot be: they are choices
about money, a domain name, and one person's risk appetite. The spec deliberately does not invent answers so that a table
reads as finished — that is the failure mode §1.1 documents and §13 counts. **Each row stays `Open` until the owner writes in the last column**, and T-01's exit criterion is literally "§12 has zero `Open` rows". **All seven were answered on 2026-09-14 — A and B chosen by the owner interactively, C–G ratified at this table's recommended defaults — and the seven Answer cells below are those answers written on disk, which meets T-01's exit criterion by count. Two residuals the answers name rather than paper over: B fixed the shape of the hostname story but not the zone's apex name, and the `aws` CLI is measured absent on this machine (`command not found`, 2026-09-14), so every `describe-*` here still belongs to T-02's first script, not to recall.

Ordering is not decoration. **A blocks C, D, E** (a region is a precondition for every `describe-*` below). **B was the
long pole**: DNS validation and an ACM certificate in *two* regions (D-M5-3) were the only steps in the whole milestone
with hours of latency and no retry that fixes them. **That sentence is now false in the direction of good news, and it is
false because of measurement rather than preference** — the account holds no hosted zone (`route53 list-hosted-zones` →
`[]`, both profiles) and no payment method can be attached to register one, so §14 settles B on the no-purchase route and
**the hours-latency external wait leaves the critical path with it.** What remains serialised is provisioning order only:
subnet IDs → RDS → the secret → the service. **G blocks nothing in §3–§11** and is therefore the one that can be decided
after the first deploy rather than before it.

### 12.1 The seven

| ID | Decision | What the spec assumes while it is open | The options, and the measured thing that decides | Blocks | Answer |
| :-- | :-- | :-- | :-- | :-- | :-- |
| **A** | **Workload region** | Nothing region-specific except two fixed facts: CloudFront's certificate must live in **us-east-1** (an AWS constraint, not a preference, §3.1), and one region must hold the VPC, ALB, ECS, RDS, ECR and Secrets Manager secret together. | Any region with Fargate + ALB + RDS PostgreSQL **18.6** + publicly-validated ACM. The binding constraint is parity with `postgres:18.6` in `api/docker-compose.yml` (§1.5): latency to a single-user app is worth tens of milliseconds, a major-version downgrade is worth re-deciding §1.5's `citext` story. Read it, don't recall it: `aws rds describe-db-engine-versions --engine postgres --region <r>`, `aws ec2 describe-availability-zones --region <r>` (the ALB needs 2), `aws ecr describe-repositories --region <r>`. **[verify-at-apply]** all three, into `docs/tasks/m5-deploy-log.md`. | T-02…T-05 | **A: `ap-southeast-1` (Singapore)** — owner, 2026-09-14. The three `describe-*` reads above stay open as T-02's first action into `m5-deploy-log.md` — no `aws` CLI on this box yet. |
| **B** | **Domain, DNS host, and who owns the zone** | `app.<domain>` as a placeholder; one SAN entry; DNS validation; **two** ACM certificates (us-east-1 for CloudFront, workload region for the ALB). | (1) A hosted zone already in this account — **[verify-at-apply]** `aws route53 list-hosted-zones-by-name`; cheapest and fastest to validate. (2) Buy a domain (~$12/yr) and delegate. (3) **No domain at all**: CloudFront's default `d111111abc.cloudfront.net` distribution name is HTTPS-valid, and `__Host-` cookies survive it (secure context, no `Domain`, path `/` — §1.2). But then **the ALB cannot get a public certificate**, because ACM will not issue for `*.elb.amazonaws.com` **[verify-at-apply]**, so D-M5-3's TLS-to-the-target collapses to CloudFront→ALB over HTTP:80 with the :80 listener restricted to CloudFront's managed prefix list. That is a *different decision record*, not a cheaper one — and it is the option to choose only knowingly. **Corrected after reading AWS's own text (§14.3), because the sentence above is under-specified in two ways that a deployer would act on:** the documented prefix list is **global**, `com.amazonaws.global.cloudfront.origin-facing` (IPv4) / `com.amazonaws.global.ipv6.cloudfront.origin-facing` (IPv6) — the regional `com.amazonaws.<region>.cloudfront.origin` form **does not appear in the current Developer Guide**, and the list costs **weight 55 of the 60 default security-group rules**; and restricting an ALB to CloudFront is documented with a **shared secret custom origin header plus a listener rule whose default action returns `403`** as its primary method, with the prefix list as the *optional* network layer ([CIT-A1-5]). Encryption is not either of them. | T-02 | **B: option (1) — a Route 53 hosted zone already in this account** — owner, 2026-09-14. **Zone apex name not yet written**; `app.<domain>` stands as placeholder until it is, and T-02's "confirm the zone" needs the literal. Option (3)'s weaker-TLS branch is moot; `list-hosted-zones-by-name` runs at T-02. **Answer superseded the same day, 2026-09-14 — kept above rather than deleted, per §0's rule that the only evidence of checking is the diff between what was asserted and what was measured.** Option (1) described a zone **this account does not have**: `route53 list-hosted-zones` → `[]` in both profiles and `route53domains list-domains` → `[]` ([`m5-deploy-log.md`](../tasks/m5-deploy-log.md), reads 9–14). **Re-answer: option (3) as route H-A** — CloudFront's default distribution domain, **$0**, chosen by the owner under the explicit constraint that **no payment method can be attached**; **H-B** (a free third-party subdomain whose unproxied nested `NS` delegates into a Route 53 zone, ~$0.50/mo) is filed in parallel and non-blocking, and **H-C** (registration, ~$17/yr) is deferred to a card rather than cancelled as a design. Consequences, the price correction that came with them, and the one thing this route still has to prove are §14; the hostname's *shape* requirement — **it must live in exactly one config value**, so paying $17 later is a redeploy and not a redesign — is invariant **I-A1-1** there. The apex string itself is deliberately in no tracked file: the repository is PUBLIC, the name is unregistered, and availability does not survive being read. |
| **C** | **IaC tool** | Nothing. §3, §8 and §10 are tool-neutral; D-M5-9 fixes the *requirement* (code, and state that lives off this laptop), not the tool. | **Terraform** (HCL; `plan` output is reviewable by someone who never read the code; a second language in the repo; state needs its own bucket + locking). **AWS CDK** (TypeScript — the language this repo already has 22 files of; the stack is code you can unit-test; the synthesised template is a debugging artifact you rarely read). **Raw CloudFormation** (no new toolchain, no abstraction, one long YAML, drift detection built in). The factor that decides it here is the one AGENTS.md is about: the owner has to defend this in an interview, so *which plan can you read aloud* beats *which scales to a team*. Note that §10's read-only `describe-*` verification commands stay valid under all three — they read state, not source. | T-03 | **C: AWS CDK (TypeScript)** — owner ratifying the recommended default, 2026-09-14. Rationale adopted from this row: a unit-testable stack in the repo's own language, defensible in interview. New `devDependencies` land with T-03 — this decision record adds none. |
| **D** | **VPC shape: bought CIDR with three subnet tiers, or the default VPC** | §3.1's requirement stands either way: ALB in public subnets (2 AZs), tasks in private-with-egress, RDS in private, and the ALB's SG as the only ingress to `:8080`. | Default VPC = zero CIDR planning and every subnet public, so a slipped RDS SG is an *internet-reachable* database. Bought `/20` = three tiers, and a routing question to answer honestly: **why do the tasks need a NAT at all?** This app makes no outbound HTTP calls — no webhooks, no external fetches — so the real consumers are the ECS agent pulling from ECR, Secrets Manager, and CloudWatch Logs, all three of which have **interface or gateway endpoints** costing ~$0.01105/hr each instead of ~$32/mo for two NATs (§11's row on `m5-infra-plan.md:213`, which called the saving "real"; it is real, and the endpoint route is the version of it that keeps the subnets private). **[verify-at-apply]** endpoint availability per service in the chosen region. | T-03 | **D: bought `/20`, three subnet tiers, VPC endpoints instead of NATs** — recommended default, 2026-09-14. Endpoint availability for ECR / Secrets Manager / CloudWatch Logs in `ap-southeast-1` stays `[verify-at-apply]` at T-03. |
| **E** | **PostgreSQL footprint, and which role creates `citext`** | §3.6: one instance, single-AZ, a `db.t4g.micro`-class opening size, 20 GB gp3, 7-day snapshots **[verify-at-apply]** each; migrations run as the app's own role at boot (D-M5-4). | Instance class and storage are a bill, not an architecture. The **first-deploy blocker** is §1.5's `citext`: `InitialCreate` calls `CreateExtension("citext")` and a non-superuser cannot. Options: (a) run the app as the master user — works, and makes every application bug a superuser bug; (b) **create the extension once, by hand, as the master, before the first deploy** — one `CREATE EXTENSION IF NOT EXISTS citext;`, keeps the app role unprivileged, and the migration's own call then has nothing to do; (c) a separate migrator role for a one-off `dotnet ef database update` — which is D-M5-4's *rejected* shape, revived for one command. (b) is the smallest change to what already exists. **[verify-at-apply]** that EF tolerates a pre-existing extension by reading `Migrations/20260909125540_InitialCreate.cs`'s generated `Up()` **before** the first deploy, because option (b) is only safe if it does. | T-04 | **E: option (b)** — recommended default, 2026-09-14: `CREATE EXTENSION IF NOT EXISTS citext;` run once by hand as master before the first deploy; the app role stays unprivileged. The EF-tolerates-pre-existing-extension read of `Migrations/20260909125540_InitialCreate.cs` is a repo-file read, not an AWS call — due at T-04's opening. |
| **F** | **Secrets Manager rotation** | D-M5-6: one secret holding one connection string, **no** automated rotation, and the documented manual path in §10.2. | Automated RDS-managed rotation needs a dual-user switchover and either a proxy or a re-read; this app has neither, and the measured shape that blocks it is §4: an `EventSource` connection may stay open for **hours**, and the task reads its secret exactly **once**, at start (§3.7, `valueFrom`). So the manual path is not "rotation, but worse" — it is the only sequence that cannot half-rotate: change the master password, write the new value, **force a new deployment**, because a rotation without a forced deployment changes nothing about a running task. | T-04 | **F: manual, as specced — D-M5-6 stands**, 2026-09-14. The forced-new-deployment is part of the sequence, not an optimization: without it the running task never re-reads the secret. |
| **G** | **How much of the deploy is automated** | D-M5-8: CI builds and pushes the image; a human runs §10's commands. | (1) As specced. (2) `workflow_dispatch` "promote this digest" — tempting, and §12.2's resident objection: a workflow that can deploy is a workflow that needs credentials; the honest middle is (2) **with** OIDC role assumption instead of long-lived keys **[verify-at-apply]** whether the account permits an OIDC provider, which removes the credential objection and leaves only the judgment one. (3) Auto-deploy on `main` — D-M5-8's rejected (a): it deletes the only place a schema-moving deploy can pause for a backup. G can be decided after the first deploy, which is why it is last. | — | **G: (1) as specced — CI builds/pushes, a human runs §10**, 2026-09-14. This row's own note holds: revisit as (2)+OIDC after the first deploy; OIDC-provider availability stays `[verify-at-apply]` for that conversation. |

### 12.2 Out of scope for v1, with the reason each one is out

An unbounded "not yet" is how a personal project dies at 3 a.m. Each of these was considered, and each has a specific
measured thing that makes it wrong *here* rather than merely big.

| Excluded from v1 | The reason, measured |
| :-- | :-- |
| **`desiredCount > 1`, autoscaling, Fargate Spot** | Two independent blockers, and the second is worse than the first. (i) A rolling update that brings the new task up before the old one stops is two concurrent `Database.Migrate()` runners — D-M5-4's stated expiry condition (§8.2). (ii) **The event bus is in-process**: `ApplicationEventBus.cs:30-31` says so in the running code ("two instances each notify their own connected clients and no others"), as does `Program.cs:69`, under `ASSUMPTION-m3-backend-api-002` (single instance through M5). A second task does not add capacity; it **drops events** for anyone parked on the other one. Scaling out is therefore an *architecture* task (PG `NOTIFY`, or a shared channel), not a number in a service definition. |
| **Blue/green with CodeDeploy, or a staging environment** | Both revisions point at one RDS instance, so it inherits (i) above and adds a second migration surface. It also assumes concurrent users, which §3.2's stop-then-start window is cheap precisely because there are none. |
| **A `workflow_dispatch` deploy button** | §9.2 item 5, and row G above. Credentials in CI are the cost; "the same human action, minus the part where a person is watching" is the benefit nobody asked for. |
| **WAF, geo-restriction, Shield, edge rate-limiting** | The threat surface is one password-protected user (§1.2: nine routes, one of which accepts credentials), and `/api/health` is the only anonymous non-marker route. WAF rules add a false-positive class that would block the owner's own login, with no measured threat to pay for it. |

| **Automated secret rotation** | Row F. |
| **Multi-AZ RDS, read replicas, cross-region DR** | RPO is a daily snapshot either way for this data; multi-AZ buys availability the only user would not notice, at roughly double the line item. |
| **Re-adding an in-image `HEALTHCHECK`** | ECS does not act on a Dockerfile `HEALTHCHECK` (§3.2), so it would be a *decorative* signal while the target group is the real one (§3.4). It stays deleted for the same reason a test that asserts nothing should. |
| **`BootGuard` asserting non-empty `Cors:AllowedOrigins`** | §1.2: under the single-origin topology of D-M5-1 an **empty array is correct**, so the assertion inherited from `docs/aws-deployment.md` §3 would reject a valid production config. Dropped; §11 carries the correction. |
| **Anything needing a second database** | §1.5's migration set is the whole schema story, and M5 adds no migrations (§8.4). |

---

## 13. What the morning draft got wrong

Seven factual claims in the draft written earlier on 2026-09-13 did not survive measurement. They are listed here rather
than quietly corrected, for the reason §0 gives: the reader who finds this document after a failed deploy needs to know
that its author checked, and the only evidence of checking is the diff between what was asserted and what was measured.

| The draft said | What is true | How the difference was caught | Where the correction lives |
| :-- | :-- | :-- | :-- |
| `GET /api/health` returns `{"status":"degraded","database":"unreachable"}` when the DB is down, and the ASP.NET `MapHealthChecks` middleware can be pointed at `/healthz`. | The failure body is **not** that JSON — the handler returns a bare `Results.StatusCode(503)` and problem-details fills it (`503`, `application/problem+json`, **172 bytes**, `title: "Service Unavailable"`). `/live`, `/ready`, `/healthz` **do not exist**: `grep -rn MapHealthChecks api/src` → no hits. | Ran the built image with the database unreachable and read the response; grepped for the middleware. | §1.2, §3.4 |
| `MigrateWithRetryAsync` applies migrations at boot "with bounded retries and backoff". | No such symbol exists. `Program.cs:157-160` calls `db.Database.Migrate()` inline; `grep -riE 'retry\|backoff' api/src` returns only unrelated comments, and a migration failure is **terminal** (measured twice: blank connection string → `InvalidOperationException`; wrong host → unhandled `NpgsqlException`, no `Now listening on` line). | Read the file the draft had already cited. | §1.5, D-M5-4 |
| The target group and task definition use **port 5000**. | **8080** — the base image sets `ASPNETCORE_HTTP_PORTS=8080` and the socket was measured listening there, while the image's own `EXPOSE 5000` and health probe pointed at 5000 and produced a permanently-`unhealthy` container (B-02). | `docker run … env`, an in-container socket list, and a probe from the host. | §1.4 B-02, §3.2, §3.4, §11 |
| Five AWS numbers, stated as fact: ALB idle timeout 60 s, ALB request timeout 60 s, CloudFront "idle 60 s / hard max 15 min", target group interval 30 s / timeout 5 s / thresholds 5-2, and a Kestrel `keepAliveTimeout` above 60 s. | None had been read from a source at draft time, and the Kestrel setting **does not exist in this codebase** — it was invented to fill a gap in the argument. | The rewrite refused to restate them. Each cell now carries `[verify-at-apply]` plus the command that reads it, and §5 states why a guessed timeout is the expensive kind of wrong. | §4.3, §5 |
| `desiredCount: 2` with a rolling update at `minimumHealthyPercent: 100` / `maximumHealthyPercent: 200`. | Not a wrong fact about AWS — a wrong read of **this** app. Two overlapping tasks means two concurrent `Migrate()` runners (D-M5-4's own expiry condition) and, worse, two in-process event buses each notifying only their own clients. | `ApplicationEventBus.cs:30-31`, which names the single-instance assumption in the running code. | D-M5-7, §3.2, §8.2, §12.2 |
| "ACM/DNS/ECR/S3/ALB/IAM stacks already created, commit `46c0e0c`"; `infra/` holds the templates; `deploy.yml` pushes the image. | No such git object (`git cat-file -t 46c0e0c` → could not find object), no `infra/` directory, no `.dockerignore` until B-03, and the only workflow is `ci.yml`. | `find`, `ls`, and one `git cat-file`. | §1.1 |
| SSE passes through CloudFront unchanged, so no API change is needed. | The API writes no keep-alive, so an idle stream's lifetime is decided by whichever proxy timeout is shortest — and the browser's silent reconnect then re-delivers events the client already has. | Reading the handler's write path, then §4.1's frame analysis. | R-4.2 (§4.2), §4.3, T-06 |

**The pattern, since seven rows is a pattern and not a run of luck.** Three of the seven were checkable by *running*
something, two by *reading a file the draft had already cited*, one by `git cat-file`, and one by refusing to assert an
AWS default without opening the AWS page for it. Not one required insight. What the draft lacked was not care but a
rule, and the rule is now the top line of §0: **measure it, or mark it `[verify-at-apply]` and write down the command
that would measure it.** A guessed number does not crash; it produces a stream that dies on a schedule and reads like an
application bug (§5), which is the most expensive kind of wrong this project can make.

What did *not* change: the topology, the cookie argument, the rollback ladder, and the nine decisions in §2 are the same
shape the draft had. What changed is that every load-bearing fact now carries its measurement, which is the difference
between a plan and a record.

---

## 14. Amendment A1 (2026-09-14) — the hostname route, and the one §2 decision it reverses

**Why this section exists instead of a silent edit.** §12 B asked *which zone should this app delegate into*. Measurement answered a
different question — *is there a zone, and can one be bought* — and the owner's constraint (**no payment method can be attached to this
account**) made the answer "no, and not for this milestone". That removes the premise of `D-M5-3`: with no registrable hostname there is
no certificate the ALB can present, and the decision that **rejected** "HTTP-only origin hop" now has to be re-read against a route that
has selected it. Rewriting §2 quietly would have destroyed the only evidence that anyone checked — §0's rule, and §13's. So the superseded
wording stays where it was written, struck through rather than deleted, and **this is the section a reader arrives at from it.**

Unlike §1–§13, this section's facts are not measurements of a repository. They are readings of AWS's own documentation, taken
2026-09-14, and each claim names its source — because the claim that decides the security posture here cannot be tested from this
machine at all, and §13's lesson is precisely what happens when a document asserts a platform fact nobody opened the platform's page for.

### 14.1 The rows this reverses, and where each now lives

| Surface | What it said | What is true under **H-A** | Where the change was made |
| :--- | :--- | :--- | :--- |
| **D-M5-3** | *"TLS terminates at the ALB as well as at CloudFront"*, with *"HTTP-only origin hop protected only by a CloudFront-source SG rule"* in its **Rejected** half | **CloudFront validates the origin certificate and refuses a self-signed one** ([CIT-A1-4]), and **ACM cannot issue for `*.elb.amazonaws.com`** ([CIT-A1-3]) — so the origin hop is **HTTP:80** and the rejected option is the deployed one, defended by unreachability + a shared header instead of by encryption | §2 `D-M5-3` amendment block |
| §3.1 diagram + table | `origin 2: ALB (HTTPS:443, own ACM cert)`; ACM row's *two certificates*; Route 53 row's alias | ACM row → **zero certificates**; Route 53 row → **no record at all**; CloudFront row → `http-only` origin policy; the diagram's "not-cleartext" claim withdrawn (the ASCII is left intact — a diagram edited to match a reversal nobody can re-read is how documents like this start lying) | §3.1 paragraph + three table rows |
| §3.3 | *"The HTTP:80 listener's only job is `redirect-to-https`; if it is absent, a mistaken HTTP path fails loudly"* | **:80 is the serving listener**; the loudness guarantee is replaced by prefix-list ingress + a default-`403` header rule, so §3.3's sentence stops being the defence it was written to be | §3.3 first and last bullets |
| §6 `T-02` | request **both** ACM certificates, complete DNS validation | no zone, no certificate, no validation; the task's substance is this section's `[verify-at-apply]` cells + the three console numbers | §6 (amended 2026-09-14) |
| §6 `T-09` | *"ALB + HTTPS:443 listener"*, proved with `curl -kv https://<alb>/…` | **ALB + HTTP:80 listener + the origin-header check**, proved with header/no-header pairs and a public-internet negative test | §6 (amended here) |
| §12 B | option (1), a hosted zone already in this account | **option (3) as H-A**, $0; H-B parallel; H-C when a card exists — and option (3)'s own description corrected on two points a deployer would have acted on | §12 B answer cell + correction block |
| §12 preamble | *"B is the long pole … hours of latency and no retry that fixes them"* | the external wait is **gone**; what remains serialised is provisioning order only | §12 preamble (amended) |

### 14.2 The three routes, priced from a measured source

Prices came off `aws route53domains list-prices` on 2026-09-14 — read-only, logged in
[`m5-deploy-log.md`](../tasks/m5-deploy-log.md): `.com` **$16**, `.dev` **$17**, `.app` **$20**, `.io` **$71**, `.click` **$3**, with
**register == renew for every TLD** (no first-year hook to be fooled by), and a hosted zone at **$0.50/month, charged at creation, free
if deleted within 12 hours**. Two non-price facts in that measurement are load-bearing: **promotional credit cannot pay registration
fees**, which turns a no-card account from an inconvenience into a hard block; and `aws.exe` calls sharing a `login_session` cache are
**not concurrency-safe** — ten parallel `list-prices` calls all failed with `CreateOAuth2Token … authorization grant is invalid` while
serial calls on the *same* token returned `rc=0`, so every AWS call in this milestone is serialised.

| Route | Hostname | Cost | What it keeps | What it costs | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **H-A** | CloudFront's default distribution domain, `d111111abcdef8.cloudfront.net` | **$0** | `__Host-` cookies (they need `Secure` + `Path=/` + **no `Domain`**, never a registrable domain — [CIT-A1-1]), ECS + ECR + S3/OAC, the cache behaviours, SSE through the edge, migrations-at-boot, **and zero certificates to manage** | **the encrypted CloudFront→ALB hop** (§14.4), a public URL that survives recreating the distribution, DNS validation as a lesson, the apex→`app` redirect | **selected** |
| **H-B** | free third-party subdomain whose **unproxied nested `NS` record delegates into a Route 53 hosted zone** | **$0.50/mo** (the zone) | nearly all of H-A's costs, *including the encrypted origin hop*: a name ACM **can** issue for, DNS validation inside a zone this account controls, the two-region certificate constraint, named exact origins | registrable-domain branding — and it **puts this milestone's cookie boundary on infrastructure a volunteer project can rename or revoke** | **filed in parallel, non-blocking** |
| **C → H-C** | register a `.dev` / `.com` | **$16–17/yr** + zone | the whole §3 shape, D-M5-3 intact, a hostname that is theirs | nothing but money | **deferred to a card**, and bound by **I-A1-3** to cost a redeploy, not a redesign |

**H-B is now worth more than it looked.** When this route was first priced, H-B was "the same lesson for sixty cents". §14.1 is why
that understates it: **H-B is the cheapest thing that restores D-M5-3**, because ACM will issue for a delegated subdomain this account
controls and will not issue for `*.elb.amazonaws.com`. Sixty cents is the price of not reversing a security decision — which is the
first argument for taking H-B up *before* the first deploy rather than after it. That is an owner call, not an agent call, and it is
recorded as one in §14.6.

**The price correction that came with the decision (DEBT-26).** Two passes and ~58 read-only calls were spent on a **$17** question
while `docs/m5-infra-plan.md` §7 — written earlier, never reconciled to ability to pay — priced the planned topology at **~$75/month**,
of which **~$32 is a NAT gateway** its own footnote permits skipping. The domain was **23 % of one month** of the architecture it sits
inside. `aws.amazon.com/free` was read the same day: *"up to $200 in credits"*, *"over 90 services for up to 6 months"*, *"no charges and
no surprise overages"*, credits *"automatically applied"* past always-free limits — so **the credit balance, not the card, may be the
runway** for compute, since only *registration* is excluded. Whether that is true of *this* account is
**`UNCERTAINTY-m5-aws-deployment-010-a`**, not a claim, and its resolution is the owner's three console numbers.

### 14.3 Claims, citations, and the one place AWS disagrees with itself

- **`DECISION-m5-aws-deployment-010`** — *M5's public hostname is CloudFront's default distribution domain (**H-A**); no domain
  registration and no hosted zone in this milestone; the hostname, when it arrives, must be reachable by changing one config value.*
  **Options considered:** (1) in-account zone — *falsified, the account has none*; (2) buy a domain — *blocked, credit cannot pay registry
  fees and no card can be attached*; (3) H-A; (4) H-B. **Selected:** (3), with (4) pursued in parallel and (2) retained as the end state.
  **Decided by:** the human, 2026-09-14, under the stated constraint; the agent priced the routes and verified the AWS behaviour claims,
  and did not choose among them. **Status:** accepted. **It amends `D-M5-3` rather than deleting it.**

Every Material Claim below links to a Citation or an Uncertainty record. Where AWS's own text does not settle something, the row says so
instead of borrowing confidence from the surrounding rows.

| ID | Claim | Verdict | Source, as read 2026-09-14 |
| :--- | :--- | :--- | :--- |
| **CIT-A1-1** | `__Host-` cookies require `Secure` (set from a secure page), `Path=/`, and **no** `Domain` — none of which needs a registrable domain. | **SUPPORTED twice** | MDN, *Using HTTP cookies → Cookie prefixes*, https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/Cookies#cookie_prefixes — *"Cookies with names starting with `__Host-` must be set with the Secure attribute by a secure page (HTTPS). In addition, they must not have a Domain attribute specified, and the Path attribute must be set to /."* **Plus this repository's own measurement**: `docs/spikes/2026-09-12-host-prefix-cookie-jar.md` (Chromium discards `__Host-` over plain `http` at *any* address and accepts it over `https://127.0.0.1` with a self-signed cert — the prefix is gated on the **scheme**, not on the domain's provenance). |
| **CIT-A1-2** | A distribution is HTTPS-reachable on its default domain with a certificate CloudFront provides; no alternate domain name is needed. | **SUPPORTED, with a nuance worth knowing** | CloudFront Dev Guide, *Require HTTPS for communication between viewers and CloudFront*, https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-https-viewers-to-cloudfront.html — *"If you're using the domain name that CloudFront assigned to your distribution … CloudFront provides the SSL/TLS certificate."* Nuance: the cert is documented as the **default CloudFront certificate `(*.cloudfront.net)`** (*All distribution settings reference*, `DownloadDistValuesGeneral.html`) — a wildcard over all distributions, not one minted for yours. Fine for reachability; it is also why no custom cert exists at all on this route. |
| **CIT-A1-3** | ACM will not issue a public certificate for the ALB's own `*.elb.amazonaws.com` name. | **SUPPORTED by suffix, not by name** | ACM User Guide, *Troubleshoot certificate requests*, https://docs.aws.amazon.com/acm/latest/userguide/troubleshooting-cert-requests.html — *"You cannot request a certificate for Amazon-owned domain names such as those ending in `amazonaws.com`, `cloudfront.net`, or `elasticbeanstalk.com`."* **Honest limit:** this is a *Note* under one error path, the list is introduced with *"such as"*, and no page names `elb.amazonaws.com` literally. It is the strongest statement AWS publishes, and the empirical form of it is `UNCERTAINTY-…-010-b`. |
| **CIT-A1-4** | CloudFront **validates** the certificate on an encrypted origin hop and rejects self-signed ones, so an ALB with no valid certificate cannot serve `https-only`. | **SUPPORTED** | CloudFront Dev Guide, *Require HTTPS for communication between CloudFront and your custom origin*, https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-https-cloudfront-to-custom-origin.html — *"When CloudFront uses HTTPS to communicate with your origin, CloudFront verifies that the certificate was issued by a trusted certificate authority"* and *"You can't use a self-signed certificate for HTTPS communication between CloudFront and your origin"*; *Security checklist for marketplace distributions* — *"CloudFront validates the certificate on connection"*. **No ELB/AWS-service validation exemption exists in the Developer Guide** (a search of the guide's pages turned up only *"this isn't required when you use an Amazon S3 origin or certain other AWS origins"*, which is about *installing* a certificate, not about CloudFront skipping validation). Failure mode if this is ignored: `502 Bad Gateway`, per the same page's name-match rule. |
| **CIT-A1-5** | The documented way to restrict an ALB to CloudFront is a **shared secret custom origin header + a listener rule whose default action returns `403`**; the prefix list is an **optional** network layer, and its AWS-managed name is **global**, not regional. | **SUPPORTED; the regional name in circulation is **not**** | CloudFront Dev Guide, *Restrict access to Application Load Balancers*, https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/restrict-access-to-load-balancer.html (header + `403` default rule primary; *"(Optional) Limit access to origin by using the AWS-managed prefix list for CloudFront"*), and *Locations and IP address ranges of CloudFront edge servers*, https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/LocationsOfEdgeServers.html#managed-prefix-list — `com.amazonaws.global.cloudfront.origin-facing` (IPv4) and `com.amazonaws.global.ipv6.cloudfront.origin-facing`. **`com.amazonaws.<region>.cloudfront.origin` appears nowhere in the current guide.** VPC note: *Working with AWS-managed prefix lists* — the CloudFront list carries **weight 55** against the default 60-rule security-group quota. Caveat to keep: AWS also warns *"Using HTTPS can help prevent an eavesdropper from discovering the header name and value"* — on an HTTP:80 origin hop the shared header travels in cleartext, so it authenticates CloudFront to the ALB against an off-path attacker and **not** against anyone on the path. §14.4 carries that as the residual. |
| **CIT-A1-6** | Origin protocol policy is per-origin (`http-only \| https-only \| match-viewer`), ELB origins arrive preconfigured to `https-only`, and the API accepts `http-only`. | **SUPPORTED** | *Origin settings*, https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/DownloadDistValuesOrigin.html#DownloadDistValuesOriginProtocolPolicy; *Template-preconfigured origin settings (ELB)*, `template-preconfigured-origin-settings.html#elb-origin-preconfiguration`; `API_CustomOriginConfig.html`. **Practical consequence for T-10/T-13:** the CDK/console default must be *changed*, not left alone — a distribution created against an ALB origin and left on its preconfigured `https-only` will `502` against an ALB with no listener certificate. |

- **`UNCERTAINTY-m5-aws-deployment-010-a`** — the three console numbers (plan type, remaining credit, payment methods present or absent).
  **Impact:** whether any of this is payable at all. **Validation action:** the owner reads them; **no M5 write happens first** (DEBT-26).
  **Owner:** human. **Status:** open.
- **`UNCERTAINTY-m5-aws-deployment-010-b`** — CIT-A1-3's suffix-only footing, and one real conflict between two AWS pages: the ELB
  guide's *Load balancer integrations* says *"CloudFront only supports ACM certificates in the US East (N. Virginia) us-east-1 region …
  change the CloudFront origin connection from HTTPS to HTTP, or provision an ACM certificate in … us-east-1"*, while the CloudFront guide
  allows an ELB-origin certificate issued **in any region**. **Unresolved between two primary pages.** It does not bind H-A (there is no
  origin certificate on this route at all), but it **does** bind H-B/H-C, so whoever restores the 443 listener must settle it empirically:
  request the workload-region certificate, attach it, and read whether CloudFront's `https-only` origin path works — **one `curl` through the
  distribution, not a paragraph of prose.** **Owner:** whoever lands H-B or H-C. **Status:** open, deliberately not treated as verified.

### 14.4 The failure mode this amendment introduces, as an FMEA row

| Failure scenario | P / S | Detection | Mitigation / fallback | Recovery |
| :--- | :--- | :--- | :--- | :--- |
| **CloudFront→ALB credentials travel the last hop in cleartext**, because :80 replaced 443 (§14.1). An off-path attacker gains nothing; **an on-path attacker inside the VPC sees `__Host-JTSession`, `__Host-JTCsrf`, and the shared origin header** — the header the mitigation depends on | Low (requires a foothold in the VPC or a compromised neighbor task) / **High** (session theft, and the `Secure` flag is a browser-side promise that encrypts nothing — D-M5-3's own words) | (i) ALB access logs + the task SG's flow logs, alerting on any :80 source that is not the ALB SG; (ii) the `403`-default rule's own hit rate, which is the *only* signal that someone is trying to reach the ALB directly; (iii) CloudFront's `502` rate, which is how a mis-set origin policy announces itself (CIT-A1-6) | Layered, and each layer says what it does *not* do: **prefix-list ingress** (`com.amazonaws.global.cloudfront.origin-facing`, CIT-A1-5) = L3/L4 filtering, widen-able by one console edit; **shared origin header + `403` default** = authentication, but see the cleartext caveat in CIT-A1-5; **task SG allows `8080` from the ALB SG only** (§3.3, unchanged); **RDS private, no public IP, TLS:5432** (unchanged). The real mitigation is that this state is **temporary and one config change from ending** | Attach a certificate and flip the origin policy back: **H-B at ~$0.50/mo or H-C at ~$17/yr** buys a name ACM will issue for, then D-M5-3 revives verbatim (I-A1-3). The rollback is a listener + `OriginProtocolPolicy` change, **not** a re-architecture — which is the only reason accepting this row is defensible at all |

### 14.5 Invariants added by this amendment (binding from T-03 onward)

- **I-A1-1 — one config value.** The apex, when it arrives, is **one** setting threaded through CloudFront aliases, the ACM ARNs,
  `Cors__AllowedOrigins`, and the JWT `iss` / cookie scope. Any design needing more than one edit to attach a domain fails this row.
- **I-A1-2 — the unregistered apex is in no tracked file.** The repository is PUBLIC, the name is known-unregistered, and availability
  does not survive being read. It enters the repo **in the commit that registers it**, or not at all.
- **I-A1-3 — H-C must cost ~30 minutes, not a redesign.** I-A1-1 restated as an acceptance test for whoever pays the $17, and the reason
  §14.4's recovery column is allowed to say "one config change".
- **I-A1-4 — every AWS call in this milestone is serial.** Parallel `aws.exe` on a shared `login_session` cache self-revokes (§14.2).
  Related, and now a **reliability** dependency rather than a hygiene preference: the deploy CLI authenticates as **account root** with
  **zero IAM users in the account**; `pk:ship` should be asked for a scoped identity before the first write, not after.
- **I-A1-5 — no M5 write before the three console numbers arrive** (DEBT-26), and none before this amendment is reviewed and merged.
- **I-A1-6 — no AWS platform fact enters this document without its source and access date.** §13's rule, extended: *"the docs said so"* is
  a citation only when the page, the sentence and the date are named, and it is an Uncertainty record when two pages disagree (§14.3, `-b`).

### 14.6 What this section does **not** authorise, and the decision it hands back

Not a deploy, not a tag, not a certificate request, not a paid resource, not a provisioning run: §9 and **R-9.2** still forbid a deploy
ahead of the human's word, and **I-A1-5** still holds. Two things are explicitly **the owner's call**, and the agent chose neither:

1. **Whether to buy H-B's sixty cents before the first deploy rather than after it** — §14.2 argues that restores D-M5-3 and removes
   §14.4's row, and §14.4's row is the only genuine security regression in this milestone.
2. **Whether the account's root-only identity is acceptable for the first write** (I-A1-4).

The next executable step is **T-03's stack shape on paper** — priced from the `[verify-at-apply]` cells and this section's citations, not
from the `m5-infra-plan.md` §7 table that DEBT-26 indicts — and it changes no platform until both gates above are answered.

