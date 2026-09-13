# M5 AWS Infrastructure Provisioning Plan

- **Status**: Draft (awaiting owner review)
- **Author**: Assistant (`pk:plan`)
- **Date**: 2026-09-13
- **Basis**: `docs/aws-deployment.md` (D-M5-1…4, Option A)
- **Spec**: `docs/specs/2026-09-13-spec-m5-aws-deployment.md`

---

## 1. Architecture (Option A — Single Origin via CloudFront)

```
Browser ──HTTPS──> CloudFront (app.example.com)
                     ├─ /api/*        ──> ALB (HTTPS) ──> ECS/Fargate (Kestrel, HTTP)
                     └─ /*  (static) ──> S3  (built SPA)
```

**Key properties**:
- Single browser origin → `__Host-*` cookies same-site, no CORS preflight
- ALB terminates TLS → Kestrel runs HTTP internally
- S3 serves static SPA assets (cached by CloudFront)
- Single ECS task (preserves in-process SSE bus)

---

## 2. AWS Resources to Provision

### 2.1 S3 (SPA Static Assets)

| Property | Value |
|----------|-------|
| Bucket name | `job-tracker-spa-<account-id>` |
| Region | `ap-southeast-2` (or owner's choice) |
| Block public access | Yes (CloudFront OAC only) |
| Versioning | Enabled (rollback safety) |
| Lifecycle | None (single version retained) |

**Sync command** (CI/release):
```bash
aws s3 sync dist/ s3://job-tracker-spa-<account-id>/ --delete
```

### 2.2 CloudFront Distribution

| Property | Value |
|----------|-------|
| Alternate domain name | `app.example.com` (ACM cert required) |
| Default behavior (`/*`) | S3 origin (SPA bucket) |
| API behavior (`/api/*`) | ALB origin |
| Viewer protocol policy | Redirect HTTP to HTTPS |
| Cache policy (SPA) | CachingOptimized |
| Cache policy (API) | CachingDisabled |
| Origin request policy (API) | AllViewerExceptHostHeader |
| WAF | Optional (not in first deploy) |

**Cache invalidation** (post-deploy):
```bash
aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
```

### 2.3 Application Load Balancer (ALB)

| Property | Value |
|----------|-------|
| Scheme | Internet-facing |
| Listener | HTTPS:443 (ACM cert) |
| Target group | ECS task (port 5000, HTTP) |
| Health check | `GET /api/health` → 200 |
| Security group | Allow 443 from CloudFront IPs |

### 2.4 ECS/Fargate

| Property | Value |
|----------|-------|
| Cluster | `job-tracker` |
| Task definition | `job-tracker-api:latest` |
| Container | `dotnet/sdk:10.0` build → runtime image |
| Port | 5000 (Kestrel HTTP) |
| CPU | 256 (0.25 vCPU) |
| Memory | 512 MB |
| Desired count | 1 (D-M5-3: single instance) |
| Launch type | Fargate |
| VPC | Private subnet(s) |
| Security group | Allow 5000 from ALB SG |

**Task definition environment**:
```json
{
  "environment": [
    { "name": "ASPNETCORE_ENVIRONMENT", "value": "Production" },
    { "name": "Cors__AllowedOrigins", "value": "https://app.example.com" }
  ],
  "secrets": [
    { "name": "ConnectionStrings__Default", "valueFrom": "arn:aws:secretsmanager:..." }
  ]
}
```

### 2.5 RDS Postgres

| Property | Value |
|----------|-------|
| Engine | PostgreSQL 18.6 |
| Instance class | `db.t3.micro` (dev) / `db.t3.small` (prod) |
| Storage | 20 GB (auto-scaling enabled) |
| Multi-AZ | No (single instance, D-M5-3) |
| Backup retention | 7 days |
| Encryption | Enabled (AWS-managed KMS) |
| Security group | Allow 5432 from ECS SG |
| Parameter group | `data_protection_keys` table created by EF migrations |

### 2.6 Secrets Manager

| Secret | Key | Source |
|--------|-----|--------|
| `job-tracker/prod` | `ConnectionStrings__Default` | RDS endpoint + credentials |

**Rotation**: Optional (not in first deploy).

---

## 3. IAM Roles & Policies

| Role | Permissions |
|------|-------------|
| `ecsTaskExecutionRole` | ECR pull, CloudWatch Logs, Secrets Manager read |
| `ecsTaskRole` | Minimal (app-level, no AWS API calls) |

---

## 4. Networking (VPC)

| Resource | CIDR | Notes |
|----------|------|-------|
| VPC | `10.0.0.0/16` | Default or existing |
| Public subnet A | `10.0.1.0/24` | ALB, NAT gateway |
| Public subnet B | `10.0.2.0/24` | ALB (AZ redundancy) |
| Private subnet A | `10.0.10.0/24` | ECS task, RDS |
| Private subnet B | `10.0.11.0/24` | RDS (AZ redundancy) |
| NAT gateway | In public subnet | ECS task outbound internet |

**Forwarded headers config** (`appsettings.Production.json`):
```json
{
  "ForwardedHeaders": {
    "KnownNetworks": ["10.0.0.0/16"]
  }
}
```

---

## 5. Deployment Sequence

1. **Infrastructure (CloudFormation/CDK)**:
   - VPC + subnets + NAT gateway
   - RDS instance + security group
   - S3 bucket + CloudFront distribution
   - ALB + target group + security group
   - ECS cluster + task definition + service
   - Secrets Manager secret (populated manually or via script)
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

## 6. Rollback Procedure

| Component | Rollback action |
|-----------|-----------------|
| ECS task | Redeploy previous task-definition revision |
| SPA | Redeploy previous S3 sync + CloudFront invalidation |
| Database | No rollback (Expand-Contract, D-M5-4) |

---

## 7. Cost Estimate (Monthly, Single Instance)

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

## 8. Open Questions for Owner

1. **AWS region**: `ap-southeast-2` (Sydney) assumed; confirm.
2. **Domain + ACM certificate**: Owner must provision `app.example.com` and request ACM cert in `us-east-1` (CloudFront requirement).
3. **VPC**: Use existing VPC or create new?
4. **IaC tool**: CloudFormation, CDK, or Terraform?
5. **CI/CD**: GitHub Actions deploy workflow, or manual?
6. **Secrets Manager rotation**: Enable automatic rotation for DB credentials?

---

## 9. Deliverables

- [ ] CloudFormation/CDK template (`infra/` directory)
- [ ] `Dockerfile` for API (multi-stage: build + runtime)
- [ ] `appsettings.Production.json` (forwarded headers config)
- [ ] GitHub Actions deploy workflow (optional)
- [ ] `docs/m5-deploy-runbook.md` (operator checklist)
