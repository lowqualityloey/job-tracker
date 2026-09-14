# M5 deploy log — T-02 step 0: read-only AWS measurement

**Task:** [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md) · **Date:** 2026-09-14 (UTC) · **Level:** L2 (read-only)
**Writes performed: zero, across both passes.** Every command here is a `get`/`list`/`describe`/`check`. Nothing was created,
changed, or deleted. Pass 1 (§Reads and verdicts) mapped the account; pass 2 (§Second pass) priced the one decision the
first pass proved was missing.

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
| 11 | `rds describe-db-engine-versions --engine postgres` | Majors 12–18 offered; newest minor per major, counted from the full response: **18.6 present** (179 rows match major 18) | ✓ **Exact parity** with `api/docker-compose.yml`'s `postgres:18.6` is available in `ap-southeast-1` — no downgrade decision needed. |
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

## Command-form lessons (each one cost a failed call or a wrong verdict)

- **A filter that is narrower than the question destroys the answer.** Read 11 was first written from
  `grep -oE '"EngineVersion": "1[67][^"]*"'` — a pattern that cannot see major 18 — and the log therefore recorded
  "17.11 newest, parity is a decision". It is not: **`18.6` is offered**, matching local dev exactly. Re-measured from the
  saved response with the majors counted (`12–18`, 179 rows at major 18). This is AGENTS.md's `grep -E "Passed!|error CS"`
  failure with the roles reversed: the pattern was written for what I expected to compare, not for what the API returns.
- `route53domains` **has no `ap-southeast-1` endpoint** — `Could not connect to the endpoint URL`. The whole checklist was
  region-qualified with the workload region; the registrar API is a global service reached through `us-east-1`, like ACM
  for CloudFront. A region flag is not a global constant.
- `aws login` takes `--region` like any other command, and **without it the command stops to ask**, which in a non-TTY
  shell becomes `No Windows console found`. A CLI that "needs a terminal" was really a CLI that needed an argument.

## Second pass (08:12–08:30 UTC) — pricing the missing decision, and one self-inflicted failure

**Zero writes.** Every operation is `list`/`get`/`describe`/`check`. Worth naming one precisely:
`check-domain-availability` is a *lookup*, not a reservation — it holds nothing, blocks nobody, and is not the step that
spends money (`register-domain` is). Pass 1 made 18 reads; this pass made 40, of which **20 produced nothing new and all
20 were my own doing** — the ledger is at the end of this section.

### The finding that changes how T-03 must be run: `aws.exe` is not concurrency-safe on a `login_session` cache

Ten `route53domains list-prices` calls issued in parallel (`&` + `wait`, one per TLD) returned **rc=254 on all ten**:

```text
aws: [ERROR]: An error occurred (ValidationException) when calling the CreateOAuth2Token operation:
The provided authorization grant is invalid, expired, revoked, or malformed
```

The same TLDs, issued **serially** minutes later, returned `rc=0` — and `sts get-caller-identity` was `rc=0` at
**08:20:24Z** and again at **08:21:57Z** throughout, in both regions and both profiles. So the failure was never the
session. It was ten processes sharing one `login_session` refresh path, where the loser of that race abandoned the cached
grant and tried to mint a fresh OAuth2 token non-interactively, which cannot succeed in a shell.

- **The trap in it:** the error names `CreateOAuth2Token`, which reads exactly like "your login expired." I recorded "the
  session is dead" in working notes at 08:14 and retracted it at 08:21 after the serial re-run — the third claim caught
  between writing and measuring in one session, and the reason the retraction is in this file rather than the guess in it.
- **What it costs M5:** any step that fans out AWS calls — a shell loop with `&`, a batch of ECR pushes, an IaC `apply` at
  default parallelism — can revoke its own credentials mid-ladder, at whatever hour the apply happens to be running. Two
  mitigations, in order of preference: **serialise every AWS CLI call**, or **stop refreshing through a browser** by using a
  scoped IAM identity with a long-lived credential. The second is the least-privilege question already on the owner's table
  for T-03, and this finding upgrades it from "good hygiene" to "also the thing that makes the ladder reliable."
- **Not measured:** the bearer token's actual TTL. Minted 07:21:13Z, still valid 08:22:57Z, so **≥ 61 min** with no known
  upper bound. A ladder longer than an hour still needs a renewal plan, and `aws login` remains the only one in this harness.

### Measured cost of a hostname — the number §12 B's `Answer` column needs

`route53domains list-prices --tld X` via `us-east-1`, USD per year, read 08:22Z:

| TLD | Register | Renew | | TLD | Register | Renew |
| :-- | ---: | ---: | :-- | :-- | ---: | ---: |
| `.com` | **16.00** | 16.00 | | `.xyz` | 19.00 | 19.00 |
| `.org` | 16.00 | 16.00 | | `.me` | 31.00 | 31.00 |
| `.net` | 17.00 | 17.00 | | `.co` | 38.00 | 38.00 |
| `.dev` | **17.00** | 17.00 | | `.io` | **71.00** | 71.00 |
| `.app` | 20.00 | 20.00 | | `.click` | 3.00 | 3.00 |

- **`register` == `renew` for every TLD measured.** There is no first-year hook, so the recurring number *is* the number —
  and the spec's `~$12/yr` assumption is **$16 for `.com`, $17 for `.dev`**, i.e. 33–42% above what §12 B priced it at.
  Corrected in the task record rather than quietly re-quoted.
- **`.io` — the reflex choice for a developer project — costs 4× `.dev`.** `.click` at $3 is the floor if the name matters
  more than the suffix.
- **Credits cannot pay for registration.** Quoted from the pricing page: *"You may not use Promotional Credit for any fees
  or charges for Route 53 domain name registration."* Credits will pay for the zone, the compute and the traffic; the
  domain is the one line in M5 that reaches a real card. This is the specific reason a blanket "do what you recommend"
  cannot, on its own, complete this step.
- **Hosted zone: `$0.50`/month, charged at creation and again on the first of each month, not prorated** — but *"a hosted
  zone that is deleted within 12 hours of creation is not charged."* So a throwaway experiment is free and a surviving one
  is $6/yr.
- Default account ceiling is **20 domain registrations**. Irrelevant at one domain; relevant the day M5 grows a staging apex.
- **Availability, measured and deliberately not written here:** `get-domain-suggestions` returned 8 candidates all
  `AVAILABLE`, and four `check-domain-availability` calls showed two obvious-form names **UNAVAILABLE** and two shorter
  personal-form `.dev` names **AVAILABLE**. The names stay out of this file under the redaction rule at the top; they are in
  the session record, and they become a deliberate literal only once the owner chooses one.

### Command-form lessons, pass 2 — three of mine, each avoidable for free

- **`--generate-cli-skeleton input` is a local command.** It prints the exact parameter shape and touches no API. Two of my
  three usage errors would not have happened: `get-domain-suggestions` wants `--only-available` as a *flag* (not
  `--only-available true`) and `--suggestion-count` ≤ 8, and it rejects a bare keyword — `--domain-name jobtracker` fails
  `InvalidInput: Give domain name must contain more than 1 label`, because the parameter takes a **full domain** and
  suggests variants of it.
- **The availability operation is `check-domain-availability`, not `check-domain`.** `aws route53domains check-domain`
  raises `ParamValidation: found invalid choice` at parse time — rc=252, zero network calls, which is why four wasted
  attempts cost nothing at all. Learn to separate **252 (my command was malformed) from 254 (AWS rejected a valid request)
  from 0-but-empty (my filter was wrong)**: three different failures, three different fixes, and only the last one is
  invisible unless you print the raw payload.
- **`AssetDetails` is the shape of `aws pricing get`, not of `route53domains list-prices`.** My first extraction returned
  empty for all eight TLDs and I very nearly logged "Route 53 publishes no prices". The real shape is
  `{"Prices":[{"Name","RegistrationPrice","TransferPrice","RenewalPrice","ChangeOwnershipPrice","RestorationPrice"}]}`,
  each `{Price, Currency}`. Same failure class as read 11 in pass 1 — that one was a regex narrower than the answer, this
  one was a JSONPath borrowed from a different service — and the fix is identical both times: **`jq keys` or `cat` the raw
  response before extracting from it.** The second time in one session that a filter, not the cloud, was what hid the
  evidence — and the difference is that this time the command *succeeded*, so nothing signalled it.
- **`cut -c1-100` destroyed an identifier, and I completed it from memory.** The checks table truncated the job id to
  `.../job/10391594`; I then fetched logs for `10391594976`, a number whose tail I had *invented*. It errored, a `||`
  fallback saved the call, and the tell was a suspiciously small byte count. Rule: when a field is truncated, re-print it
  untruncated (`--json jobs --jq '.jobs[] | .databaseId'`) — never widen a value that a `cut` narrowed. **The second
  time today, after the jq path, that a filter of mine produced a plausible-looking identifier out of nothing.**
- **A wider grep manufactured a failure out of a passing test's name.** Adding `|cannot` to the failure pattern produced
  `fail=1`, and the hit was `[stderr] applicationFormValidation.test.tsx > application form — rejection > cannot be talked
  into storing an impossible date by the native control` — **a test title, on stderr, from a green run.** The `stderr`
  prefix is React's `act()` warning plumbing, not a failure channel. Narrow pattern: hides the evidence (read 11). Wide
  pattern: invents evidence (this one). Both errors are mine, both in the same hour, and the only defense in either case
  is reading the matched line whole.

### CI verdict for this branch, read whole rather than filtered

Run `34825303606`, job `103915942673` (`verify`), completed `08:56:49Z → 08:57:29Z` at head `764d82b`:
**Test Files 23 passed (23) · Tests 227 passed (227) · Duration 9.00 s**, build chunks `0.33 / 1.56 / 61.23 kB` gzipped,
`tsc -b`, `eslint`, `stylelint`, `vitest run` and `vite build` all present in the step stream, the DEBT-11 config-shadow
assertion executed, and **0** lines matching `failed|FAIL|✖|error TS|npm ERR` after stripping ANSI properly
(`tr -d '\033'` — `sed 's/\u001b…//'` silently does nothing, because GNU sed has no `\u` escape; that dead end cost two
re-reads). `verify-api` passed in **7 s**, which is the docs-only skip, not a .NET run.

*(Recorded here rather than in the AWS section above because the harness differs and the root cause does not: three of the
five misreads in this session were my own filters, and one of them was on this repository's CI log rather than on JSON.)*

### Call ledger for this pass, so the count is auditable rather than remembered

| Group | API calls | rc | Informative |
| :--- | ---: | :--- | ---: |
| `list-prices`, ten TLDs in parallel | 10 | all 254 | 1 — the concurrency finding |
| `sts` ×2 regions, `acm`, `route53`, `route53domains list-domains` re-confirmation | 5 | 0 | 5 (2 of them re-confirmations) |
| `list-prices` serial, wrong jq path | 8 | 0 | 0 |
| `list-prices` raw dump + serial with correct extraction | 10 | 0 | 10 |
| `get-domain-suggestions` (+2 parse failures, 2 `InvalidInput`, 1 ok) | 3 | mixed | 1 |
| `check-domain-availability` (+1 parse-failure group, 4 ok) | 4 | 0 | 4 |
| **Total** | **40** | | **21** |

**Cumulative for T-02 step 0: 58 read-only API calls, 0 write calls, 2 account states unchanged.** Twenty of the forty
calls in this pass bought nothing, all twenty traceable to three mistakes of mine, recorded at the ratio they happened
rather than at the ratio that flatters the pass.

## Third pass (10:04 UTC) — the owner has no card: re-probe, public-doc reads, **zero writes**

| # | command | region | verdict |
| :-- | :------ | :----- | :------ |
| 59 | `"$AWS" --profile loey --region us-east-1 sts get-caller-identity` | us-east-1 | `error: INVALID_REQUEST` — the
  SSO session expired between passes. **Re-probed before deciding anything, and the answer was recorded as an error, not
  as an assumption**: no command in this pass could have reached a write API, and none tried |

**Non-AWS reads (public sources, no credentials).** `aws.amazon.com/free` → free plan “**up to $200 in credits**”,
“over 90 services for **up to 6 months**”, “**no charges and no surprise overages**”, and “Always free …
if you go beyond these limits or access paid features, **credits are automatically applied to cover the costs**”; the
product grid names EC2, S3, RDS, DynamoDB, Aurora Serverless, SageMaker and Bedrock as “Available on both plans” and
Bedrock AgentCore as “Paid plan exclusive”. **ECS, Elastic Load Balancing, CloudFront and Route 53 are not named**,
so their credit eligibility stays unverified. `raw.githubusercontent.com/is-a-dev/register/main/dnsconfig.js` → the
record types a free `is-a.dev` subdomain can carry (`A AAAA CAA CNAME DS MX NS SRV TLSA TXT URL`) and `proxyState = data.proxied ? CF_PROXY_ON : CF_PROXY_OFF` — unproxied unless asked, which is what makes an H-B style nested `NS`
delegation resolvable in public. In-repo: `docs/m5-infra-plan.md:200-213`, the ~$75/mo table and its own skip-the-NAT
footnote.

**Dead ends, logged because a failed read is evidence about the source, not the conclusion.** Four attempts on
`docs.aws.amazon.com/awsaccount/latest/billing/*.html` returned **404** (the guide’s URL space has moved), `eu.org`
served an **expired TLS certificate**, and `help.eu.org` / `doc.eu.org` refused to connect. Stopped guessing URLs after
the third 404 rather than manufacturing a citation for the card question — **“does the Free plan need a card” is
therefore reported as unknown**, and the gate moved to three console reads instead.

**Mistakes made and caught in this pass.**
1. **An assertion caught my own defect before it reached a file.** The §8 row was formatted as
   `'| %s | %s | %s | %s'` — missing the trailing pipe every sibling row carries — so `row.count('|') == 5` failed and
   the script died without writing. Without that assert the ragged row would have joined the table’s existing
   `{4:1, 5:62, 6:1}` distribution and looked perfectly green in a `git diff`, because a markdown table renders badly
   rather than loudly.
2. **A redaction check that now cries wolf.** `grep -cE '[0-9]{12}'` reports four hits across `STATE.md` and this file,
   and all four are **GitHub Actions job ids** (`1039…`), not account ids. The “twelve consecutive digits = possible
   account id” rule predates this repo quoting CI ids; it needs a context predicate, because an alarm that always
   fires is an alarm that gets ignored — the same family as a `grep` pattern that hides the one warning that matters.
3. Restated rather than re-earned: no backticks in `git commit -m` (bash eats them), and multi-line bodies edited
   through files, never through shell strings.

**Lifetime AWS totals after this pass: 59 read-only attempts (one here, which errored), 0 writes, 0 dollars.
Registrant contact data never requested, and the chosen apex never written to any tracked file.**

