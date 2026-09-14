# Handoff Execution Record: M5 — AWS Deployment, handoff-001

- **Task ID**: `TASK-2026-09-13-m5-aws-deployment` → [`TASK-m5-aws-deployment.md`](./TASK-m5-aws-deployment.md)
- **Checkpoint Record**: [`TASK-m5-aws-deployment.checkpoint-001.md`](./TASK-m5-aws-deployment.checkpoint-001.md) — read it for evidence; read it *first* if you are the next session
- **Local Task Source**: `docs/tasks/TASK-m5-aws-deployment.md` — reconciled against the spec on 2026-09-13 (branch, state, decision IDs, AC-1, blockers and next action were all stale; the reconciliation table is §7 of the checkpoint record)
- **Specification**: `docs/specs/2026-09-13-spec-m5-aws-deployment.md` (1,221 lines, §0–§13, 17 tasks, 9 decisions)
- **Milestone / ceremony**: M5 — **Level 3 (Release-Critical)** · **State**: `handoff_ready` · **Recorded**: 2026-09-13 17:06 UTC
- **Revision**: `docs/state-m5-deploy-readiness-merged` @ `da04ee4` (contains `origin/main` `80918be` + 2 commits in no PR)

## Objective

Take the milestone from "spec written" to "the next person can resume without re-deriving anything", and make the
working tree's mixed concerns separable into an atomic commit ladder — without touching AWS, committing, or opening a PR.

## Scope

- Branch topology measured and published accurately — including the duplicate-SHA hazard that would otherwise bite at PR time.
- Every verification claim **re-executed** at this boundary, including a clean-room rebuild where cache could have falsified it.
- Stale `docs/STATE.md` projections regenerated as whole sections from live commands, never patched line-by-line.
- Four atomic commit boundaries identified and ordered, with each boundary's *concern* named.

## Explicitly out of scope

No deployment, no IaC, no AWS API call, no image push, no tag, no release, no `git push`, no commit, no PR. No `api/` or
`src/` edit. No resolution of §12 — those seven answers belong to the owner, and the spec is written so that guessing one
(e.g. the region) would have to be undone in eleven places later.

## Achievements

| # | What landed | Evidence |
| :--- | :--- | :--- |
| 1 | The container claim in the spec is now **re-proven clean-room**, not carried over | `docker build --no-cache-filter build` → exit 0, `#11 RUN dotnet restore` re-executed, **0 × `NU1015`**, manifest-list `sha256:ed0d92c0…`. The first build had six `CACHED` steps *including the restore step under test* — quoting its exit code would have repeated the exact mistake `AGENTS.md` records. |
| 2 | `.dockerignore` proven **effective**, not merely present | `#4 [internal] load .dockerignore → transferring context: 1.20kB` in both runs, i.e. BuildKit reads it; and the whole build transferred **≤ 9.07 kB** of context while `api/` on disk is **76,544,478 bytes**, of which **75,927,831 (99.2%) is host `bin/`+`obj/`**. Had the filter been absent, `COPY . .` would necessarily have uploaded tens of megabytes. |
| 3 | Four concerns confirmed **separable** and ordered into a ladder | `git status --porcelain` + per-file `git diff --numstat`; §12 of the checkpoint record names what each commit carries |
| 4 | `STATE.md` §3, §3A banner, §5, §7 and the §8 row regenerated from live measurement; §4 untouched | §4 identical before/after: **55 lines, 25 items, 8 `##` headings** in the file (measured, not assumed) |
| 5 | **Dockerfile diff read line-by-line** and found stronger than any summary claimed | it *removes* `"Healthcheck=ExecStart": null` rather than omitting it — the key is present-but-null in the base image metadata, so an omission-only fix would not have cleared it; it also **deletes** the inherited `HealthCheck` instruction with a comment explaining that ECS ignores Dockerfile healthchecks |
| 6 | A wrong routing conclusion from this session was caught, disclosed and corrected | spec §6 line 844 makes **T-06/T-07 immediately startable** (`Depends: —`, "can start immediately"); an earlier turn had declared the milestone wholly parked. The correction is in §9 of the checkpoint record, not hidden here. |
| 7 | `.m5-parts/` identified for what it is and given a reasoned disposition | 28 files / 212 KB, unreferenced by tracked files, not gitignored: the spec-assembly harness (`assemble.py`, `audit.py`, three probes, 14 parts, a pre-assembly snapshot). Decision: keep untracked **and un-ignored** so it stays loud, delete after the spec commit lands. |

## Verification (all re-executed 2026-09-13, UTC)

| Time | Command | Result |
| :--- | :--- | :--- |
| 17:02 | `npm run verify` | **exit 0** — 227 tests / 23 files, `61.23 kB gz` bundle, build 1.38 s |
| 17:0x | `npx tsc -p tsconfig.app.json --noEmit` | exit 0 |
| 17:04 | `docker build --no-cache-filter build -f api/Dockerfile api` | **exit 0**, digest `sha256:ed0d92c0…`, restore re-executed, 0 `NU1015` |
| 17:0x | `grep -c NU1015` on both build logs | `0` and `0` |
| 17:0x | `git merge-base --is-ancestor main origin/main` | true — local `main` is fast-forwardable |
| 17:0x | secret / debug-marker scan over changed files + `.m5-parts/*.py` | 1 hit, a placeholder template at spec line 350 — not a value |
| — | `dotnet test` | **not run** — no `api/` code changed; the recorded 169/169 is quoted as history, not re-claimed |

## Evidence (where the proof lives)

- Build logs from this session: `/tmp/jt-verify.log`, `/tmp/jt-dockerbuild.log` (incremental — the one that *looked*
  green without being strict), `/tmp/jt-dockerbuild-nocache.log` (the strict rerun whose exit code is quotable).
- Claims under review: spec §1.1–§1.5 (measurements that forced D-M5-2/D-M5-3 to be re-scoped), §4.1–§4.4 (SSE + health
  bounds → R-4.2/R-4.4), §11 (17 stale-doc rows, each with file:line), §12 (7 owner decisions, 6 blocking).
- Contract: `AGENTS.md` §"Verification commands" + §"Commit discipline"; workflow `.promptkit/workflows/checkpoint.md`.
- Structural guard used while editing `STATE.md`: **8** `##` headings, §4 = **55** lines / **25** items, measured before
  and after. Every removed line accounted for; no projection patched locally where it could be regenerated.

## Unresolved blockers (inherited by the next session)

1. **§12 owner decisions A–G — seven open, six blocking T-02…T-05.** A: workload region (decides the `us-east-1`
   certificate pairing and is a precondition for every `describe-*` measurement). B: domain / DNS host / zone owner — the
   long pole, because ACM DNS validation costs minutes-to-hours that no retry shortens. C: IaC tool — the spec frames the
   choice as *"which plan can you read aloud in an interview"*, not *"which scales to a team"*.
2. **The `D-M5-*` ID namespace was reused across two documents** (four in the Task Record, nine in the spec, with
   `D-M5-2`/`D-M5-3` meaning different things). Reconciled here by deferring to spec §2; **the owner should confirm that
   the Task Record's original four constraints are all still true under the spec's nine decisions** — that is a judgement
   about intent, not a measurement, and it is the one thing in this handoff I could not verify.
3. **Publishing hazard**: two commits with no PR, and a stale duplicate remote head under different SHAs (see §Branch of
   the checkpoint record).
4. **`docs/aws-deployment.md` still carries five stale rows** while being cited as the ratified record — T-15 owns it;
   nobody should re-derive infra facts from that file before T-15 lands.

## Next Action — exactly one, prioritized

**T-06: implement R-4.2 (the SSE keep-alive heartbeat) in `api/src/JobTracker.Api/ApplicationCatalog.cs:71-113`** — the
`GET /api/applications/events` handler, confirmed present at those lines — as **Red → Green → Refactor**.

It is first because it is the only action that is simultaneously **unblocked** (spec §6: `Depends: —`, *"can start
immediately"*) and **highest-consequence** (§4.1: without it the live-update feature is likely to break in production
while remaining invisible locally — the asymmetry the spec measured, and precisely why no local test catches its absence).
It touches no AWS surface, so it needs none of the §12 answers. Exit criteria are §4.2's four assertions. Route:
`pk:test` for assertion design, then `pk:fix`; ceremony **L1** (no schema, no auth, no public-contract change — the event
payload is unchanged and `: keep-alive` is a comment frame, which SSE clients discard by spec).

> [!WARNING]
> ### ⚠️ Blocked: waiting on human input, on every other M5 path
> Everything except T-06/T-07 waits on §12 A–G, and deploy / tag / release / push-to-`main` stay human-only. When the
> owner answers, those replies go into spec §3's `[verify-at-apply]` cells **before** any provisioning task runs.

**Runner-up, and the honest reason it lost**: `pk:commit` the tree into the four-commit ladder below, then `pk:pr`. Pure
mechanical cleanup of this session — but `AGENTS.md` notes a branch with no remote head is one disk failure from being
lost, and CI only runs on pushed branches. If the owner prefers durability over the T-06 code path, run the ladder first:
it is a one-line trade, not a dependency. T-06 requires none of those commits.

## The commit ladder prepared here (no commit was run)

`git status --porcelain` plus per-file `git diff --numstat` confirm **four separable concerns**, currently mixed in one
tree — which is the whole reason this section exists:

| # | Concern | Files | Why it must not merge with another |
| :--- | :--- | :--- | :--- |
| 1 | `fix(api): restore cleanly and declare the port contract in the image` | `api/Dockerfile` (±61) + `api/.dockerignore` (new, untracked) | One behaviour: the Dockerfile's `COPY . .` is safe **only because** the ignore file exists. Split them and `git revert` of either reintroduces the `NU1015` / artifact-shadowing failure. Proven by the `--no-cache-filter` rerun. |
| 2 | `docs(spec): rewrite M5 from measurements` | `docs/specs/2026-09-13-spec-m5-aws-deployment.md` (+1,255 / −74) | The measurement record. Reviewers must read it without engine or state noise — and it is the artifact §12's answers get written back into. |
| 3 | `chore(promptkit): engine bump 9ef20b8 → c7199c3` | `.promptkit` gitlink | Tooling, never a project decision. Bundling it makes the spec diff unreviewable and the revert dangerous. |
| 4 | `docs(state): regenerate M5 projections from measurement` | `docs/STATE.md` + the Task Record reconciliation + these two new records | Must land **last**: §3/§5/§7 are projections of the other three, so any earlier order leaves STATE.md's own record false for exactly one commit. |

`.m5-parts/` is in **none** of the four. It stays untracked *and un-ignored* so it remains loud, and is deleted once
commit 2 lands. Before each commit: `git status --porcelain` (assert what you staged **and** what you left), never
`verify && commit` on one line without `set -e`, no backticks inside `git commit -m` (use `-F <file>`), and after pushing,
`gh pr view <n> --json headRefOid` must equal `git rev-parse HEAD`.

## Handover Prompt (paste to resume)

> Resume **M5 — AWS Deployment** for `job-tracker` (React/TS + ASP.NET Core/.NET 10 + PostgreSQL, learning-first,
> **Level 3** release-critical). Read in this order: `docs/tasks/TASK-m5-aws-deployment.md`, then
> `docs/tasks/TASK-m5-aws-deployment.checkpoint-001.md` (§4 evidence, §7 reconciliation, §9 next action), then
> `docs/specs/2026-09-13-spec-m5-aws-deployment.md` §6 (T-01…T-17) and §12 (seven open owner decisions — six block
> everything except T-06/T-07). State is `handoff_ready`: **do not deploy, run IaC, call AWS, tag, release, push to
> `main`, commit, or open a PR** without explicit owner authorization. Do **not** repeat this session's earlier claim
> that the milestone is wholly parked on ratification — spec §6 line 844 makes **T-06 and T-07 startable now**, and the
> agreed next action is **T-06 (R-4.2 heartbeat) as a Red/Green/Refactor triple**. Verify before repeating any number:
> re-run `npm run verify`, and re-run the container check as `docker build --no-cache-filter build -f api/Dockerfile api`
> — a cached `docker build` does **not** re-execute the restore step under test. Re-measure branch topology before
> publishing (`git rev-parse --abbrev-ref HEAD`; `git log --oneline origin/main..HEAD`;
> `git log --oneline HEAD..origin/<branch>`): the remote holds duplicate commits under different SHAs, so
> `--force-with-lease` plus `headRefOid` verification is expected, not exceptional. Keep the four concerns separate per
> the ladder table above. If the owner answered §12 A/B/C in review, write those answers into spec §3's `[verify-at-apply]` cells before
> any provisioning task, and refresh STATE.md §3/§5/§7 as **whole regenerated sections** — never by patching the adjacent
> bullets.


