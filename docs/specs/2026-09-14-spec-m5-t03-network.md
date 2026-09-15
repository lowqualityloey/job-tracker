# T-03 Design — the network that carries M5

**Status: paper only.** No AWS call of any kind, no `cdk` binary, no new dependency, no resource.
**Task:** `TASK-2026-09-13-m5-aws-deployment` §6 **T-03** · **Parent spec:** [`2026-09-13-spec-m5-aws-deployment.md`](./2026-09-13-spec-m5-aws-deployment.md) §3.1, §3.3, §12 C/D, **§14** · **Ceremony: Level 2** inside a Level 3 milestone.
**Executes only when** `I-A1-5` clears (the owner's three console numbers) and §14.6's two decisions are answered. A design document provisions nothing.

---

## 0. What this document is for

T-03 is the first provisioning task after the decisions run out, and it is the one task in M5 whose
mistakes are **expensive to remove later**: a route table and two security groups are the only things
standing between a session cookie and the public internet, and §14 made that true of *two* hops
instead of one. Everything below is therefore written as something a reviewer can say aloud in an
interview — which is AGENTS.md's standard — and every number is either **derivable** (an address
plan, a rule count), **answered** (a §12 decision), or **marked `[verify-at-apply]` with the exact
command** (anything AWS charges or exposes per-region).

Three rules from `§14.5` bind the design directly, and are stated here rather than assumed:

| Invariant | What it forces in this design |
| :--- | :--- |
| **I-A1-1** — the apex is **one config value** | Every hostname-shaped output comes from `apexValue` in one place: CloudFront aliases, the ACM ARNs, `Cors__AllowedOrigins`, the JWT `iss`. T-03 must not mint a second source. Under **H-A** the value is `null`, and *null is a supported state, not an error path* — that is what makes H-C a redeploy instead of a redesign |
| **I-A1-4** — every AWS call is **serial** | No provisioner may fan out. This is a hard constraint on how the CDK deploy is invoked and on any script that wraps it |
| **I-A1-6** — no platform fact without a source and an access date | §5 and §11 cite or mark `[verify-at-apply]`. An earlier revision of this very line claimed §11's prices "came off AWS's own pages on **2026-09-14**" — the primary-source pass (§11.1) found that claim false and this line is its correction at the line: the AWS pages returned **no numbers at all** to this machine, so §11 commands the reads instead of quoting them |

## 1. Constraints inherited, not re-decided

| Source | Constraint | Consequence here |
| :--- | :--- | :--- |
| §12 A | Region **`ap-southeast-1`** | two AZs, chosen: **1a and 1b** (the account's default VPC already uses 1a/1b/1c — measured, §14.1's evidence — so both exist and accept the instance classes §3.6 wants) |
| §12 D | **Bought `/20`, three tiers, no NAT** | §2 and §3 |
| §12 C | **AWS CDK (TypeScript)** | §7, including the dependency justification |
| D-M5-1 | One CloudFront distribution, one public origin, two origins behind it | the ALB is never public-DNS-reachable; nothing in this design points a record at it |
| §14 (H-A) | **No certificate the ALB can present** | the ALB serves **HTTP:80**, and its `:80` ingress is CloudFront-only (§4). Restoring 443 is an H-B/H-C change, and this design is what makes that a small diff |
| §3.3 | **The ALB's SG is the only ingress to `:8080`** | `sg-task` has exactly one ingress rule (§4) |
| D-M5-4 | Migrations at boot, `desiredCount = 1` | no second subnet tier "for scaling"; the tasks' egress need is fixed and small |
| §1.5 / T-04 | RDS private, no public IP | `private-rds` has **no** route to anything but local + endpoints (§3) |

## 2. Address plan

`10.0.0.0/20` — 4,096 addresses, bought because the default VPC makes every subnet public (§12 D's
stated reason: *a slipped RDS SG is an internet-reachable database*). Six `/24`s are allocated;
**four are deliberately left unallocated**, and the reason is the only honest answer available: T-08's
service may need a second AZ pair, and a `cdk`-managed VPC that must be re-planned is a re-planning of
route tables and SG rules under a live service — the thing §6's T-03 row exists to prevent.

| CIDR | Name | AZ | Tier | Usable (≈) | Purpose |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `10.0.0.0/24` | `public-1a` | `ap-southeast-1a` | public | 251 | ALB nodes; CloudFront-facing |
| `10.0.1.0/24` | `public-1b` | `ap-southeast-1b` | public | 251 | ALB nodes — the two-AZ rule the ALB requires |
| `10.0.2.0/24` | `private-task-1a` | `ap-southeast-1a` | private, endpoint egress | 251 | Fargate ENIs (the task, `awsvpc`) |
| `10.0.3.0/24` | `private-task-1b` | `ap-southeast-1b` | private, endpoint egress | 251 | same, second AZ |
| `10.0.4.0/24` | `private-rds-1a` | `ap-southeast-1a` | private, isolated | 251 | RDS primary |
| `10.0.5.0/24` | `private-rds-1b` | `ap-southeast-1b` | private, isolated | 251 | RDS standby-AZ slot (single-AZ today; §12.2 keeps multi-AZ out) |
| `10.0.6.0/23`–`10.0.7.255` | *unallocated* | — | — | ~1,024 | headroom, held for the reason above |

Two design notes worth being able to defend. **RDS gets two subnets even though the instance is
single-AZ**, because T-04's `DBSubnetGroup` is documented as needing subnets in more than one AZ —
**`[verify-at-apply]` at T-04**, since T-03 creates subnets and not the group; the two tiers exist on
the strength of that requirement, not as a resilience claim. And **the task subnets, not the DB
subnets, get the endpoints**, because the only process that dials out is the one holding the image, the
secret and the log handle.

## 3. Route tables — the fail-closed part

| Table | Associates to | Routes | Deliberately absent |
| :--- | :--- | :--- | :--- |
| `rt-public` | `public-1a/1b` | `0.0.0.0/0` → IGW; local | — (the ALB must be internet-facing to receive CloudFront) |
| `rt-task` | `private-task-1a/1b` | local; **prefix-list routes** to the endpoints created in §5 | **`0.0.0.0/0` of any kind.** No IGW, no NAT, no transit |
| `rt-rds` | `private-rds-1a/1b` | local only | `0.0.0.0/0`, endpoint routes, peer access |

**The assertion that matters, and it is testable without an account (§8, T-N1):** *no route in the
stack targets an internet gateway or a NAT gateway from a private table.* That single sentence is the
difference between "private" meaning *unreachable* and meaning *we hoped*. §12 D's whole argument for
dropping the NAT — zero outbound HTTP calls — is only true of the **application**; the **agent**
(§5) is the counter-example, which is exactly why egress is endpoint-shaped rather than route-shaped.

## 4. Security groups, and the rule-quota trap

| SG | Ingress | Egress | Why not the simpler thing |
| :--- | :--- | :--- | :--- |
| `sg-alb` | `80/tcp` from **`pl-com.amazonaws.global.cloudfront.origin-facing`** (IPv4; §5's `[verify-at-apply]` covers whether an IPv6 list is needed for this distribution) | `8080/tcp` → `sg-task` | `0.0.0.0/0` on :80 is the default AWS recommends for a general ALB; here it would expose the cleartext hop §14 just accepted. **Also: the prefix list is not the authentication** — §14.4 says so; the shared origin header + default-`403` rule (T-09) is |
| `sg-task` | `8080/tcp` from **`sg-alb` only** (§3.3's sentence, unchanged) | `443/tcp` → endpoint SGs (ECR API + ECR data, Secrets Manager, Logs, STS); `5432/tcp` → `sg-rds` | Allowing `443` to `0.0.0.0/0` would make the endpoints decorative: traffic would leave the VPC and the "no route to the internet" claim in §3 would already be false |
| `sg-rds` | `5432/tcp` from **`sg-task` only** | *(none beyond local)* | A `10.0.0.0/16` ingress would let the *ALB subnet* reach the database. It cannot — different SG — but writing the CIDR makes the next person's job a widening |
| `sg-endpoint` | `443/tcp` from `sg-task` (and `sg-alb` where the ALB itself needs a call, which §5 resolves) | local | Without an explicit endpoint SG, interface endpoints take the VPC default SG, which is `0.0.0.0/0` egress / no ingress — a silently-reachable port on every ENI in the VPC |

**The quota trap, and it is a real one (I-A1-6):** an AWS-managed prefix list is a **single** SG rule
but carries **weight 55** against the default quota of **60 rules per security group**. `sg-alb`
therefore has ~5 slots left for its lifetime. That is a design constraint, not a footnote: adding a
second prefix list (IPv6) or a handful of CIDR rules to `sg-alb` can fail at `AuthorizeSecurityGroupIngress`
time — **in production, during a deploy**. §8's T-N6 pins the arithmetic as a test, and §10's ordering puts
`sg-alb` changes behind a rule-count check.

## 5. The endpoint set — what "no NAT" actually buys, and what it must include

**Measured premise:** this app makes **zero outbound HTTP calls** (no webhooks, no external fetches).
So the consumers of egress are all AWS-side, and each is a documented PrivateLink service:

| # | Service | Endpoint name | Needed for | Verified by |
| :-- | :--- | :--- | :--- | :--- |
| 1 | ECR **API** | `com.amazonaws.ap-southeast-1.ecr.api` | `BatchGetImage` / authorization token | `describe-vpc-endpoints` + `aws ecr get-login-password` from a task in `private-task-1a` |
| 2 | ECR **data (DKR)** | `com.amazonaws.ap-southeast-1.ecr.dkr` | **pulling the image layers** — separate from (1), and the classic half-configured pair | same; a task that PENDINGs forever with no failure is this row missing |
| 3 | Secrets Manager | `com.amazonaws.ap-southeast-1.secretsmanager` | `GetSecretValue` at task start (§3.7's `valueFrom`) | D-5's boot log reaching `Now listening on` |
| 4 | CloudWatch Logs | `com.amazonaws.ap-southeast-1.logs` | the `awslogs` driver's `CreateLogStream` / `PutLogEvents` | a log stream appears with the task's ENI id in its name |
| 5 | STS | `com.amazonaws.ap-southeast-1.sts` | the task role's `AssumeRole` for credentials | `aws sts get-caller-identity` **from inside the task** |
| 6 | ECS / agent messengers | `…ecs`, `…ecs-agent`, `…ec2messages`, `…ssmmessages` | whether the agent can *register* the task at all with no other egress — **this is the row that decides whether the no-NAT design is sufficient or merely cheap** | §11.2's apply-time reads + the D-5 boot test; **`[verify-at-apply]`, and it is the single most likely first-deploy blocker in T-03** |
| 7 | S3 **gateway** | `aws.ap-southeast-1.s3` (gateway type, route-based, no ENI) | the SPA bucket *if* the deploy pushes to it from a task; **not needed** for CloudFront's S3 origin, which is AWS-side | `describe-prefix-lists` + route table |
| 8 | DynamoDB gateway | — | nothing here uses it | **out**, with the reason: an endpoint you cannot name a consumer for is attack surface with a bill attached |

**Private DNS: on**, per VPC, for the interface endpoints — without it the SDK calls resolve to public
IPs, which §3's routing then cannot deliver, and the failure looks like a timeout rather than a
misconfiguration. **Endpoint policies: `*` at creation, tightened in a separate change.** Stated as a
deliberate trade-off rather than a shortcut: the debug path for a task that never reaches `RUNNING` is
short, and a restrictive endpoint policy is a second, silent failure mode stacked underneath it —
§14.4's own shape. The tightening task lands with T-16, with the log-driver caveat named.

## 6. ECR repository and CloudWatch log group

| Item | Value | Justification |
| :--- | :--- | :--- |
| ECR repo | `job-tracker-api`, **immutable tags**, scan-on-push **off** for v1 | D-M5-5 deploys **by digest**, so mutability has no consumer; scan-on-push is off because T-12 does the build, not the push path, and a scanner that cannot fail a deploy is a report nobody reads — recorded, not silently deferred |
| Log group | `/ecs/job-tracker/api`, `retentionInDays = 30` | §6's T-03 row makes *some* retention the exit criterion. 30 days is chosen because the only forensics this app needs is "why did yesterday's deploy fail", and every extra day is billed storage measured against the ~$75/mo ceiling DEBT-26 put in play |
| Flow logs | **not in T-03** | §14.4 names flow logs as a *detector* for the cleartext-hop risk, so this is a real gap; adding them means an S3 bucket or a second log group and its own ingestion cost, and it is a separate change with its own reason — recorded as **O-3** in §12 rather than folded in |

## 7. CDK shape, and the dependencies it brings

§12 C chose CDK/TypeScript. **This design creates none of it** — the files below are the contract T-03
implements when it is allowed to run.

```text
infra/                       # new top-level: the repo has no infra/ today (§1.1 says so explicitly)
├── bin/app.ts               # one App, three stacks, in dependency order
├── lib/
│   ├── network.ts           # VPC, subnets, route tables, SGs, endpoints  ← T-03
│   ├── observability.ts     # log group + retention                     ← T-03
│   └── data.ts              # RDS subnet group, instance, sg-rds rules  ← T-04
├── cdk.json                 # context: vpcCidr, azs, apexValue, region
├── test/network.test.ts     # §8's assertions
└── README.md                # why each construct is L2 or an escape hatch
```

| Dependency | Version policy | Justification (AGENTS.md: every library is justified where it is configured) |
| :--- | :--- | :--- |
| `aws-cdk-lib` | latest **stable** minor, pinned exactly | the one library that writes the platform; §1.5's Version Selection Policy, no `latest` in a manifest |
| `constructs` | the range `aws-cdk-lib` declares as its peer | peer, not a choice — pinning it by hand is how the synth breaks on an upgrade |
| `typescript`, `ts-node` | **already present** (`devDependencies: 19`) | reused, not added — this is the argument for §12 C's choice |
| `aws-cdk-lib/assertions` | ships with `aws-cdk-lib` | it is the test surface in §8; no extra package |
| `aws-sdk-client-anywhere` | **not added** | the verification in §9 is shell + `aws` CLI, already installed on this machine as a Windows binary (`aws-cli/2.36.44`). A JS SDK in the repo would be a second way to do one thing, and I-A1-4 makes a parallel-fan-out client a liability |

**Stack split, and the deletion test on it:** three stacks rather than one because `network` is the
only one whose failure mode is *retrofit-while-live* (§3/§4), `observability` is disposable, and
`data` (T-04) has a different blast radius entirely. One stack for all of it would mean a broken log
retention value blocks a VPC. Two stacks would not let RDS be destroyed independently of a network the
ALB still needs. Three is the smallest number where each can fail alone.

**`apexValue` is the single context key for I-A1-1** — `null` under H-A today. The escape hatch it
needs: `cloudfront.Distribution` requires **at least one default root object** and its
`additionalAliases` must not receive an empty string, so `null` must be mapped to *absent*, not to
`''`. That is a 2-line check with a test in §8 (T-N7), and it is the whole of what "one config value"
costs.

## 8. Test surface — assertions on synthesised templates, no account required

The repo's testing doctrine is behaviour-first; the honest analogue at this seam is **the template a
synth produces**, because that document *is* the observable behaviour of infrastructure-as-code.
`aws-cdk-lib/assertions`' `Template.fromStack` + `hasResourceProperties` / `resourceCountIs` are the
seam. Eight named cases, and each one is written to fail on a plausible mistake rather than on a
fictional one:

| # | Assertion | The mistake it catches |
| :--- | :--- | :--- |
| **T-N1** | no route in the stack has a target of type `IGW`/`NAT` **from a private route table** (count of such routes = 0) | someone "fixes" a pull failure by adding a NAT — quietly restoring the ~$32 line and the claim §3 denies |
| **T-N2** | VPC CIDR is exactly `10.0.0.0/20`, six `/24` subnets, two per tier | default-VPC reuse, or a `/16` nobody can reason about |
| **T-N3** | `sg-task` has **exactly one** ingress rule: `tcp:8080` sourced from `sg-alb` | the second rule that always appears while debugging T-08 |
| **T-N4** | `sg-rds` ingress is `tcp:5432` from `sg-task` only, and **no** rule sources from the VPC CIDR | the widening §12 D's whole argument was written against |
| **T-N5** | `sg-alb` ingress is `tcp:80` from the CloudFront **prefix list** reference type, not a CIDR | a `0.0.0.0/0` paste, or an ip-ranges.json refresh job nobody maintains |
| **T-N6** | **rule-quota arithmetic:** total weight across `sg-alb` rules ≤ 60, asserted with the 55 counted | the deploy-time `AuthorizeSecurityGroupIngress` failure described in §4 |
| **T-N7** | with `apexValue = null`, the distribution has **no** `Aliases` property at all, and every other `apexValue` consumer resolves to absent | an empty-string alias, which fails at *apply* time, in the region with the least patience |
| **T-N8** | log group exists **with** `RetentionDays` set (absent = infinite retention, i.e. a silently growing bill) | the "provision then configure later" path that never comes back |

**What these cannot prove, stated rather than implied:** a synth test never dials an endpoint, so
§5's row 6 — whether the ECS agent can register a task with no other egress — is **outside** this
suite and inside `[verify-at-apply]`. The suite pins the *shape*; only a task that reaches `RUNNING`
settles the *sufficiency*. Anyone reading §8 as proof of §5 has read the wrong section as the
guarantee, which is a failure mode this repository has already documented twice.

## 9. Prove it by — the commands, serialised (I-A1-4), every one region-qualified

From §6's T-03 row plus the rows this design adds. Region is mandatory on every call: both profiles
have **no `region` key** (measured), so even global endpoints abort without it.

```bash
# 1. six subnets, two AZs, three tiers, and the CIDR is what §2 says
aws ec2 describe-subnets --region ap-southeast-1 \
  --filters "Name=vpc-id,Values=$VPC" --query 'Subnets[].[CidrBlock,AvailabilityZone]'
# 2. the fail-closed claim: private tables carry no IGW/NAT route
aws ec2 describe-route-tables --region ap-southeast-1 --filters "Name=vpc-id,Values=$VPC" \
  --query 'RouteTables[].[AssociationSet.Main, Routes[?GatewayId!=null].GatewayId]'
# 3. endpoints exist, are available, and private DNS is on
aws ec2 describe-vpc-endpoints --region ap-southeast-1 --query \
  'VpcEndpoints[].[ServiceName, State, PrivateDnsEnabled]'
# 4. rule weights actually consumed, per SG (the quota trap, measured not assumed)
aws ec2 describe-security-group-rules --region ap-southeast-1 --filters "Name=group-id,Values=$SG_ALB"
# 5. log group retention
aws logs describe-log-groups --region ap-southeast-1 --log-group-name-prefix /ecs/job-tracker \
  --query 'logGroups[].[logGroupName, retentionInDays]'
# 6. ECR exists with immutable tags
aws ecr describe-repositories --region ap-southeast-1 \
  --query 'repositories[].[repositoryName, imageTagMutability]'
# 7. the only test that settles §5 row 6: a task in private-task-1a reaches RUNNING
aws ecs run-task --cluster $C --task-definition $TD --network-configuration \
  "awsvpcConfiguration={subnets=[ subnet-private-task-1a ],securityGroups=[$SG_TASK],assignPublicIp=DISABLED}"
```

Serial, one after another, by hand or by a script that does not parallelise. A fanned-out ladder on a
shared `login_session` cache self-revokes mid-deploy — that is I-A1-4, measured the hard way.

## 10. Ordering, and how any of it gets undone

1. **`cdk bootstrap` first** — and it creates an S3 bucket plus IAM roles, which collides with
   **§14.6's second decision** (the account has **zero IAM users**; everything here would be
   bootstrapped and deployed as **root**). This is the first step that *cannot* be taken without the
   owner, and it is listed first because it is the one people run on autopilot.
2. `network` stack → 3. `observability` stack → *(T-04 owns `data`)*.
4. **Destroy order is the reverse, and the guard rails are the point:** `cdn remove --force` is
   required before the S3 bucket can empty, a VPC will not delete while an ENI holds an address, and
   **RDS has a `deletionProtection` flag that must be set on creation** because a mistyped `cdk destroy`
   of the `data` stack is the one action in M5 with no undo. Nothing in T-03's own surface destroys
   data; T-03's rollback is `cdk destroy` of two stacks and a check that the subnet group is empty.

## 11. Costs — every figure cited or commanded, none recalled

**The primary-source pass ran at 18:2x UTC on 2026-09-14 and failed in a specific, instructive way; the failure is
this section's actual content.** The two pricing pages this design would quote — `https://aws.amazon.com/vpc/pricing/`
and `https://aws.amazon.com/privatelink/pricing/` — both returned **HTTP 200 to this machine and zero numbers**: the
per-region tables are rendered client-side, and this shell reads server-delivered HTML. Two follow-up reads of the
static `docs.aws.amazon.com` pages hit the session's external-content ceiling. The outcome I-A1-6 permits is therefore
the one recorded below: **no hourly or per-GB figure in this document was read off an AWS page, every price row is
`[verify-at-apply]` with the read that closes it, and nothing below is load-bearing on a number** (§11.2 says why
that costs the design nothing).

### 11.1 The retraction this pass owes

The §0 I-A1-6 row claimed, until this pass, that §11's prices "came off AWS's own pages on 2026-09-14". That sentence
had an access date and no read behind it — the same species as the parent spec's indicted §7 table (a cost figure
wearing a citation it never earned), caught here one day later and in an uncommitted draft, which is the cheapest
possible place to catch it. Corrected at the line itself. The audit note this leaves for the reviewer: **every `[CIT`
marker in this document re-cites a parent §14.5 record** (prefix-list name and weight 55 → `CIT-A1-5`; CloudFront-
provided default-domain certificate → `CIT-A1-2`); none asserts a page this file read.

### 11.2 The cost surface, and the read that closes each row

T-03's billable footprint is small enough to enumerate exhaustively — which is the point of `/20`, no NAT, no scaling:

| Line item | What T-03 creates | Price evidence now | The apply-time read (I-A1-4: serial) |
| :--- | :--- | :--- | :--- |
| VPC, subnets, route tables, SGs | 1 + 6 + 3 + 4 | none recalled — the "VPC is free" sentence is everyone's memory, not a quote | first week's `aws ce get-cost-and-usage --time-unit DAILY --group-by DIMENSIONALIZATION/SERVICE --region ap-southeast-1` → expect no line |
| Interface endpoints | rows 1–5 of §5 (+ row 6 if confirmed) | client-rendered, §11 | the creation console's price note **before** `create-vpc-endpoint`; the service codes and unit figures land in `m5-deploy-log.md` with the timestamp of that read |
| Gateway endpoint (S3) | route-based, no ENI | client-rendered, §11 | same console read; expected zero, which is precisely the kind of "expected" the log exists to falsify |
| CloudWatch Logs | 1 group, 30-day retention | client-rendered, §11 | ingestion + storage per region on `aws.amazon.com/cloudwatch/pricing/`, read in a browser (the page that does render for a human), and a week-1 `aws ce` slice |
| ECR | 1 repo, digest-deployed images | client-rendered, §11 | same family: console pricing section read in a browser, then the week-1 `aws ce` slice |
| The avoided NAT | **0** | parent §12 D's `~$0.01105/hr` and `~$32/mo` both trace to the indicted `m5-infra-plan.md` §7 table (grep-verified this pass) — directionally right, provenance-wise unusable | the two real figures join the other rows in `m5-deploy-log.md`, and the comparison gets its first honest reading there |

**Why no number forms this design.** The endpoint-vs-NAT choice was made on §12 D's *privacy* ground (keep the subnets
private) and re-affirmed as a test that *forbids* the route (T-N1); if endpoint hours cost more than NATs, the
correction is H-B's topology or a grumbled line item, **not** an `0.0.0.0/0` route on `rt-task`. The 30-day retention
(§6) is a forensics window, not a price. O-3's flow logs are risk appetite. So the cost table closes T-03 — it does
not shape it — and that separation is the real result of a failed pricing pass: the only owner-side numbers this
design genuinely needs are DEBT-26's three console reads (O-4), and none of them can change a CIDR.

## 12. Open items handed to the owner

**Status read 2026-09-15 01:05 UTC: O-1 answered (`after`); O-2, O-3 and O-4 open. T-03's execution stays gated by I-A1-5 until all four
clear — the answered row removes one owner decision, not the gate.**

| # | Item | Why it is not the agent's to close |
| :-- | :--- | :--- |
| **O-1** | **H-B before first deploy, or after** (§14.6) | **ANSWERED 2026-09-15 01:05 UTC — `after`.** ~$0.50/mo restores `D-M5-3`'s encrypted origin hop; H-A is $0 and accepts cleartext inside the VPC. A security posture bought with someone else's money. **The residual is a different owner act:** choosing "after" makes parent §14.4's row live by plan, and its stated mitigation is that the state is *temporary* — so the window needs a named expiry trigger. Until one exists this is **DEBT-28**, and O-4 is what tells the owner whether a date or a payment event is the honest form of it |
| **O-2** | **Root vs scoped IAM identity** (§14.6, I-A1-4) | §10's bootstrap is the first action that would be taken as root, in an account with zero IAM users |
| **O-3** | **VPC flow logs: now or T-08** | §14.4 lists them as the detector for the accepted cleartext risk; the cost is real and small; the decision is risk appetite, not engineering. **O-1's answer raises this row's weight:** the risk it detects is no longer hypothetical but scheduled, and the other two detectors in
parent §14.4 (the `403`-rule hit rate, CloudFront's `502` rate) announce attempts and misconfiguration, not interception — flow logs are
the only one that can reconstruct what crossed the hop afterwards, and they cannot be back-filled |
| **O-4** | **The three console numbers** (I-A1-5) | Not readable from this machine by any command that exists. Until they arrive, T-03 stays a document — which is exactly what it is today. **O-1's answer also makes them load-bearing for the security posture**, not only for the bill: the flip that ends §14.4's row costs a $0.50/mo zone, and whether this account can pay it is one of the three numbers |

## 13. Non-goals

No Transit Gateway, no third-party egress, no `desiredCount > 1` (§12.2), no multi-AZ RDS, no WAF, no
automated secret rotation, no second log pipeline. Each is excluded for a *measured* reason in §12.2 of
the parent spec, not because it is big — and T-03's shape deliberately does not preclude any of them
except the NAT, which was argued out rather than skipped.

---

*Written 2026-09-14 as the paper half of T-03; §11 re-filled at 18:30 UTC by the pass that caught §0's uncited
claim. It changes no platform, installs nothing, and executes only when §12's O-1…O-4 are answered — re-read 2026-09-15:
**O-1 is answered (`after`), three remain, and the gate is unchanged.***
