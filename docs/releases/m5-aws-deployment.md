# Production Release Checklist: M5 — AWS Deployment

<!-- <a id="RELEASE-m5-aws-deployment"></a> -->

> **Status**: `pending` — awaiting owner review of PR #80 (M5 infra plan + Dockerfile) and provisioning decisions.

- **Release ID [Required]**: `RELEASE-m5-aws-deployment`
- **Canonical Release Record Path [Required]**: `docs/releases/m5-aws-deployment.md`
- **Release Linkage State [Required]**: `pending`
- **CI Triage Link [Required when a CI failure exists]**: `N/A - no CI failure`
- **Verification Link [Required]**: `N/A - infra plan, not code release`
- **Verified Result [Required]**: `Pending`
- **Resume Condition [Required]**: Owner reviews PR #80, ratifies D-M5-1…4 decisions, provisions domain + ACM cert, selects IaC tool

- **Release Version / Tag**: `v0.3.0` (planned, after infra implemented)
- **Deploy Lead**: Owner (human approval required)
- **Target Environment**: Production (AWS)
- **Release Date**: TBD (after infra implementation)
- **Commit SHA**: `09bf9b5` (PR #80 head)

---

## 1. Pre-Flight Verification

- [x] All code merged to `main` with approved PR review (PRs #77/#78 merged)
- [x] All automated tests passing in CI (`npm run verify` → 227 tests, build 1.59s)
- [x] Build succeeds with zero bundle size alerts (61.23 KB gzipped)
- [ ] Git tag created and pushed (pending infra implementation)

---

## 2. Environment Variables & Secrets Audit

| Variable Name | Required Scope | Verified in Prod Dashboard | Validated via `BootGuard` |
| :--- | :--- | :---: | :---: |
| `ASPNETCORE_ENVIRONMENT` | Task def | [ ] | [ ] |
| `ConnectionStrings__Default` | Secrets Manager | [ ] | [x] (fail-fast at boot) |
| `Cors__AllowedOrigins` | Task def | [ ] | [x] (no `*` allowed) |
| `ForwardedHeaders:KnownNetworks` | Task def / config | [ ] | [ ] |

---

## 3. Database Migration Sequencing

- **Migration Present**: No (existing schema, no new migrations)
- **Migration Type**: None (Expand-Contract; `-074` key ring already in RDS)

### Sequencing Execution Plan
1. [x] **Step 1**: RDS instance provisioned with `data_protection_keys` table (created by existing EF migrations)
2. [ ] **Step 2**: ECS task boots and runs `db.Database.Migrate()` (migrate-on-boot, `Program.cs:139-152`)
3. [ ] **Step 3**: Verify `GET /api/health` returns 200 with DB ping latency

---

## 4. Post-Deployment Smoke Testing

| Probe Target | Verification Command / URL | Expected Output | Actual Result |
| :--- | :--- | :--- | :--- |
| **System Health** | `GET /api/health` | `HTTP 200 { "status": "ok", "dbPing": "<latency>" }` | Pending |
| **Same-Origin Auth** | Browser login → `GET /api/applications` | 200, `__Host-JTSession` cookie same-site, no CORS preflight | Pending |
| **Fail-Fast** | Remove `ConnectionStrings__Default` → restart task | Boot crash with named error | Pending |
| **Rollback** | Redeploy previous task-def revision | App recovers, no DB change | Pending |
| **No Cross-Origin** | `grep SameSite=None` on build | No matches (verified) | Pending |
| **Post-Deploy Smoke** | Synthetic login → read | Flow passes over real HTTPS origin | Pending |

---

## 5. Post-Release Observation Window (15 Minutes)

- [ ] CloudWatch Logs inspected: zero unhandled exception spikes
- [ ] ALB target health: 1/1 healthy
- [ ] RDS connection pool utilization healthy (< 60%)
- [ ] CloudFront cache hit ratio > 80% (SPA assets)

---

## 6. Rollback Runbook (In Event of Incident)

- **Application Rollback Command / Action**:
  ```bash
  aws ecs update-service --cluster job-tracker --service api --task-definition job-tracker-api:<previous-revision>
  ```
- **Database Action**:
  - Schema is in Expand phase and fully backwards-compatible; no database rollback required
  - Data-protection key ring persists in RDS (`data_protection_keys` table)
- **SPA Rollback**:
  ```bash
  aws s3 sync s3://job-tracker-spa-<account-id>/ dist/ --delete  # Restore previous version
  aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
  ```
- **Sign-off**:
  - Release Status: [Successful | Rolled Back | Investigating]
  - Notes: [Any follow-up items or technical debt to track]

---

## 7. Owner Decisions Required

| Decision | Status | Notes |
|----------|--------|-------|
| D-M5-1: ECS/Fargate + ALB + RDS | Recommended | Owner ratifies by merging PR #80 |
| D-M5-2: Option A (CloudFront + S3 + ALB) | Recommended | Single-origin preserves `__Host-*` cookies |
| D-M5-3: Single ECS task | Recommended | Preserves in-process SSE bus |
| D-M5-4: RDS-backed key ring | Recommended | Already implemented (`-074`) |
| Domain + ACM certificate | **Owner action** | `app.example.com` + cert in `us-east-1` |
| IaC tool | **Owner choice** | CloudFormation / CDK / Terraform |
| VPC | **Owner choice** | Existing or new |
| CI/CD workflow | **Owner choice** | GitHub Actions or manual |

---

## 8. Infrastructure Provisioning Checklist

- [ ] **S3 bucket**: `job-tracker-spa-<account-id>` (versioning enabled, public access blocked)
- [ ] **CloudFront distribution**: `app.example.com` → S3 (SPA) + ALB (API)
- [ ] **ALB**: HTTPS listener, target group (port 5000, HTTP), health check on `/api/health`
- [ ] **ECS cluster**: `job-tracker`
- [ ] **ECS task definition**: `job-tracker-api:latest` (256 CPU, 512 MB, port 5000)
- [ ] **ECS service**: Desired count 1, Fargate, private subnet
- [ ] **RDS instance**: PostgreSQL 18.6, db.t3.micro, 7-day backups, encryption enabled
- [ ] **Secrets Manager**: `job-tracker/prod` with `ConnectionStrings__Default`
- [ ] **VPC**: Public/private subnets, NAT gateway, security groups
- [ ] **IAM roles**: `ecsTaskExecutionRole` (ECR, CloudWatch, Secrets Manager), `ecsTaskRole` (minimal)
- [ ] **ACM certificate**: `app.example.com` in `us-east-1` (CloudFront requirement)

---

## 9. Deployment Sequence

1. **Infrastructure** (CloudFormation/CDK):
   - VPC + subnets + NAT gateway
   - RDS instance + security group
   - S3 bucket + CloudFront distribution
   - ALB + target group + security group
   - ECS cluster + task definition + service
   - Secrets Manager secret (populated manually)
   - IAM roles

2. **Application deploy**:
   ```bash
   # Build and push Docker image
   docker build -t job-tracker-api ./api
   docker tag job-tracker-api:latest <ecr-repo>:latest
   docker push <ecr-repo>:latest
   
   # Update ECS service
   aws ecs update-service --cluster job-tracker --service api --force-new-deployment
   
   # Sync SPA to S3
   npm run build
   aws s3 sync dist/ s3://job-tracker-spa-<account-id>/ --delete
   
   # Invalidate CloudFront cache
   aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
   ```

3. **Post-deploy verification** (Scenarios 1–6 from spec):
   - `GET /api/health` → 200
   - Browser login → read `GET /api/applications` (same-origin, no CORS)
   - Inspect cookies: no `SameSite=None`

---

## 10. Cost Estimate (Monthly, Single Instance)

| Resource | Estimated cost |
|----------|----------------|
| ECS Fargate (256 CPU, 512 MB, 1 task) | ~$12 |
| RDS db.t3.micro | ~$13 |
| ALB | ~$16 + data |
| CloudFront | ~$1 (1 TB free tier) |
| S3 | ~$0.50 |
| Secrets Manager | ~$0.40 |
| NAT gateway | ~$32 + data |
| **Total** | **~$75/month** |

*Dev/staging can use `db.t3.micro` and skip NAT gateway (use public subnets for ECS) to reduce cost.*

---

## 11. Open Questions for Owner

1. **AWS region**: `ap-southeast-2` (Sydney) assumed; confirm.
2. **Domain + ACM certificate**: Owner must provision `app.example.com` and request ACM cert in `us-east-1`.
3. **VPC**: Use existing VPC or create new?
4. **IaC tool**: CloudFormation, CDK, or Terraform?
5. **CI/CD**: GitHub Actions deploy workflow, or manual?
6. **Secrets Manager rotation**: Enable automatic rotation for DB credentials?
