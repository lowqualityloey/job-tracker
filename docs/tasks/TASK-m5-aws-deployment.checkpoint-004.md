# Checkpoint Record: M5 — AWS Deployment, checkpoint-004

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment` → [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md)
- **Specifications**: [`docs/specs/2026-09-13-spec-m5-aws-deployment.md`](../specs/2026-09-13-spec-m5-aws-deployment.md) — **1,401 lines**, §0–§14 ·
  [`docs/specs/2026-09-14-spec-m5-t03-network.md`](../specs/2026-09-14-spec-m5-t03-network.md) — **318 lines**, §0–§13 (both `wc -l`, measured
  2026-09-15 02:06 UTC). The parent is the milestone contract; **T-03's spec is now the binding design of record for the network half**, and
  the reason a second spec exists is that §14's amendment changed what T-03 had to design.
- **Milestone / ceremony**: M5, **Level 3 (Release-Critical)**. `pk:ship` and the human merge gate stay in force for anything that touches
  AWS. **Zero AWS calls in this checkpoint pass and zero in the whole session it closes — reads included.**
- **State**: **`handoff_ready`** — a stop state, self-imposed at a session boundary. It prohibits implementation edits, AWS writes, commits
  on other concerns, and task switches until §8's resume condition holds. It is *not* a fatigue checkpoint: it is the first boundary in this
  milestone where **the remaining blocker is neither code nor knowledge but a number only a human can see in a console.**
- **Author of this record**: Assistant (`pk:checkpoint`), 2026-09-15 02:06 UTC
- **Branch / revision**: `main` = `origin/main` = **`18045ad`** (**PR #96's merge**, `MERGED 2026-09-15T02:00:57Z`, read from
  `gh pr view 96` *after* the pull rather than asserted from memory); `git checkout main && git pull --ff-only` → clean fast-forward. This
  record lives on `docs/m5-checkpoint-004`, cut from that commit. **No head SHA of this branch is transcribed anywhere in it or in
  `docs/STATE.md`** (DEBT-25). The durable form of the claim is the invariant: `gh pr view <this-pr> --json headRefOid` equals
  `git rev-parse HEAD`, re-read in the same breath as anything that depends on it.
- **Supersedes**: [`checkpoint-003`](./TASK-m5-aws-deployment.checkpoint-003.md) **as the live pointer.** 003 stays valid as evidence and its
  §7 scope-change table was **fully applied** — verified row by row at this boundary: spec §6's T-02 row is restated (line 856), §12 B's
  Answer cell corrected (line 1204), D-M5-3's TLS consequence made an explicit dated amendment (§14), T-03's verification cell rewritten
  around VPC endpoints rather than NAT, and H-B's risk column now carries the volunteer-infrastructure exposure 003 §7 asked to register
  (line 1298: *"puts this milestone's cookie boundary on infrastructure a volunteer project can rename or revoke"*). **Nothing from 003's
  §7 is pending.**

---

## 1. Objective (unchanged in text, changed in one respect)

Make the application deployable to AWS (CloudFront + S3 SPA, ALB, ECS/Fargate, RDS PostgreSQL, Secrets Manager) while preserving M4's
`__Host-` cookie posture, fail-fast boot, and an unauthenticated `GET /api/health` the ALB target group can trust.

**What changed since 003:** at that boundary the milestone had an *unreconciled contract* (spec and task record describing different T-02s)
and no design. Both are closed. The contract is amended, T-03 exists on paper, and **every owner decision in §14.6 and §12 has been answered
except one.** M5's remaining unknown is no longer "what should we build?" but "can this account pay for what we decided?"

## 2. Completed since checkpoint-003 (`2026-09-14 14:05 UTC`) — six merges, all measured

| Concern | PR → merge | Verified how |
| :--- | :--- | :--- |
| **DEBT-27 closed**: spec §6's T-02 row and §12 B reconciled to the H-A decision | #91 → `9f14671` `14:24:13Z` | `gh pr view` at this boundary |
| **§14 Amendment A1** — the hostname routes priced, `D-M5-3` partly reversed, `I-A1-1`…`I-A1-6` locked, §14.4's FMEA row for the cleartext last hop | #92 → `09b4e0d` `15:14:36Z` | same; the amendment is the reason T-03's design reads the way it does |
| **T-03's design delivered** — address plan, three route tables, four-SG matrix, eight-row endpoint set, CDK shape, synth assertions, `[verify-at-apply]` cost table | #93 → `8a10b69` `18:33:25Z` | same; §11's retraction of a fabricated cost table is inside this range and is the milestone's second-most-instructive artifact |
| Projection syncs after each merge | #94 → `82e4f94` `19:21:41Z` · #96 → `18045ad` `02:00:57Z` | same |
| **All three remaining owner decisions answered, in one session** — **O-1 `after`** (first deploy on H-A, certificate later), **O-2 scoped identity first**, **O-3 VPC flow logs inside T-03** | #95 → `e31b24b` `01:52:26Z` | answers recorded with timestamps in §14.6, T-03 §12, and the task record's dated sections |
| **DEBT-28 opened and closed inside one PR** — O-1's answer created a cleartext window with no expiry, so a P1 row named the gap; the owner then chose **the event, not a date** | #95 | the trigger is written where it can be enforced: parent **T-09's** task row *and* its Prove-it-by column |
| T-03's design **grew** rather than merely being stamped | #95 (O-3) | §6's flow-log row, §8's **T-N9**, §9's item 8, §11.2's cost line — all new in that commit, and `grep -c '^| \*\*T-N'` → **9** assertions |

## 3. Changed files in this checkpoint pass

`docs/STATE.md` (§1 branch field + a dated boundary bullet, §2's active-task and aggregate lines, §3A regenerated **whole for the fourth time
in one day**, §5's O-4 blocker wording, §8's new row) and this record. **No source file, no config, no spec, no AWS resource.** `.m5-parts/`
left exactly as found — see §6.

## 4. Verification evidence at this boundary

| Gate | Result |
| :--- | :--- |
| `date -u` | **2026-09-15 02:05:32 / 02:06:18 UTC** — every stamp in this record re-read against it, none carried over |
| `git rev-parse HEAD` / `origin/main` / `main` (after `checkout main && pull --ff-only`) | all three **`18045ad`**; `git status --porcelain` → **only `.m5-parts/`** |
| `git merge-base --is-ancestor 439f4e5 main` | **YES** — both of this session's commits are on `main`, so DEBT-28's closure is fact, not pending |
| `gh pr view 91..96 --json state,mergedAt,mergeCommit` | six merges, §2's table, all `MERGED` with measured `mergedAt` |
| `gh pr list --state open` | **empty** at 01:57 and at 02:06 UTC — nothing is waiting on a reviewer except this PR |
| `gh pr checks 96` | **`verify` pass 32 s** · `verify-api` pass 7 s · `bundle-baseline-recheck` **skipping** (docs-only head; read `.github/workflows/ci.yml` before quoting that step — an API-green claim is CI-only while DEBT-23 stands) |
| **`npm run verify` on this tree** | **exit 0** — typecheck → `eslint --max-warnings 0` → stylelint → **227 tests / 23 files** → build **61.23 kB** gz. Re-executed after **every** content change since 01:11 UTC; no run has ever been non-zero in this session |
| `docs/STATE.md` structure (after a whole-section §3A rewrite, the operation that has twice deleted 51 lines here) | `grep -c '^## '` → **8**; §4 `cmp`-identical to `main` (**55** lines by the `^## 4\.` → `^## 5\.` range); ⛔ archive `cmp`-identical at **346**; **4** live §3A bullets |
| AWS session | **not claimed live and not needed**: zero control-plane calls, reads included, across the entire session. `I-A1-5` unbroken |
| Dependency hygiene | runtime **3** / dev **19** — unchanged since M0; T-03's §7 still lists every CDK dependency as *arriving with its own written justification*, and none has arrived |
| `vite.config.js` | **absent** after the build the gate just ran (DEBT-11 guard) |
| Secrets / leak scan | staged diff scanned for `AKIA`, `aws_secret`, `BEGIN .*PRIVATE KEY`, `.env` paths → **0 findings**; the chosen apex appears in **no tracked file** (I-A1-2), and no account id, zone name or profile name was introduced |
| Release-evaluation handoff | **N/A** — this repository consumes PromptKit OS; no tag, hosted release, changelog, deploy, or rollback is authorised or implied by this record |

## 5. Decisions and invariants the next session must not undo

1. **`D-M5-1` … `D-M5-9` stand** as summarised in `checkpoint-001` §5, **except `D-M5-3`**, which §14 partly reverses: with H-A,
   **TLS terminates at CloudFront** and CloudFront → ALB is `HTTP:80` pinned to the CloudFront managed prefix list. That is a decision
   change, not a discount, and it is written as one.
2. **`I-A1-1` … `I-A1-6` are in force** (parent §14.5). One of them has **partly discharged**: I-A1-5 forbids M5 writes *"before the three
   console numbers arrive, **and** before this amendment is reviewed and merged."* The amendment merged in #92, so **only the console
   numbers half still binds.** Say the whole rule, not the convenient half.
3. **I-A1-4's open question is closed by O-2**: root performs exactly **one** write — minting the scoped identity (T-03 §10 step 0) — and
   everything after it runs scoped. Serialising `aws.exe` calls is still mandatory; a fanned-out ladder on a shared `login_session` cache
   self-revokes mid-deploy, which is how this milestone lost a session to its own tooling.
4. **The cleartext last hop now has a date-shaped owner obligation, and it is not a date.** DEBT-28's trigger is the **event "first deploy
   standing"** (T-13's exit); it lives in parent **T-09's** row and Prove-it-by column, not only in a debt table. **Naming an event is not
   scheduling one:** if T-13 passes and the flip does not run, the defect is the skip.
5. **The flip is additive and one test moves with it.** `sg-alb` gains a `443/tcp` ingress from the *same* prefix list, one listener, and
   `OriginProtocolPolicy: https-only`; **no CIDR, route table or endpoint row moves**. **T-N5**'s expected ingress set must change at flip
   time. And a direct `https://` probe to the ALB's own DNS name is **expected to fail hostname verification by design** — the certificate is
   for the H-B name — so nobody should read a correct flip as a broken one.
6. **Flow logs are in T-03** (O-3): VPC-level, `ALL`, 10-minute aggregation, **S3** destination, 30-day lifecycle. The destination is argued
   from topology (a private path to S3 already exists via §5's gateway endpoint, so no egress rule is added and CloudWatch ingestion never
   enters the bill), **not** from any price. **One `[verify-at-apply]` fact is deliberately unresolved:** whether an S3 destination needs a
   publishing role at all — settled by attempting the create *without* one (T-03 §9 item 8), never by recalling what the CloudWatch form
   wants.
7. **`UNCERTAINTY-m5-aws-deployment-010-a` and `-010-b` are both still open and both are O-4/T-09 work, not paper work.** 010-a is whether
   promotional credit pays a **$0.50/mo hosted zone**; 010-b is whether a certificate requested in the **workload region** attaches to a
   CloudFront origin — and 010-b *transfers to whoever lands the flip* rather than being deferred away.
8. **The unregistered apex is in no tracked file** (I-A1-2). The repository is PUBLIC; availability does not survive being read.
9. **No new `devDependencies` before T-03** (unchanged: 3 / 19), **no NAT gateway** (§12 D), and **`pk:ship` + the human merge gate remain
   exactly where checkpoint-003 put them.** Zero pushes to `main`, no tags, no releases by an agent.

## 6. Active blockers & open questions

- **The only gate on M5 provisioning (owner, three console reads):** account **plan type**, **promotional credit remaining**, **payment
  methods attached — or definitively none**. No command on this machine can see them. They decide *both* the topology's cost story
  (DEBT-26) and whether O-1's deferred certificate can be bought at all, because the flip's price is the $0.50/mo zone. **Until they arrive,
  `I-A1-5` forbids every M5 write and T-03 stays a document.**
- **M4 `AC-3`** — the owner-side browser walkthrough, open since 2026-09-12; restrike or restate. Nothing unblocks it but a human with a
  terminal.
- **DEBT-21** — `.m5-parts/` (**28 files, 212 KB**, re-measured `find | wc -l` + `du -sh` at this boundary) is untracked, not gitignored,
  and its delete-after trigger passed on 09-14. It is one `rm -r` and a one-line `chore` commit; deliberately not folded into a docs PR,
  and deliberately not deleted inside a checkpoint.
  *(Closed 2026-09-15 04:11 UTC by the pass the owner asked to "do the recommendations": archived to a `/tmp` tarball first, then
  deleted, with `git status --porcelain` → empty. The bullet's stated blocker — that the spec's §12 cites two of the directory's files
  as provenance — was tested first and is **false of the merged spec** (`grep` over `docs/specs/` → no matches), so nothing dangles.
  §5's DEBT-21 row carries the finding, and §8's 04:11 row the evidence.)*
- **DEBT-22** — `docs/aws-deployment.md` still carries five stale rows and is still the cited decision record. **T-15** owns it.
- **DEBT-23** — the local full API suite cannot bootstrap Testcontainers, so *every* PR pays the CI-only-verification tax for the API.
- **Remote branch litter (owner's to delete):** ~~**13**~~ → **3** non-`main` refs as of 04:11 UTC — ten were proved absorbed and deleted; three tips each hold one commit with no patch-equivalent on `main`, so they were kept (`dbbb6ac`, `ed8a305`, `17c366b`; patches in `/tmp`, subjects now recorded in §8 of `docs/STATE.md`). (`git ls-remote --heads origin | grep -v '/main$' | wc -l`), including
  the `docs/m5-t03-network-design` orphan and three M3/M4-era heads. `docs/m5-o1-answer` and `docs/state-m5-answers-merged` were deleted
  with their merges, which is the first time this session's cleanup list got shorter without being re-typed from the previous bullet — worth
  noticing, because §3A's in-flight bullet has now been invalidated by a merge **three times in one day**.
- **A process question this record raises rather than answers:** §3A's "In flight" bullet is *structurally* perishable — it is false the
  moment its own PR merges. Three regenerations in eight hours is not cadence, it is a shape problem. Either the bullet names a *predicate*
  instead of a PR, or a sync commit is treated as part of every merge. The next session should pick one and write it into `AGENTS.md` —
  **not** in this pass, which is documentation-only and already carries a checkpoint's own diff.
  *(Resolved 2026-09-15 02:46 UTC, one question put back to the owner: they chose **predicate-only** — no sync PR per merge, stale values ride the next content PR. Written as* **AGENTS.md rule twelve** *and applied in the same commit, so §1's branch field and* *§3A's in-flight bullet now carry the commands that re-derive them. The merge-carried-sync alternative stays on the table; this one is cheaper and it was the owner's call.)*

## 7. Scope change record — **none pending**

checkpoint-003 §7's five-row table is fully applied (row-by-row verification in the header above), and this session's two scope movements were
**authorised before they were made, not after**: O-3's flow logs entered T-03 only after the owner chose *"now"*, and O-2's step 0 entered §10
only after *"scoped first"*. Both changed the *design text* rather than a status line, which is the distinction the answers were asked to
respect. **No pending scope change exists at this boundary.** The one thing that would need a new Scope Change Record is a decision to change
**the topology itself** — e.g. abandoning H-A for a registered domain before T-13 — and that is the owner's, with `pk:plan` in front of it.

## 8. Exactly one next action, and the resume condition

**Next action — the owner's, and it is three numbers:** read **plan type**, **promotional credit remaining**, and **payment methods attached
or none** in the AWS billing console and reply with them. Do not include an account id.
**Reviewing and merging this checkpoint PR is the other human action, and it is smaller.**

**Then, and only then, the agent's single next executable step is T-03's execution**: parent **`pk:ship`** asks its questions first (the
deploy identity is now scoped by decision but does not yet exist), then §10's step 0 mints the identity, and §9's reads run **serially** in
their written order, with `docs/tasks/m5-deploy-log.md` filling as the evidence file — the same discipline T-01's clean-room re-proof and
T-02's step 0 used.

**Resume condition**: this task moves to active execution when **O-4 arrives** *and* the receiver validates, before editing: the Task ID, the
nine invariants in §5 (numbered 2, 4, 6 are the ones a fresh session most often overwrites), T-03's §12 status line, and the fact that
**§3A's in-flight bullet is stale by construction** and must be re-read rather than trusted. Level 3 gates are unchanged: human merge per PR,
`pk:ship` before any deploy, zero pushes to `main`, no tags.

---

## Handover Prompt (paste into a fresh session)

*Embedded here rather than duplicated into `TASK-m5-aws-deployment.handoff-002.md` on purpose: 003 did the same, and a second file restating
the same next action is the third-copy failure DEBT-12 exists to prevent. If a handoff record is wanted, generate it from this block — do not
maintain it beside it.*

```markdown
# Session Resume: M5 AWS Deployment — T-03 execution, gated on one owner answer (`pk:ship` first, Level 3)

## 1. Context & Environment
- Start from `main` and MEASURE it: `git rev-parse --abbrev-ref HEAD`, `git rev-parse HEAD origin/main`, `git status --porcelain`.
  Read `AGENTS.md` first (**twelve** commit-discipline rules as of 2026-09-15 — re-derive the count with the `awk` one-liner in its own
  header rather than trusting either of us), then `docs/STATE.md` §3A (the live projection) and
  `docs/tasks/TASK-m5-aws-deployment.checkpoint-004.md` (the live pointer; 003 is superseded).
- **Active task**: `TASK-2026-09-13-m5-aws-deployment`, M5, Level 3, state `handoff_ready`.
- **Where M5 actually stands**: T-01 ✅ · T-06 ✅ · T-07 ✅ · T-02 ✅ reconciled (#91/#92) · **T-03 designed and merged (#93), not executed**.
  All owner decisions are answered except **O-4: account plan type, promotional credit remaining, payment methods — three console numbers no
  agent can read**. Until they arrive, `I-A1-5` forbids every AWS write, read-only probes excepted by `pk:ship`.

## 2. Key files to inspect
- `docs/specs/2026-09-14-spec-m5-t03-network.md` — §4 (SG matrix), §5 (endpoint set), §6 (ECR/log group/flow logs), §7 (CDK shape),
  §8 (nine synth assertions T-N1…T-N9), §9 (eight SERIAL verification reads), §10 (apply order, step 0 = mint the scoped identity),
  §11 (cost table: every cell `[verify-at-apply]`, none recalled), §12 (three of four answered)
- `docs/specs/2026-09-13-spec-m5-aws-deployment.md` — §6 task table (T-09 now carries the flip's trigger), §14 (amendment: H-A, I-A1-1…6,
  §14.4's FMEA row, §14.6 both items answered)
- `docs/tasks/TASK-m5-aws-deployment.md` §"O-1 answered", §"O-2 and O-3 answered" · `docs/tasks/m5-deploy-log.md` (redaction rule) ·
  `.github/workflows/ci.yml` before quoting any green API check

## 3. Locked invariants (do not undo)
- `D-M5-3` is **partly reversed**: TLS terminates at CloudFront, CloudFront→ALB is `HTTP:80` restricted to the CloudFront managed prefix
  list. A decision change, not a discount. DEBT-28 closed with the trigger = the **event** "first deploy standing" (T-13's exit), enforced
  from parent **T-09**, so a flip that never happens is a skip to object to, not a debt to re-open.
- Flip shape: additive. `sg-alb` +`443/tcp` from the same prefix list, one listener, `OriginProtocolPolicy: https-only`, **no CIDR/route
  change**, and **T-N5's expected set moves with it**. Direct `https` to the ALB's own name fails hostname verification by design.
- Flow logs are IN T-03: VPC-level, `ALL`, 10 min, **S3** destination (topology argument, never a price), 30-day lifecycle. Whether the S3
  form needs a publishing role is `[verify-at-apply]` — settle it from the create call, do not recall it.
- Root does exactly ONE write (creating the scoped identity; §10 step 0). Every `aws.exe` call is SERIAL — a parallel ladder on a shared
  `login_session` cache self-revokes. `UNCERTAINTY-010-a` (credit vs $0.50 zone) and `-010-b` (cert region vs CloudFront origin) are open
  and are settled by measurement, not argument.
- The unregistered apex is in NO tracked file (repo is PUBLIC). No new `devDependencies` (3 runtime / 19 dev). No NAT gateway.
  API-suite green is a **CI-only** claim while DEBT-23 stands.

## 4. Current state & immediate next step
- **Verified at the last boundary**: `npm run verify` exit 0 (227 tests / 23 files, 61.23 kB gz), CI `verify` pass on #96's head,
  `main` = `origin/main` = `18045ad`, STATE structure intact (`^## ` 8, §4 and archive `cmp`-identical, 4 live bullets), zero AWS calls all
  session, `.m5-parts/` still untracked (DEBT-21).
- **Single next step**: obtain **O-4's three numbers** (owner), then `pk:ship`'s pre-flight questions, then execute T-03 §9/§10 in order with
  `m5-deploy-log.md` as the evidence file. If O-4 has not arrived by the time you finish reading this, do not improvise a cost model and do
  not start T-04 — there is no agent-side task that touches a platform behind T-03.
- **Also worth deciding early** (raised in checkpoint-004 §6, unanswered): §3A's "In flight" bullet is false the moment its own PR merges —
  it was regenerated three times in eight hours. Choose either a predicate wording or a merge-carried sync, and write it into `AGENTS.md`.

Inspect the files above and confirm the state matches before editing anything.
```
