# Checkpoint Record: M5 — AWS Deployment, checkpoint-002

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment` → [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md)
- **Specification**: [`docs/specs/2026-09-13-spec-m5-aws-deployment.md`](../specs/2026-09-13-spec-m5-aws-deployment.md) — 1,220 lines (`wc -l`, measured 2026-09-14 06:12 UTC), §0–§13; §12 now carries seven filled Answer cells
- **Milestone / ceremony**: M5, **Level 3 (Release-Critical)** — `pk:ship` + explicit human authorization remain in force for anything that touches AWS
- **State**: **`handoff_ready`** — a *self-imposed* stop state at a session boundary, with a named resume condition (§8)
- **Author of this record**: Assistant (`pk:checkpoint`), 2026-09-14 06:15 UTC
- **Branch / revision**: `main` = `origin/main` = `3e2f2fb` (PR #84 merge, `mergedAt 2026-09-14T06:07:46Z`, measured by `gh pr view`); this record lives on `docs/m5-checkpoint-002`. All ancestry assertions made with `git merge-base --is-ancestor` after reading, never before.
- **Supersedes**: [`checkpoint-001`](./TASK-m5-aws-deployment.checkpoint-001.md) and [`handoff-001`](./TASK-m5-aws-deployment.handoff-001.md) *as the live pointer*. Both stay valid as evidence; checkpoint-001's §"what remains" and handoff-001's ladder have since executed and merged.

## 1. Objective (unchanged, restated from the Task Record)

Make the application deployable to AWS (CloudFront + S3 SPA, ALB, ECS/Fargate, RDS PostgreSQL, Secrets Manager) while preserving the M4 cookie posture, fail-fast boot, and an unauthenticated `GET /api/health` the ALB target group can trust. **M5's decision phase is now closed; what remains is provisioning and platform work.**

## 2. Completed since checkpoint-001 (`2026-09-13 17:06 UTC`)

| Concern | Artifact / PR | Verified how |
| :--- | :--- | :--- |
| Deploy-readiness code: SSE heartbeat (T-06), health bound (T-07) | ladder + PR #81; `a5fc654`→`4799856`→`a42070e`→`0986e53` + PR #82 | Red/Green counts at each commit; **CI `verify-api` full suite 210 passed / 0 failed (1 m 12 s)** on #82 |
| Publication of the 09-13 uncommitted payload | PR #81 (`dfe018c` → `b4c2888`, 03:46:01Z) | `headRefOid` asserted at create; merge asserted via `git merge-base` |
| T-07 state checkpoint | PR #83 (`5f2c87e` → `0b15c4b`, 05:51:55Z) | same assertions |
| **§12 owner decisions A–G recorded (T-01's exit)** | spec §12 Answer cells via PR #84 (`5eb76d5` → `3e2f2fb`, 06:07:46Z) | **grep `*Open*` in §12 → 0** — the exit criterion counted, not asserted; A/B chosen interactively by the owner, C–G at this record's recommended defaults |
| **Date-truth correction** | commit `1bbbc8f` (this branch) | the decisions pass had written **26 stamps as 2026-09-15; `date -u` and git committer dates read 2026-09-14** — corrected digit-only, structure re-verified |

## 3. Changed files this checkpoint touched

`docs/STATE.md` (§1/§2/§3A/§5/§7/§8), `docs/tasks/TASK-m5-aws-deployment.md` (header Branch/State/spec-lines, Next action), this record, and — as its own commit — the 3-file date correction. **No source file, no config, no AWS resource was touched; zero AWS calls were made this session.**

## 4. Verification evidence at this boundary

| Gate | Result |
| :--- | :--- |
| `date -u` | **2026-09-14 06:09 UTC** (the reason §2's correction row exists) |
| `git rev-parse main origin/main` | both `3e2f2fb`; `5eb76d5`, `5f2c87e`, `0986e53` each asserted ancestors via `git merge-base --is-ancestor` |
| CI on #82 | `verify-api` **Passed! 0 failed / 210 passed** (1 m 12 s); `verify` pass 39 s — the current standing verdicts |
| CI on #83 / #84 | `verify` pass 33 s / 30 s; `verify-api` "pass" in 5 s is the **designed docs-only skip** — `.github/workflows/ci.yml:164-178` says to read the step-summary note, not the check mark; the workflow was re-read at this boundary because the skip verdicts were being reported |
| `npm run verify` local | **not re-run**: zero `src/`/`api/` files differ from `3e2f2fb`, whose CI `verify` just passed — quoting that as the live verdict instead |
| `dotnet test` local | still DEBT-23 (nested-Docker Testcontainers bootstrap); CI is the full-suite verifier until recovered |
| `aws` CLI | **`command not found`** (measured, twice) — no `describe-*`/`list-*` has ever run; every `[verify-at-apply]` in §12 is owned by T-02 step 0 |
| Working tree | `git status --porcelain` → only `.m5-parts/` (28 files, 212 KB, **DEBT-21 — its delete-after trigger has passed**: the ladder it was held through merged in #81) |
| Structure of `docs/STATE.md` | `grep -c '^## '` = **8**; §4 `cmp`-identical to HEAD before and after both commits |
| `AGENTS.md` commit-discipline rules | re-derived: `awk '/^### Commit discipline/,/^CI \(/' AGENTS.md \| grep -c '^- \*\*'` → **10** |

## 5. Decisions and invariants the next session must not undo

All nine §2 `D-M5-*` decisions stand unchanged (checkpoint-001 §5 still canonical for them). New since then, all *decided* facts now living in spec §12 Answer cells: **A** `ap-southeast-1` · **B** in-account Route 53 zone, option (1) — the weaker-TLS default-hostname branch (3) is now dead and must not be re-proposed as "cheaper" · **C** CDK/TypeScript — new `devDependencies` arrive at T-03 with a written justification each · **D** bought `/20` + VPC endpoints, **no NATs** (the honest reason: this app makes zero outbound HTTP; do not re-add NAT "for safety") · **E** `citext` pre-created by hand as master before first deploy; app role stays unprivileged · **F** no automated rotation — the forced redeploy is *part of* the manual sequence, not an optimization · **G** human-run deploy commands; revisit (2)+OIDC only after the first deploy. §4's locked invariants are untouched — verified `cmp`-clean at both commits.

## 6. Active blockers & open questions

- **T-02's two gates (neither is a decision):** the **zone apex name** (B fixed the shape, the literal exists only in chat and cannot be applied from chat), and **AWS CLI + working credentials** on the machine that runs T-02 (absent here, measured). Owner action.
- **M4 `AC-3`** — owner-side restrike-or-restate, open since 2026-09-12.
- **DEBT-23** — local full API suite cannot bootstrap Testcontainers (nested Docker); recovery is optional but CI-only verification is a per-PR tax.
- **DEBT-21** — `.m5-parts/` delete-after step is now *due* (its stated trigger — the ladder — merged). Held pending the owner's word; one `rm -rf` away from either outcome.
- **No open questions with answers pending from the agent.** Nothing waits on me right now.

## 7. Scope changes

None. The §12 answers were the pre-declared exit of T-01; recording them changed no objective, AC set, or dependency graph — it *unblocked* one (T-02 → then T-03…T-05).

## 8. Exactly one next action, and the resume condition

**Next action: the human merges this PR (checkpoint-002).** After that, at a new session or turn: **T-02 step 0** — owner supplies the zone apex name and configures the AWS CLI locally with their own credentials (keys never enter chat), then the agent runs, in this order, into a new `docs/tasks/m5-deploy-log.md`: `aws route53 list-hosted-zones-by-name` (confirms B), A's three region `describe-*`s (confirm `ap-southeast-1` has Fargate + 2 AZs + RDS `postgres:18.6`), and only after all four read clean, the two ACM certificate requests (us-east-1 + workload region) — DNS validation is the hours-latency item, which is why it goes first and alone.

**Resume condition**: T-02 is authorized by spec §6 dependency (T-01 done) + the owner's 2026-09-14 instruction "start T-02"; it *executes* only on the two §6 facts above. Level 3 gates (`pk:ship`, human merge for every PR, zero pushes to `main`) are unchanged.

---

## Handover Prompt (paste into a fresh session)

```markdown
# Session Resume: M5 AWS Deployment — T-02 (certificates + DNS validation)

## 1. Context & Environment
- **Branch**: start from `main` @ `3e2f2fb`+ (measure `git rev-parse main origin/main`; PR #84 merged the §12 decisions). Read `AGENTS.md` first — the 10 commit-discipline rules are load-bearing.
- **Active Task**: `TASK-2026-09-13-m5-aws-deployment` — M5, Level 3, state `handoff_ready`; canonical local source: `docs/tasks/TASK-m5-aws-deployment.md`, live pointer held by `docs/STATE.md` §3A; current contract: `docs/tasks/TASK-m5-aws-deployment.checkpoint-002.md`.
- **Current Status**: T-01 ✅ (spec §12 has zero `Open` cells, grep-counted), T-06 ✅ (#81), T-07 ✅ (#82, CI full-suite 210/0). T-02…T-17 remain.

## 2. Key Files to Inspect
- `docs/specs/2026-09-13-spec-m5-aws-deployment.md` §12 (seven Answer cells = the decisions) and §3.1/§3.4 (topology, target group)
- `docs/tasks/TASK-m5-aws-deployment.md` — State, Blockers, Next action, resume condition
- `.github/workflows/ci.yml:142-178` — the verify-api docs-only skip, before quoting any green check
- `docs/STATE.md` §3A — four live bullets + dated amendment markers; regenerate whole, verify after edit (`^## ` = 8, §4 cmp-clean)

## 3. Locked Technical Invariants (do not undo)
- D-M5-1…9 as written (checkpoint-001 §5 summaries; spec §2 is authority) — especially: deploy by digest, tag SHA; migrations at boot while `desiredCount = 1`; empty `Cors:AllowedOrigins` is *correct* same-origin; no in-image HEALTHCHECK.
- §12 answers: `ap-southeast-1`; in-account Route 53 zone; CDK; bought /20 + VPC endpoints, **no NATs**; manual `citext` pre-creation; manual rotation **with** forced redeploy; human-run deploy (OIDC only after first deploy).
- The ALB :8080 has exactly one ingress: the ALB SG. RDS stays private. CloudFront's cert lives in us-east-1 regardless of region choice.

## 4. Current State & Immediate Next Step
- **Done**: everything checkpoint-002 §2 lists; last full-suite verdict = CI #82 (210/0, 1 m 12 s).
- **Next single step**: T-02 step 0 needs two owner facts — the **zone apex name** and **`aws` CLI configured with the owner's own credentials** (never in chat; this machine measured `command not found`). Then run in order into `docs/tasks/m5-deploy-log.md`: `list-hosted-zones-by-name` → three `describe-*` region checks → only then request both ACM certs. Commit ladder per AGENTS: measurement before assertion, docs-only skips must be quoted as skips, assert `headRefOid` after every `gh pr create`.

Please inspect the files listed and confirm state matches before editing.
```
