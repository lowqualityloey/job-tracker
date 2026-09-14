# Checkpoint Record: M5 — AWS Deployment, checkpoint-001

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment` → [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md)
- **Specification**: [`docs/specs/2026-09-13-spec-m5-aws-deployment.md`](../specs/2026-09-13-spec-m5-aws-deployment.md) — 1,221 lines, §0–§13, nine decisions (§2), seventeen tasks (§6), seventeen stale-doc rows (§11), seven owner decisions (§12)
- **Milestone / ceremony**: M5, **Level 3 (Release-Critical)** — `pk:ship` + explicit human authorization
- **State**: **`handoff_ready`** — a *self-imposed* stop state, not an external one (see §8 for what it does and does not bar)
- **Author of this record**: Assistant (`pk:checkpoint`), 2026-09-13 17:06 UTC
- **Branch / revision**: `docs/state-m5-deploy-readiness-merged` @ `da04ee4`
  - `origin/main` = `80918be` (PR #80 merged); **HEAD contains `origin/main` and adds 2 commits** (`c8bd912`, `da04ee4`) that are **in no pull request**
  - local `main` = `7b3bc37` — an **ancestor** of `origin/main` (verified with `git merge-base --is-ancestor`), so it is fast-forwardable, not diverged. Reconcile pending.
  - **Branch-topology hazard**: `HEAD..origin/docs/state-m5-deploy-readiness-merged` returns `2e088a6` + `4909b03`, which carry the **same two commit messages** as local `c8bd912` + `da04ee4` under different SHAs. History was rewritten locally after #80 merged. Publishing therefore needs `--force-with-lease`, and `AGENTS.md`'s "the PR merged at a SHA your branch tip does not match" failure is live here — **verify `gh pr view <n> --json headRefOid` against `git rev-parse HEAD`** once the PR exists.

## 1. Objective (unchanged, restated from the Task Record)

Make the application deployable to AWS (CloudFront + S3 SPA, ALB, ECS/Fargate, RDS PostgreSQL, Secrets Manager) while preserving the M4 cookie posture (`__Host-` cookies, session gate, antiforgery gate), with fail-fast boot and an unauthenticated `GET /api/health` the ALB target group can trust.

## 2. Completed since the last boundary (`11edcd9`)

| Concern | Artifact | Status on disk |
| :--- | :--- | :--- |
| Release checklist | `docs/releases/m5-aws-deployment.md` (188 lines) | committed `c8bd912` |
| State projection sync | `docs/STATE.md` | committed `da04ee4` |
| Infra provisioning plan | `docs/m5-infra-plan.md` (234 lines) | committed `09bf9b5` — **and §11 of the spec names four stale claims inside it** |
| M5 spec, rewritten from measurement | `docs/specs/2026-09-13-spec-m5-aws-deployment.md` | **uncommitted**: +1,255 / −74 |
| Container build-context fix | `api/.dockerignore` (new) | **uncommitted, untracked** |
| Container restore + port contract | `api/Dockerfile` | **uncommitted**: ±61 |
| PromptKit OS engine bump | `.promptkit` gitlink `9ef20b8 → c7199c3` | **uncommitted** — must travel as its own `chore(promptkit)` commit |

## 3. Changed files this checkpoint touched

`docs/tasks/TASK-m5-aws-deployment.md` (reconciled — see §7), this record,
`TASK-m5-aws-deployment.handoff-001.md`, and `docs/STATE.md` §1/§2/§3/§3A-banner/§5/§7/§8.
**No source file, no config, no AWS resource was touched.**

## 4. Verification evidence — re-executed at this boundary, not carried over

| Gate | Command | Result |
| :--- | :--- | :--- |
| Full frontend gate | `npm run verify` | **exit 0** — typecheck · `eslint . --max-warnings 0` · stylelint · **227 tests / 23 files** (11.10 s) · `tsc -b && vite build` **1.38 s** |
| Bundle size | same run, build stage | `dist/assets/index-D3LZqJ4m.js` 190.89 kB → **61.23 kB gz**; CSS 5.21 kB → 1.56 kB gz. **Byte-identical to the 61.23 kB recorded at the 2026-09-13 boundary**, which is the expected result: no file under `src/` moved. |
| Typecheck, standalone | `npx tsc -p tsconfig.app.json --noEmit` | exit 0 |
| Container, incremental | `docker build -f api/Dockerfile api` | exit 0, manifest-list `sha256:c52ec89d…` — **but six steps reported `CACHED`, including `RUN dotnet restore`. This run did NOT re-execute the step whose failure the spec claims to have fixed.** |
| Container, clean-room | `docker build --no-cache-filter build -f api/Dockerfile api` | **exit 0**, manifest-list `sha256:ed0d92c0…`. Restore re-executed (`#11 … Determining projects to restore…`, 1.379 s), **zero `NU1015`**, `dotnet publish` completed (`#13 … JobTracker.Api -> /app/publish/`, 10.7 s). |
| API suite | `dotnet test` in `dotnet/sdk:10.0` | **NOT RUN.** No `api/` code changed in the working tree, so nothing was re-measured rather than assumed-unverified. The last recorded count (**169 passed / 0 failed / 0 warnings**) belongs to an earlier boundary and is quoted as history. |

**Why the last two rows are both in the table**: `AGENTS.md`'s "reproduce the gate's conditions before quoting its
verdict" rule was earned by an *incremental* build reported as clean. The first `docker build` is exactly that shape —
six steps `CACHED`, including the restore step the spec's claim is about — so its exit code is not the evidence; the
`--no-cache-filter` rerun is, and it is the one that licenses the spec's §1.4 claim. The `.dockerignore` half is carried
by the same runs: `#4 [internal] load .dockerignore → transferring context: 1.20kB` proves BuildKit reads it, and a total
context transfer of ≤ 9.07 kB against an `api/` tree of **76,544,478 bytes** (of which **75,927,831, or 99.2%, is host
`bin/`+`obj/`**) proves it is doing its job — without it, `COPY . .` would have uploaded tens of megabytes.

## 5. Decisions and invariants the next session must not undo

Nine in spec §2. The six that bind *code*, not consoles:

- **D-M5-1** — one CloudFront distribution, one public origin, two origins behind it. Rejected: the API serving the SPA (it changes the cookie and CORS surface). **Consequence**: in production `Cors:AllowedOrigins` is legitimately **empty** — asserting non-empty (as `docs/aws-deployment.md:57` still tells you to do) would reject a valid config.
- **D-M5-4** — migrations stay at boot (`Program.cs` `Database.Migrate()`); no separate migration step. Expired the moment `desiredCount > 1`.
- **D-M5-5** — tag the image with the git SHA, **deploy by digest**. `:latest` is a defect, and `docs/releases/m5-aws-deployment.md:118` still says `:latest` (§11 caught it).
- **D-M5-7** — rolling update gated on the ALB target group with the circuit breaker; `deploymentAlarms` are not optional, or the breaker is inert and §8's rollback story is fiction.
- **Single instance through M5** (`ASSUMPTION-m3-backend-api-002`) — the event bus is **in-process** (`ApplicationEventBus.cs:30-31`). Scaling out is an architecture task (PG `NOTIFY`), **not** a number in a task definition.
- **Container contract** — the port is declared in the Dockerfile that builds the image (`ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`), not inherited from a base image; runtime is `USER $APP_UID`; no in-image `HEALTHCHECK`, because ECS does not act on one and the target group is the real check.

## 6. Blockers

1. **§12 owner decisions A–G: seven open, six of them blocking T-02…T-05.** A = workload region (a precondition for every `describe-*`); B = domain / DNS host / who owns the zone, **and the long pole** — ACM in two regions, minutes-to-hours of latency, no retry that fixes it; C = IaC tool, where the spec states the deciding factor as *"which plan can you read aloud"* (interview-defensibility) rather than *"which scales to a team"*.
2. **`docs/aws-deployment.md` is cited as the ratified decision record, and the spec's §11 marks five of its rows stale** — `:46` secret delivery via task env (D-M5-6 chose `secrets[].valueFrom`), `:55` "enable `UseForwardedHeaders`" (already enabled), `:57` tells a reader to extend two asserts that are **already implemented** at a line number that moved, `:71` says `GET /api/health` does not exist, `:10` names CloudFront as a forwarded-headers peer (it never reaches the task). T-15 owns those repairs.
3. **Publishing hazard** — §Branch above: two commits in no PR, plus a stale duplicate remote head.

## 7. Record reconciliation performed here (a mismatch fixed, not silently absorbed)

`AGENTS.md` and the checkpoint contract both make the Task Record authoritative, so it was reconciled against the spec in
this pass. What changed in `docs/tasks/TASK-m5-aws-deployment.md`, and why each line was false rather than merely old:

| Task Record said | Measured truth | Action taken |
| :--- | :--- | :--- |
| `Branch: feat/m5-deploy-readiness` | branch is `docs/state-m5-deploy-readiness-merged` @ `da04ee4` | corrected |
| `State: in_progress (implementation of deploy-readiness changes)` | deploy-readiness code is merged (PRs #77/#78); the remaining M5 work is spec §6 T-01…T-17 | set to `handoff_ready`, with the stop state's scope written down |
| Four ratified decisions `D-M5-1…4`, e.g. "D-M5-2: single origin via CloudFront" | spec §2 carries **nine**, and its `D-M5-2`/`D-M5-3` mean different things (SSE through CloudFront; TLS at the ALB too) | **ID collision removed** — the Task Record defers to spec §2 as the numbering authority and keeps only the four owner-settled *constraints* |
| `AC-1: GET /api/health → 200 with DB ping` | body is `{"status":"ok","database":"reachable"}`; **no latency field exists** (`Program.cs:207`, per spec §11) | AC-1 reworded — a smoke test asserting `dbPing` fails against working code |
| `Blockers: None blocking` | spec §12: seven open, six blocking | replaced with the §12 pointer |
| `Next action: open a PR with the three changes` | those changes are merged; the tree now holds a spec rewrite + container fix + engine bump | replaced with the single action in §9 |

## 8. What the stop state bars, and what it does not

`handoff_ready` bars **implementation**: no IaC, no AWS calls, no `api/` or `src/` edits, no commits, no PR actions, no task
switch — until either the §12 resume condition is met or the owner overrides this record. Read-only measurement continues.
It does **not** bar §9, which is code work the milestone itself declares unblocked.

## 9. Exactly one prioritized next action

**T-06 — implement requirement R-4.2 (the SSE keep-alive heartbeat) in `ApplicationCatalog.cs:71-113`, as a Red / Green / Refactor triple.**

**This corrects a claim made earlier in the same session.** The turn that ran `pk:route` reported *"nothing here is
implementable; the milestone is parked on human ratification."* Spec §6 line 844 says the opposite in as many words:
*"**T-06 and T-07 are the only code changes and can start immediately**, in parallel with all provisioning, because they
don't touch AWS"* — and both carry `—` in the Depends column. The error is the familiar one: reasoning about a document
from its status line instead of reading its table. **The routing card from that turn should be read as amended.**

Why T-06 over the near-identical T-07: §6's own "why it cannot be skipped" says T-06's absence is *"likely to break in
production while remaining invisible locally"* — the asymmetry §4.1 measured, and the one item whose failure mode is a
silent outage rather than a loud 503. §4.2's four assertions are the exit criteria: a five-minute idle stream emits
**≥ 12 comment frames and zero `event:` lines**; a right-owner write still produces exactly one `event: change`; an
unrelated-owner write produces nothing; disconnect stops the writes.

**Runner-up actions, explicitly not chosen**: `pk:commit` the tree as four atomic commits + `pk:pr` (the mechanical
residual of this session — the owner may reasonably want it first, since a pushed branch is a CI-checked branch);
§12 A/B/C (owner-only, cannot be delegated).

## 10. Hygiene scan (Phase 3)

- **Secrets**: clean. Pattern scan for AWS access-key IDs, inline `password=` assignments and private-key blocks across
  all changed files plus `.m5-parts/*.py` returned exactly one hit — spec line 350, a **shape template** with `...`
  placeholders, not a value. No untracked `.env` exists. D-M5-6 puts the real string in Secrets Manager via
  `secrets[].valueFrom`, never in plaintext task `environment`.
- **Debug probes**: zero `[DEBUG-*]` markers in the changed files.
- **`.m5-parts/` — 28 files, 212 KB, untracked, and NOT gitignored** (`git check-ignore` → exit 1). Nothing under version
  control references it. It is the **reproducibility harness for the spec rewrite**: `assemble.py` applied the §0–§13
  replacement edits, `audit.py` checks dangling §-references, table-column agreement against each header row, and
  leftover drafting artifacts (`TODO`/`XXX`/`B-05`/`desiredCount: 2`), `refprobe.py` / `refscan.py` / `toc.py` are its
  probes, `m5-p*.md` are the parts, `spec.before-assembly.md` is the pre-assembly snapshot.
  **Disposition decided here: keep in place, untracked and un-ignored, and delete after the spec commit lands.**
  Reasoning is the repo's own `vite.config.js` rule — *a stray artifact should be LOUD in `git status`, then deleted*.
  `.gitignore`-ing it would hide a real thing that is not meant to be permanent, and committing 212 KB of one-use
  scaffolding into `docs/` would make the record worse than spec §13 already does by admitting it. **The owner can veto
  this in review; it is recorded, not silent.**
- **Uncommitted stable work**: yes — which under Phase 3 means `pk:commit` is owed before any *new* feature work starts.
  §9 argues why T-06 and the commit ladder are compatible, and why the ladder is the runner-up.

## 11. Release-evaluation handoff fragment (projection only — never an approval)

- **Evaluation ID**: `M5-AWS-DEPLOY-2026-09-13` (repository-local; no external release system exists here)
- **Release Candidate Commit**: **none** — no candidate has been designated. `da04ee4` is a docs commit on a branch, not a candidate.
- **Preliminary SemVer Candidate**: **no candidate.** `v0.2.0` in `package.json` is nominal; there is no tag and no remote release (STATE.md §1).
- **QA Status**: not reviewed. The only gate that has run is `npm run verify` (§4), which covers no AWS surface.
- **Unresolved Blockers**: §12 owner decisions A–G (§6.1); branch publishing hazard (§6.3)
- **Requested Release Coordinator Decision**: **answer §12 rows A, B and C** — workload region, domain/DNS owner, IaC tool — inside the PR that carries this spec, since all three change the *form* of every task in §6.
- **Source Evaluation / Task Record**: `docs/tasks/TASK-m5-aws-deployment.md` (canonical Local Task Source) · `docs/specs/2026-09-13-spec-m5-aws-deployment.md`
- **Handoff Status**: **Blocked — waiting on human input** (§12), with one unblocked code task (§9)
- **Handoff Boundary**: this is a handoff projection. Final evaluation and approval belong to `pk:ship` and the Release Coordinator. **No tag, release, publication, remote operation, deployment, or rollback action is authorized by this record.**


## 12. Disposition of the working tree at this boundary (what would be staged, and why)

**Nothing is committed.** This section names the intended commit ladder so that the next session executes a decision
rather than invents one — and so that the four concerns cannot quietly fuse into one commit whose message would then be
false about three of them. Order matters: 1 and 2 are the code and the spec; 3 is tooling noise; 4 is the projection that
*describes* 1–3, so it must land last.

| # | Type + title | Files | Why it is its own commit |
| :--- | :--- | :--- | :--- |
| 1 | `fix(api): restore cleanly and declare the port contract in the image` | `api/Dockerfile`, `api/.dockerignore` *(new)* | One behaviour, independently revertable: the image builds clean-room. Proven with `docker build --no-cache-filter build` **before** the commit exists — `R-9.3`'s "a commit that breaks the container build is a defect with a badge on it", inverted. |
| 2 | `docs(spec): rewrite M5 from measurements` | `docs/specs/2026-09-13-spec-m5-aws-deployment.md` | +1,255 / −74 of spec is a reviewable unit only if nothing else rides along. The owner of the first edition needs the diff to be exactly the thing they are being asked to re-ratify. |
| 3 | `chore(promptkit): engine bump 9ef20b8 → c7199c3` | `.promptkit` *(gitlink)* | Tooling churn has no business inside a spec or a fix — the rule PR #74 was opened to honour. |
| 4 | `docs(state): regenerate M5 projections and records` | `docs/STATE.md`, `docs/tasks/TASK-m5-aws-deployment.md`, `…checkpoint-001.md`, `…handoff-001.md` | Projections describe the state *after* 1–3 exist, so they cannot be written honestly before them. |

**`.m5-parts/` — the disposition, with its cost.** 28 files / 212 KB / ~2,394 lines: the scratch parts the spec was
assembled from (`part-01` … `part-12`, `part-r1` … `part-r5`, `merge.py`, `extract.py`). They are **not** in the ladder.
**Keep them untracked and unignored until commit 2 lands**, then delete them. Ignoring them instead would hide a live
`git status` entry that is the only reminder those files exist, and the spec's §12 table cites two of them as provenance
for measured numbers — a deleted-and-ignored harness makes those citations point at nothing without leaving a trace in
`git status`. **`git add -A` would commit all 28**; `pk:commit`'s single-concern staging is the guard, and this paragraph
is the note that guard was reasoned about rather than assumed.

**Verification state at the moment of staging:** `npm run verify` exit 0 and the clean-room `docker build` exit 0, both
re-executed at 17:02–17:06 UTC (§4). Commit 1's files have not changed since that build. **`dotnet test` has *not* been
re-run at this boundary** — no `api/` code changed — so no commit message here may claim a test count, and the numbers in
STATE.md §4/§5 that predate this session are history, not assertions.


