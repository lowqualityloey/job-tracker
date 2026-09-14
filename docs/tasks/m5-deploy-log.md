# M5 deploy log — T-02 step 0: read-only AWS measurement

**Task:** [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md) · **Date:** 2026-09-14 (UTC) · **Level:** L2 (read-only)
**Writes performed: zero.** Every command below is a `get`/`list`/`describe`. Nothing was created, changed, or deleted.

## Redaction rule (why the identifiers look like this)

`gh repo view --json visibility` → **PUBLIC** (measured 2026-09-14). Account IDs, hosted-zone IDs, VPC IDs and any
domain name are therefore recorded as `<placeholder>` in this file, per the rule written into the task record at
T-02 step 0. Region names, AZ names, engine versions and error codes are **not** redacted: they are AWS platform
facts, not account facts, and they are what a future reader needs.

## Harness

```text
AWS="/mnt/c/Program Files/Amazon/AWSCLIV2/aws.exe"     # aws-cli/2.36.44, Windows binary, WSL interop
"$AWS" --profile loey --region ap-southeast-1 <service> <operation> </dev/null
```

Every call carries an explicit `--region`: the `loey` profile's config section holds only `login_session`, no
`region` key. Stdin is redirected from `/dev/null` so a command that wants a prompt fails loudly instead of
holding the session open. Each call is wrapped in `timeout 20|25`.

## Session establishment (one correction to the plan)

| Step | Command | Outcome |
| :--- | :--- | :--- |
| 1 | `aws login --profile loey` (started from this shell) | **Failed** — printed a region prompt, then `No Windows console found. Are you running cmd.exe?` The `login` command needs a TTY *only because* it wanted a region. |
| 2 | `aws login --profile loey --region ap-southeast-1` | **Worked** — `Attempting to open your default browser.` The owner completed the browser sign-in; no terminal prompt was needed. |
| 3 | `sts get-caller-identity` | **rc=0 at 07:21:13Z** — session live. |

The task record had renewal as an owner-terminal-only action. **Partly false**: the agent can *initiate* the flow
from this shell; the owner still performs the authentication itself. The human half is the click and the
credentials, not the process launch.

## Reads and verdicts

| # | Read | Result | Verdict |
| :-- | :--- | :--- | :--- |
| 1 | `sts get-caller-identity` (loey) | `Arn: arn:aws:iam::<ACCOUNT>:root` | ⚠️ **Live, but as account root** — not an IAM role or user. |
| 2 | `sts get-caller-identity` (jonell) | Same account ID (compared by SHA-256 of the digits, not by eye) | ⚠️ The two profiles are **two console logins into one account**, both root. |
| 3 | `route53 list-hosted-zones` (loey) | `"HostedZones": []` | ❌ **No hosted zone exists** in this account. |
| 4 | `route53 list-hosted-zones` (jonell) | `"HostedZones": []` | ❌ Same — rules out "the zone is in the other profile". |
| 5 | `route53domains list-domains` (loey, `us-east-1`) | `"Domains": []` | ❌ **No domain is registered in this account either.** |
| 6 | `acm list-certificates` (`us-east-1`) | `"CertificateSummaryList": []` | ✓ Empty — no cert to reuse; T-02's request will be the first. |
| 7 | `acm list-certificates` (`ap-southeast-1`) | `"CertificateSummaryList": []` | ✓ Empty, as above. |
| 8 | `ec2 describe-availability-zones --all` | `1a, 1b, 1c` available (+2 wavelength zones: `1-han-1a`, `1-mnl-1a`) | ✓ **≥3 normal AZs** — the ALB two-AZ requirement is satisfiable. |
| 9 | `ec2 describe-vpcs` | **One** VPC, `172.31.0.0/16`, `IsDefault: True` | ✓ Default VPC only; no custom VPC exists. |
| 10 | `ec2 describe-subnets` | 3 subnets, one per AZ (1a/1b/1c), all `MapPublicIpOnLaunch: True` | ✓ Default-VPC shape, public subnets in three AZs. |
| 11 | `rds describe-db-engine-versions --engine postgres` | 16.9 → **16.15**, 17.5 → **17.11** available | ✓ Version parity is achievable; pinning it is still D-M5's call. |
| 12 | `ecr describe-repositories` (`ap-southeast-1` **and** `us-east-1`) | `"repositories": []` | ❌ **No ECR repository exists.** §12 F is therefore *create*, not *reuse*. |
| 13 | `s3api list-buckets` | `"Buckets": []` | ✓ Empty. |
| 14 | `cloudfront list-distributions` | 0 items | ✓ Empty. |
| 15 | `ecs list-clusters` | `"clusterArns": []` | ✓ Empty. |
| 16 | `secretsmanager list-secrets` | `"SecretList": []` | ✓ Empty — D-M5-6's secret does not exist yet. |
| 17 | `cloudformation list-stacks` | `"StackSummaries": []` | ✓ Empty. |
| 18 | `iam list-roles` / `iam list-users` | Roles: `AWSServiceRoleForSupport`, `AWSServiceRoleForTrustedAdvisor`. Users: **none.** | ⚠️ No deploy identity exists; see finding 1. |

## What this account actually is

**Greenfield.** Eighteen reads across two regions returned nothing but AWS's own defaults: the default VPC, three
public subnets, two service-linked roles. No zone, no registered domain, no certificate, no registry, no bucket, no
distribution, no cluster, no secret, no stack.

That is the AWS-side confirmation of the row spec §"stale claims" already carried about the *repo*: **"ACM/DNS/ECR/S3/ALB/IAM
stacks already created" was owner context that never existed as a commit.** It also never existed as infrastructure.

## Deviations from spec §12 (each one needs the owner, not a workaround)

1. **§12 B is falsified.** The recorded answer — "a Route 53 zone already in this account" — is not true: reads 3, 4
   and 5 are empty in both profiles. DNS validation for T-02 has **no destination**. Either the domain lives in a third
   account or at a non-AWS registrar, or it must be registered, or M5's hostname plan changes. This does not block the
   certificate *request*; it blocks validation, which is the thing that makes a certificate usable.
2. **§12 F narrows from "registry" to "create the registry".** Read 12 shows no repository, so `job-tracker-api` must be
   created before any image can be pushed. Not a decision — a consequence, recorded so T-03's estimate is honest.
3. **New, unspecified: the deploy identity is account root.** Reads 1, 2 and 18 agree. Every write M5 makes would be made
   *as root*. D-M5-8 says deploys are human-run; it did not anticipate that the human's CLI resolves to root. Least
   privilege is a decision with a cost and a learning trade-off, so it is the owner's, not a default.

## Not yet done (T-02's remaining exit criteria)

Certificate requests (`us-east-1` for CloudFront, `ap-southeast-1` for the ALB), DNS validation, and the ACM→CloudFront
attachment. All three are **writes**, and deviation 1 means the validation route is currently unknown. Blocked behind
`pk:ship` and an explicit owner answer.

## Two command-form lessons (both cost a failed call)

- `route53domains` **has no `ap-southeast-1` endpoint** — `Could not connect to the endpoint URL`. The whole checklist was
  region-qualified with the workload region; the registrar API is a global service reached through `us-east-1`, like ACM
  for CloudFront. A region flag is not a global constant.
- `aws login` takes `--region` like any other command, and **without it the command stops to ask**, which in a non-TTY
  shell becomes `No Windows console found`. A CLI that "needs a terminal" was really a CLI that needed an argument.


