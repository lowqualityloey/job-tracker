# Checkpoint Record: M5 — AWS Deployment, checkpoint-003

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment` → [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md)
- **Specification**: [`docs/specs/2026-09-13-spec-m5-aws-deployment.md`](../specs/2026-09-13-spec-m5-aws-deployment.md) — **1,220 lines** (`wc -l`, measured 2026-09-14 14:03 UTC), §0–§13
- **Milestone / ceremony**: M5, **Level 3 (Release-Critical)** — `pk:ship` + explicit human authorization remain in force for anything that touches AWS. **Zero AWS calls of any kind were made in this checkpoint pass** (read-only included).
- **State**: **`handoff_ready`** — a self-imposed stop state at a session boundary. It prohibits implementation edits, AWS writes, and task switches until the §8 resume condition is satisfied. It was reached for a reason that is *not* fatigue: **the spec and the task record now disagree about what T-02 is**, and provisioning against either one without reconciling them would build the wrong platform loudly.
- **Author of this record**: Assistant (`pk:checkpoint`, entered via `pk:route` → the owner chose route 1), 2026-09-14 14:05 UTC
- **Branch / revision**: `main` = `origin/main` = `6da421a` (PR **#90** merge, `mergedAt 2026-09-14T11:19:05Z`, measured by `gh pr view`), and `git merge-base --is-ancestor origin/main HEAD` → **YES** with HEAD equal to it. This record lives on `chore/state-m5-post-90-checkpoint`, cut from that commit. **No head SHA of this branch is transcribed here** — DEBT-25: a head written into the commit that carries it is false on arrival. The writable form is the invariant: `gh pr view <this-pr> --json headRefOid` always equals `git rev-parse HEAD`, re-read immediately before anything depends on it.
- **Supersedes**: [`checkpoint-002`](./TASK-m5-aws-deployment.checkpoint-002.md) and [`handoff-001`](./TASK-m5-aws-deployment.handoff-001.md) **as the live pointer**. Both stay valid as evidence; but checkpoint-002 §5 records §12 B as a settled decision that measurement has since falsified (see §7), so a reader who trusts its §5 list without this record will be misled by a document that is otherwise current.

## 1. Objective (unchanged)

Make the application deployable to AWS (CloudFront + S3 SPA, ALB, ECS/Fargate, RDS PostgreSQL, Secrets Manager) while preserving the M4 cookie posture, fail-fast boot, and an unauthenticated `GET /api/health` the ALB target group can trust.

**What changed about the objective's shape, not its text:** M5's decision phase closed at checkpoint-002 and **reopened on exactly one item** — the hostname, and therefore where TLS terminates. T-02's measurement pass found the account holds no Route 53 zone; the owner then answered the follow-on question with a hard constraint (no payment method can be attached), which cancelled domain registration for this milestone rather than deferring it.

## 2. Completed since checkpoint-002 (`2026-09-14 06:15 UTC`)

| Concern | Artifact / PR | Verified how |
| :--- | :--- | :--- |
| checkpoint-002 landed | #85 → `3121b2d`, `MERGED 2026-09-14T06:23:49Z` | `gh pr view` + `merge-base --is-ancestor` |
| **T-02 step 0 executed read-only** — 58 logged calls across two passes and two regions, zero writes | #86 → `9e054e1` (07:05:51Z), #87 → `20ce0a8` (07:39:56Z), #88 → `62f5c9e` (08:09:43Z) | `gh pr view 86/87/88 --json state,mergedAt,mergeCommit` re-read at this boundary; evidence in [`m5-deploy-log.md`](./m5-deploy-log.md) |
| **Spec §12 B falsified by measurement**: `route53 list-hosted-zones` → `[]` in both profiles, `route53domains list-domains` → `[]` | task record §"Blockers / Open" | the reads themselves; the account is greenfield (ACM/ECR/S3/CloudFront/ECS/Secrets Manager/CloudFormation all empty, one default VPC, three public subnets) |
| Pricing pass: `list-prices` → `.com` $16, `.dev` $17, `.app` $20, `.io` $71, `.click` $3; register == renew; hosted zone $0.50/mo; **promotional credit cannot pay registry fees** | #89 → `fcd8ef9` (09:40:11Z) | `gh pr view 89` measured MERGED, `1cdd6b6` asserted an ancestor of `origin/main`; ten serial calls, twenty of forty bought nothing — recorded at that ratio |
| `aws.exe` is **not concurrency-safe** on a `login_session` cache: ten parallel calls returned `CreateOAuth2Token … authorization grant is invalid` while serial calls on the same token succeeded | task record §"Revised ladder status", step 2 | reproduced, then worked around by serialising; this promotes the scoped-identity question from hygiene to reliability |
| **DEBT-24 promoted to `AGENTS.md` commit-discipline rule eleven** + mirrored into `.agents/rules/job-tracker-learning.md` in the same commit (`31ae2e1`) | `AGENTS.md` | re-derived at this boundary: `awk '/^### Commit discipline/,/^CI \(/' AGENTS.md \| grep -c '^- \*\*'` → **11** |
| **DEBT-25, DEBT-26 opened**; §3A rebuilt on a measured merge | #89 docs commits, then #90 → `6da421a` (11:19:05Z) | `gh pr view 90` measured; CI on `6da421a` → **success** |
| **The hostname question answered by the owner** — apex name chosen, **no card attachable**, "go with your recommendations" → **H-A now ($0, CloudFront default domain) · H-B filed in parallel ($0.50/mo, unproxied nested `NS` into a Route 53 zone via `is-a.dev`) · H-C when a card exists ($17/yr, blocked on payment)** | task record §"The decision, taken under blanket delegation" | the chosen apex is **deliberately absent from every tracked file** — the repo is PUBLIC and availability does not survive being read |
| Prepared registration ladder steps 2–6 **withdrawn** (step 3 was its only irreversible spend) | task record §"Two things deliberately not done" | no AWS write of any kind occurred |

## 3. Changed files this checkpoint touched

`docs/STATE.md` (§1 header bullets, §2 M5 row + active-task row, §3 working set, §3A's four live bullets + dated amendment marker, §5 blockers + new **DEBT-27**, §7 item 1 pointer, §8 new row) and this record. **No source file, no config, no spec, no AWS resource.** `.m5-parts/` was left in place — see §6; deleting it is a separate concern and this is a documentation commit.

## 4. Verification evidence at this boundary

| Gate | Result |
| :--- | :--- |
| `date -u` | **2026-09-14 14:05:08 UTC** (all stamps in this record re-read against it, none carried over) |
| `git rev-parse HEAD` / `origin/main` / `main` | all three `6da421a`; branch 0 commits ahead when opened; `git status --porcelain` → only `.m5-parts/` |
| `gh pr list --state open` | **0** — nothing is in review |
| `gh pr view 86..90 --json state,mergedAt,mergeCommit` | five merges, listed in §2 with their measured `mergedAt` values |
| **`npm run verify` (this tree)** | **exit 0** — typecheck → lint (`--max-warnings 0`) → lint:css → test → build. **23 files / 227 tests passed**, 12.80 s; build **61.23 kB** gz, 1.42 s. Whole log read; **0** lines matching `failed\|FAIL\|✖\|error TS\|npm ERR` |
| CI on `main` @ `6da421a` | run **`34837595296`** → `success` (the #90 head's own verdict was read unfiltered in the pass recorded at `m5-deploy-log.md:265-266`: **23 files / 227 tests**, 9.07 s, build 978 ms, 0 failure lines, run `34833090998`, job `103940707373`) |
| `verify-api` on docs-only heads | **skip, not a suite pass** — read `.github/workflows/ci.yml:142-178` before quoting it; the step summary says `verify-api: skipped` |
| Standing full API verdict | CI #82: **0 failed / 210 passed** (1 m 12 s, job `103865396138`). Local suite remains **DEBT-23**-broken (nested-Docker Testcontainers), so **API-green is a CI-only claim on every PR until DEBT-23 closes** |
| AWS session | **not claimed live.** The last re-probe (previous pass) answered `INVALID_REQUEST`; nothing was retried this pass because this pass needs no session. Treat "profile `loey`, region `ap-southeast-1` required on every call" as the runnable form when it is next needed |
| AWS account facts carried forward from the §3A bullet this pass regenerated (so the compression loses nothing load-bearing) | Profiles `loey` / `jonell`, both **`login_session`**-based, with **no `credentials` file on either side** — there is no key path to leak, which is also why no secret hygiene work is owed here. `sts` resolved to `arn:aws:iam::<ACCOUNT>:root` in **both** profiles, and the two profiles are the **same account** (compared by hashing the digits, not by eye). Services empty: S3, CloudFront, ECS, Secrets Manager, CloudFormation, ECR ×2 regions, ACM ×2 regions, Route 53, Route 53 Domains. One `IsDefault: True` VPC, three public subnets (1a/1b/1c) → the ALB's two-AZ rule is satisfiable. **RDS offers PostgreSQL 18.6 — exact parity with `api/docker-compose.yml`**, which is T-04's premise (`citext` + `pgcrypto` proof) |
| `docs/STATE.md` structure | `grep -c '^## '` → **8** (same as `HEAD`); §4 lines **56**, `cmp`-identical to `HEAD`'s §4; §3A live bullets **4** under the dated amendment markers, ⛔ archive untouched |
| Dependency hygiene | runtime deps **3**, dev deps **19** — unchanged; this checkpoint adds nothing |
| `vite.config.js` | **absent** (DEBT-11 guard holds after the build the gate just ran) |
| Secrets / leak scan | staged diff scanned for `AKIA`, `aws_secret`, `BEGIN .*PRIVATE KEY`, `.env` paths → **0 findings**; the chosen apex is absent from every tracked file by design |
| Release-evaluation handoff | **N/A** — this repository is a consumer of PromptKit OS, not a PromptKit internal release; no tag, hosted release, changelog, deployment, or rollback is authorised or implied by this record |

## 5. Decisions and invariants the next session must not undo

1. **`D-M5-1` … `D-M5-9` stand** except where §7 explicitly puts one in question. `checkpoint-001` §5 remains the summary of record for them.
2. **§12 B's recorded answer — "a Route 53 hosted zone already in this account" — is falsified, and must not be cited as live.** Its replacement is an *owner decision under a payment constraint* (H-A), not a cheaper version of the same plan. Do not re-run the reads expecting a different answer; the account had nothing.
3. **`__Host-` cookies never needed a registrable domain** (`Secure` + `Path=/` + no `Domain`). Anyone re-arguing that a hostname is required for the auth posture is wrong, and this file previously said so — the retraction is in the task record, not here.
4. **H-A's TLS consequence is a different decision record, not a discount.** With no public certificate for the ALB, D-M5-3's "TLS all the way to the target" collapses to CloudFront → ALB over `HTTP:80`, with the :80 listener restricted to the CloudFront managed prefix list. Say it out loud in the amendment; do not let it ride in as a cost saving.
5. **The apex name lives outside the repository** until the commit that registers it. `gh repo view --json visibility` → PUBLIC; availability does not survive being read.
6. **The apex must live in exactly one config value** when it does arrive — threaded through CloudFront aliases, the ACM ARNs, `Cors__AllowedOrigins`, and the JWT `iss`/cookie scope — so that paying $17 later is a redeploy, not a redesign. That is now a requirement, not a hope.
7. **Serialise `aws.exe` calls.** A fanned-out ladder on a shared `login_session` cache self-revokes mid-deploy (§2).
8. **No NAT gateway** (§12 D, ~$32 of the ~$75/mo plan) and **no new `devDependencies` before T-03**, where CDK's arrive with a written justification each.
9. **`pk:ship` and the human merge gate are unchanged**; the deploy CLI authenticates as **account root** with **zero IAM users** in the account — unresolved, and the first thing `pk:ship` should be asked about.

## 6. Active blockers & open questions

- **Blocking M5 writes (owner, three console reads only):** account **plan type**, **remaining credit balance**, **payment methods present or absent**. No agent can see these; `aws.amazon.com/free` says "up to $200 in credits / up to 6 months", which may make credit — not the card — the real runway, since only *registration* is credit-excluded. **DEBT-26's rule stands: no M5 write until they arrive.**
- **Blocking T-02's completion (agent, and this pass deliberately did not fix it): spec §6 `T-02` and §12 B are stale against the owner's H-A decision.** Filed as **DEBT-27**, owned by `pk:plan` — see §7. Deliberately not repaired inside a checkpoint commit: it is a Level 3 contract edit wearing docs clothing, and it changes what an acceptance criterion promises.
- **M4 `AC-3`** — owner-side browser walkthrough, restrike-or-restate. Open since 2026-09-12; nothing unblocks it but a human with a terminal.
- **DEBT-21** — `.m5-parts/` (**28 files, 212 KB, `find | wc -l` and `du -sh` re-measured here**) is untracked, not gitignored, and its delete-after trigger passed at PR #81. It is one `git add -A` away from entering the record. Named, not self-executed: the owner chose the checkpoint route, not the housekeeping route.
- **DEBT-22** — `docs/aws-deployment.md` still carries five stale rows and is still the cited decision record. **T-15** owns it.
- **DEBT-23** — local full API suite cannot bootstrap Testcontainers; every PR pays the CI-only-verification tax until a nested recipe or a host `dotnet` returns.
- **Remote branch litter (owner's to delete):** `origin` still holds merged heads including `docs/m5-t02-step0-measurement` and `docs/m5-t02-retraction-close`. Agents do not delete branches here.

## 7. Scope change record — **pending, not applied**

The `pk:checkpoint` contract requires a linked scope-change record before objective, files, acceptance criteria, dependencies, non-goals, risk, or verification change. This is that record; **it authorises nothing by itself.**

| Field | Change proposed | Evidence forcing it | Status |
| :--- | :--- | :--- | :--- |
| Spec §6 **T-02** row | "Register/confirm the zone, request both ACM certificates, complete DNS validation" → the H-A shape: **no zone, no ACM certificate, no DNS validation**; T-02's exit becomes "hostname route recorded and its TLS consequence accepted", plus the `[verify-at-apply]` price measurements that were blocked by the dead session | `route53`/`route53domains` reads returned empty; owner answered "no card" | **Pending `pk:plan`** |
| Spec §12 **B** Answer cell | Correct option (1) → **option (3) shape (H-A)**, with H-B as a parallel non-blocking application and H-C deferred to a card | §2's falsification row | **Pending `pk:plan`** |
| Spec §2 **D-M5-3** | TLS terminates at the ALB too → TLS terminates at CloudFront; CloudFront → ALB over `HTTP:80` pinned to the CloudFront managed prefix list | ACM will not issue for `*.elb.amazonaws.com` | **Pending — this is a security-posture change, so it needs the owner's eyes, not a merge-time surprise** |
| Spec §6 **T-03** verification cell | "a private-subnet task reaches the internet" via NAT → via **VPC endpoints** (already §12 D's answer), with `[verify-at-apply]` endpoint availability for ECR / Secrets Manager / CloudWatch Logs in `ap-southeast-1` | §12 D answer + DEBT-26's cost reading | **Pending `pk:plan`** |
| Milestone risk register | Add: **a cookie boundary on infrastructure a volunteer project can revoke** (accepted only for H-B, which is why H-B is *parallel*, not the primary) | task record's H-B row | **Pending** |

**Nothing in this table has been written into the spec.** The spec on disk at `6da421a` still says the old thing, and the task record says the new thing. That divergence is the single most actionable fact this boundary produces.

## 8. Exactly one next action, and the resume condition

**Next action: the human reviews and merges this PR** (the checkpoint record + `docs/STATE.md` projection sync, one concern).

Then, and only then, the agent's next executable step is **`pk:plan` on the §7 table** — a Level 2 amendment inside a Level 3 milestone: restate spec §6 T-02, correct §12 B, and write D-M5-3's TLS consequence as an explicit, dated amendment rather than an implication. **No AWS write happens before (a) that amendment is on `main` and (b) the owner's three console numbers arrive.** T-03 (VPC/ECR/log group via CDK) is the first provisioning task after both, and it is `pk:ship`-gated.

**Resume condition**: task switches to `in_progress` when this PR merges **and** the receiver validates, before editing: the Task ID, the §7 divergence as still-open, the five invariants in §5 numbered 2–6, and DEBT-27's existence. Level 3 gates — human merge per PR, `pk:ship` before any deploy, zero pushes to `main`, no tags — are unchanged.

---

## Handover Prompt (paste into a fresh session)

```markdown
# Session Resume: M5 AWS Deployment — reconcile the spec to the H-A decision (`pk:plan`, Level 2 inside a Level 3 milestone)

## 1. Context & Environment
- **Branch**: start from `main` and MEASURE it (`git rev-parse main origin/main`, `git status --porcelain`). `docs/STATE.md` §1/§3A are the projection; `docs/tasks/TASK-m5-aws-deployment.checkpoint-003.md` is the live pointer. Read `AGENTS.md` first — eleven commit-discipline rules, and they are each a scar.
- **Active Task**: `TASK-2026-09-13-m5-aws-deployment` — M5, Level 3, state `handoff_ready` pending this session.
- **Current Status**: T-01 ✅ (§12 seven of seven, #84) · T-06 ✅ (#81) · T-07 ✅ (#82, CI 210/0) · T-02 **measured and decided but not reconciled**: §12 B's "zone in this account" is falsified, the owner chose **H-A ($0, CloudFront default domain)** with H-B parallel and H-C blocked on payment, and **spec §6's T-02 row still describes the pre-decision plan** (DEBT-27).

## 2. Key Files to Inspect
- `docs/specs/2026-09-13-spec-m5-aws-deployment.md` §6 (task table, ~line 824), §12 (decision table, ~line 1168), §2/§5 (D-M5-* decisions), §3.1/§3.3 (networking, the last-hop claim)
- `docs/tasks/TASK-m5-aws-deployment.md` §"The three hostname routes, priced and named" / §"The decision" / §"Two things deliberately not done" / §"Revised ladder status"
- `docs/tasks/TASK-m5-aws-deployment.checkpoint-003.md` §5 (invariants) and §7 (**the pending scope-change table — this session's work item**)
- `docs/tasks/m5-deploy-log.md` — the 58 read-only calls, and the redaction rule
- `.github/workflows/ci.yml:142-178` — the docs-only `verify-api` skip, before quoting any green check

## 3. Locked Technical Invariants (do not undo)
- `__Host-` cookies need `Secure` + `Path=/` + no `Domain` — never a registrable domain. Migrations at boot while `desiredCount = 1`; empty `Cors:AllowedOrigins` is *correct* same-origin; deploy by digest, tag by SHA; no in-image HEALTHCHECK.
- H-A means **TLS terminates at CloudFront** and CloudFront → ALB is `HTTP:80` restricted to the CloudFront managed prefix list. That is a decision change, not a discount — write it as one.
- The chosen apex is in **no tracked file** until the commit that registers it. When it arrives, it is **one config value** (CloudFront aliases, ACM ARNs, `Cors__AllowedOrigins`, JWT `iss`).
- No NAT gateway; no new `devDependencies` before T-03. Serialise `aws.exe` calls — a fanned-out ladder on a `login_session` cache self-revokes.
- API-suite green is a **CI-only** claim while DEBT-23 stands; docs-only `verify-api` "pass in 5 s" is a skip.

## 4. Current State & Immediate Next Step
- **Done**: everything checkpoint-003 §2 lists, five PRs (#86–#90) measured MERGED; `npm run verify` exit 0 on `6da421a` (23 files / 227 tests, 61.23 kB gz).
- **Next single step**: `pk:plan` — apply checkpoint-003 §7's table to spec §6/§12/§2 as a dated amendment (restate T-02, correct B, make D-M5-3's TLS consequence explicit). Docs-only diff, own branch, own PR, CI carries the verdict.
- **Hard stop that is not yours**: no AWS write of any kind until the owner supplies the three console numbers (plan type, remaining credit, payment methods). And nothing here authorises `pk:ship`, a tag, a deploy, or a push to `main` — those are human decisions, and the deploy identity is currently **account root with zero IAM users**, which `pk:ship` should be asked to fix.

Please inspect the files listed and confirm state matches before editing.
```
