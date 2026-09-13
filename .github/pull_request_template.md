# Pull Request Template

## Description
<!-- Provide a clear, concise summary of the problem this PR solves and why the change was made. -->

### Related Issues & Specs
- **Closes / Fixes**: #
- **Technical RFC / Spec**: `docs/specs/`
- **Architectural Decision Record (ADR)**: `docs/adrs/`

## Execution-Control Traceability (Optional)

Use this section for Controlled Work. Enter `N/A` for Trivial Work or for a consumer repository that does not adopt the optional protocol.

- **Task ID / Canonical Task Record**: `[TASK-YYYY-MM-DD-slug]` / `docs/tasks/<task-id>.md`
- **Specification / Execution Scope**: `[specification path]` / `[bounded files, artifacts, or behaviors]`
- **Current Execution State / Active Ownership**: `[awaiting_review / completed]` / `[owner and active pointer]`
- **Acceptance Criteria Results**: `[AC-* results and evidence]`
- **Changed-File Summary**: `[files and concise summaries]`
- **Branch / Exact Revision**: `[branch] @ [revision]`
- **Checkpoint / Handoff Records**: `[links or N/A]`
- **Scope Changes / Exceptions**: `[links or N/A]`
- **Verification / CI Evidence**: `[commands, results, and links]`
- **Blockers / Receiver Validation**: `[blockers and resume condition; receiver/revision validation or N/A]`

> A passing execution-control validator or CI job supports traceability only. It does not approve opening, merging, shipping, deploying, or rolling back this PR. Human review and the owning workflow remain authoritative.

---

## Summary of Changes
<!-- High-level overview of what was implemented, grouped by architectural layer. -->
- **Data Layer**:
- **Backend / API**:
- **Frontend / UI**:
- **Tooling / Config**:

---

## Acceptance Criteria Checklist
<!-- List all Gherkin acceptance criteria scenarios verified in this PR. -->
- [ ] **AC-1**: [Scenario: Given ... When ... Then ...]
- [ ] **AC-2**: [Scenario: Given ... When ... Then ...]

---

## Database & Data Safety Checklist
<!-- If this PR touches database schemas, migrations, or data models, complete this section. -->
- [ ] **No Database Changes**: This PR does not touch schemas, tables, or queries.
- [ ] **Expand-Contract Pattern**: All schema alterations are additive (non-breaking for rolling deploys).
- [ ] **No Destructive Drops**: No unauthorized `DROP TABLE`, `DROP COLUMN`, or `TRUNCATE` operations in this phase.
- [ ] **Row-Level Security (RLS)**: Policies verified for tenant/organization boundary enforcement.
- [ ] **Query Efficiency**: Composite indexes added for high-cardinality filters and sorted queries.

---

## Testing & Verification Evidence
<!-- Provide proof that the changes have been rigorously verified. -->

### Automated Tests
- **Test Runner Command**: `pnpm test`
- **Results**: `[X] passing, 0 failing`
- **Coverage Added**:
  - Unit tests for: `[functions/modules]`
  - Integration/E2E tests for: `[endpoints/user flows]`

### Manual Verification Steps
1. Navigate to `[URL or page]`
2. Perform action `[step-by-step instructions]`
3. Verify expected outcome `[result]`

### Visual Changes (UI Before & After)
<!-- If this PR modifies visual presentation, include screenshots or recordings. -->

| Viewport / Theme | Before | After |
| :--- | :--- | :--- |
| **Desktop (Light)** | *(image or N/A)* | *(image or N/A)* |
| **Desktop (Dark)**  | *(image or N/A)* | *(image or N/A)* |
| **Mobile (390px)**  | *(image or N/A)* | *(image or N/A)* |

---

## Rollback Strategy
<!-- How can this deployment be safely reversed if an incident occurs in production? -->
- [ ] **Zero-State Rollback**: Reverting application code or rolling back the deployment SHA has no adverse database effects.
- [ ] **Dual-Phase Rollback Required**: Rollback instructions documented below:
  - *Steps*:

---

## Reviewer Focus Areas
<!-- Highlight specific files, algorithms, or boundary conditions that deserve extra scrutiny. -->
- `[path/to/file.ts:L20-L45]`: [Specific area for reviewer focus]

---

## Pre-Submission Quality Gate
- [ ] Strict TypeScript compiles with 0 errors (`tsc --noEmit`)
- [ ] All automated test suites pass locally
- [ ] Scanned for secret leaks (`.env`, credentials, private tokens)
- [ ] Verified all temporary debug probes (`[DEBUG-xxxx]`) are removed
- [ ] Conforms to project guidelines in `PROMPTKIT.md` (and `DESIGN.md` if applicable)
