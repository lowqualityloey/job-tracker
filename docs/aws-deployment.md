# AWS Deployment (M5) — Decision & Design Record

- **Status**: Planned (Level 3 — `pk:ship` + human approval required to deploy)
- **Author**: Assistant (`pk:plan`, recommended cookie-safe defaults)
- **Date**: 2026-09-13
- **Basis**: Recommended defaults per AC-16 and locked invariants I-19 (CORS credentials-on, `"*"` refuses boot), I-21/22/23 (auth cookie/CSRF posture). The owner chose "recommended" over an explicit pick.

> This file resolves six prior phantom citations to `docs/aws-deployment.md` that existed only in references, never on disk.

---

## 1. Decisions (recommended)

| ID | Decision | Choice | Rationale |
| :--- | :--- | :--- | :--- |
| **D-M5-1** | Deployment target | **ECS/Fargate** + **Application Load Balancer** (TLS termination) + **RDS Postgres** | Canonical AWS container path; reuses the existing Postgres provider. The `-074` data-protection key ring already persists to `data_protection_keys` in this database, so nothing new is invented. |
| **D-M5-2** | Topology | **Single origin** (one browser eTLD+1 for SPA + API) | Keeps `__Host-JTSession` / `__Host-JTCsrf` **same-site** → no `SameSite=None`, no resurrection of `-073` / `D-3`; AC-12/AC-16 satisfied without cross-origin CORS. |
| **D-M5-3** | Instance count | **Single task** | Preserves the in-process SSE fan-out bus (`ASSUMPTION-m3-backend-api-002`). Scaling >1 requires PG `NOTIFY` (grill Q16) — logged as follow-up, out of scope for first deploy. |
| **D-M5-4** | Key persistence | Keep `-074`'s RDS-backed `data_protection_keys` | Already implemented; reuses `ConnectionStrings__Default`, no new secret. |

### D-M5-2 "how" — single origin via CloudFront (recommended, Option A)

Browser hits one hostname, e.g. `https://app.example.com`, served by **CloudFront**:
- `/api/*` → ALB (HTTPS listener) → ECS task (Kestrel, HTTP internally)
- everything else → **S3** (the built SPA from `dist/`)

**API code is unchanged** — the `if (app.Environment.IsDevelopment())` SpaRoot guard in `Program.cs:195` is left as-is; in Production the API does not serve static files, CloudFront does. From the browser's perspective there is one origin, so the `__Host-*` cookies are same-site and need no CORS.

**Fallback (Option B, low-friction)**: have the API serve the SPA in Production too by flipping the `IsDevelopment()` guard. Removes S3/CloudFront but loses CDN caching and touches auth-serving code. **Not the default**, recorded so the owner can down-select.

---

## 2. Request flow (single origin)

```
Browser ──HTTPS──> CloudFront (app.example.com)
                     ├─ /api/*        ──> ALB (HTTPS) ──> ECS/Fargate (Kestrel, HTTP)
                     └─ /*  (static) ──> S3  (built SPA)
```

Cookies (`__Host-JTSession`, `__Host-JTCsrf`, both `Secure`) are set by the API over the ALB's HTTPS listener and share the SPA's eTLD+1 → **same-site**, sent on every `/api` call, no preflight.

---

## 3. Runtime environment / secret contract (injected — never committed)

| Variable | Source | Required | Notes |
| :--- | :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | task def | yes | `Production` |
| `ConnectionStrings__Default` | AWS Secrets Manager → task env | yes | RDS Postgres; **also backs the `-074` key ring** (`data_protection_keys`) |
| `Cors__AllowedOrigins` | task def | yes (validated, no `*`) | single value = the app origin; CORS effectively unused same-origin, set for safety |
| forwarded headers | task def / code | yes | Enable `UseForwardedHeaders` for the ALB so `Request.Scheme`=https |
| `Kestrel__Certificates__*` | n/a | no | ALB terminates TLS; Kestrel internal HTTP |
| `Web__SpaRoot` | n/a | no | Dev-only; ignored in Prod under Option A |

**Fail-fast (pk:ship pillar 1):** `BootGuard.EnsureSafeToStart` (Program.cs:137) already runs at boot and already rejects `Cors:AllowedOrigins` containing `"*"`. Extend it to also assert `ConnectionStrings:Default` is present and `Cors:AllowedOrigins` is non-empty — crash at startup with a named variable, never mid-transaction.

---

## 4. Database & migrations (Expand-Contract)

- **RDS Postgres** (PostgreSQL 18.6 per M3 spec), single instance, automated backups on.
- **EF Core migrations**, applied by a **release step** `dotnet ef database update` against RDS, OR **migrate-on-boot** (`app.Migrate()` with bounded retry). For single instance, migrate-on-boot keeps the deploy atomic. The `-074` key-ring table is created by the same migration path.
- **Golden rule**: never combine an additive (Expand) and a destructive (Contract) change in one release.

---

## 5. Health & smoke (pk:ship pillar 4)

- **Add `GET /api/health`** (does not exist yet) → `200` + DB ping latency. ALB health check + post-deploy smoke both use it.
- Post-deploy synthetic probe: login → read `GET /api/applications` over the real origin.

---

## 6. Rollback (pk:ship pillar 5)

- **App**: redeploy previous ECS task-definition revision / image tag.
- **DB**: unchanged (Expand-Contract) → no DB rollback under incident.

---

## 7. Follow-ups / out of scope (first deploy)

- Multi-instance scaling → PG `NOTIFY` SSE bus rework (grill Q16).
- Option B (API-serves-SPA) if a CDN is unwanted.
- ACM cert + domain ownership for `app.example.com`.

---

## 8. Open questions for the owner

1. Domain + ACM certificate ownership for the single origin.
2. Confirm Option A (CloudFront+S3+ALB) vs Option B (API-serves-SPA) — recommended is A.
3. Migrate-on-boot vs separate release-step migration job — recommended is migrate-on-boot (single instance).
