# Project Architectural Profile (`PROMPTKIT.md`)

## 0. PromptKit OS Profile
- **Profile**: balanced
- **Installed**: 2026-09-14
- **Engine**: .promptkit
- **Upgrade**: Run `.promptkit/init.sh --balanced` for full 22 workflows, or `--turbo --experimental` for parallel waves


> **Instructions for AI**: Read this file during every session. Adhere strictly to the project domain
> boundaries, commands, documentation targets, and non-negotiable architectural rules defined below.
> Everything in §1–4 was auto-detected by `pk:onboard` on **2026-09-10** at revision `d78f663`.
> Trust the manifests over this document if they ever disagree, and fix this file.

---

## 1. Project Overview & Domain

- **Project Name**: Job Tracker (`package.json` → `job-tracker`, `version` 0.2.0, `private: true`)
- **Domain / Purpose**: A personal job-search pipeline tracker. One user tracks applications
  (company, title, location, lifecycle status, applied date, notes) across Dashboard → Applications →
  Application Detail. **Not** a multi-tenant SaaS product: no organisations, no roles, no shared data.
- **Primary Users**: The repository owner, as a job seeker. This is simultaneously a **learning-first
  portfolio project** — see `AGENTS.md` §Purpose. Interview-explainability is a first-class
  requirement, not a nice-to-have.
- **Roadmap intent** (from `README.md`, in order): search + status filters → create/edit forms →
  page & route tests → replace mock data with API calls → ASP.NET Core Web API backend →
  authentication → AWS deployment. Nothing on that list beyond the first four items exists yet.

---

## 2. Active Technology Stack

| Layer | Reality (detected) |
| :--- | :--- |
| Language & Runtime | TypeScript 5.5.4, `strict: true`, `target: ES2020`, ESM (`"type": "module"`), `isolatedModules`, `noEmit` |
| Build Tool / Dev Server | Vite 5.4.2 + `@vitejs/plugin-react` 4.3.1 |
| Frontend Framework | React 18.3.1 + React DOM 18.3.1 (JSX transform: `react-jsx`) |
| Routing | `react-router-dom` 6.30.1 — `BrowserRouter`, declarative `<Routes>`, catch-all `<Navigate to="/" replace>` |
| Styling & Design System | **Plain global CSS** (`src/styles.css`, 187 lines, flat class selectors). No Tailwind, no CSS Modules, no UI library, no icon set. Tokens are documented in `DESIGN.md` but **not yet expressed as CSS custom properties** |
| State & Data Fetching | **None.** No Redux/Zustand/Jotai, no TanStack Query, no `fetch`/axios call anywhere in `src/`. Pages import the module-level array `mockApplications` directly and derive values during render |
| Backend & Database | **None in this repo.** No ORM, no schema, no migrations, no `prisma/`, no `drizzle.config.ts`. Planned: ASP.NET Core Web API + database |
| Authentication | **None.** No auth provider, no session cookies, no token handling, no protected routes |
| Testing | Vitest 2.0.5 (`globals: true`, `environment: 'jsdom'`, `css: true`, `include: src/**/*.{test,spec}.{ts,tsx}`), Testing Library React 16 + jest-dom 6, setup `src/test/setup.ts` (registers `afterEach(cleanup)`) |
| Lint / Format | **Absent** — no ESLint, Prettier, or Biome config and no `lint` script |
| CI / CD | **GitHub Actions** — `.github/workflows/ci.yml` runs `npm run verify` on every PR and on `main`. No deploy pipeline (AWS is M5) |
| Package Manager | **npm** (`package-lock.json`, lockfileVersion 3). `node_modules/` present, no `pnpm-workspace.yaml`, no `turbo.json`, no `nx.json` |

### Source topology (single-package SPA, 13 files in `src/`: 12 TS/TSX + 1 stylesheet)

```text
src/
├── main.tsx                     # StrictMode + BrowserRouter mount; the only non-null assertion (`#root`), allowed
├── App.tsx                      # Route table: / → Dashboard, /applications → List, /applications/:id → Details, * → redirect
├── styles.css                   # Entire design system; global, unscoped
├── types/application.ts         # Domain contract: JobApplication + ApplicationStatus union (5 states)
├── data/mockApplications.ts     # 5 seeded records. The one read-only data source
├── components/                  # Layout.tsx, StatusBadge.tsx, EmptyState.tsx, StatusBadge.test.tsx
├── pages/                       # DashboardPage, ApplicationsPage, ApplicationDetailsPage
└── test/setup.ts                # jest-dom + cleanup
```

**Import direction (observed, keep it):** `pages → components | data | types`, `components → types`,
`data → types`. `types/` imports nothing and is the leaf of the graph. Preserve this arrow direction.

**Non-app directories in the workspace** (found on intake **pass 2** — none participate in the build graph,
and none are named in the **shared** `.gitignore`):

| Path | What it is | Why it matters |
| :--- | :--- | :--- |
| `.promptkit/` | Better-PromptKit **submodule** @ `a1eb608` (`v1.1.1-11-ga1eb608`), clean, detached HEAD | Read-only vendor tooling. `.gitmodules` + this gitlink must be committed together (DEBT-14) |
| `.agents/rules/job-tracker-learning.md` | **The only one that is git-tracked**: a second copy of the project doctrine, for Kilo Code | 9 rules lived here and nowhere else; they are now mirrored into `AGENTS.md`. Still prescribes a hanging command (DEBT-12) |
| `.kilo/` | 63 MB — `@kilocode/plugin` 7.4.23 plus its own `node_modules` | Invisible to `git status` (self-ignoring `.kilo/.gitignore`) but visible to every tree walk (DEBT-13) |
| `.fallow/` | 28 KB binary analysis caches | Self-ignored via `*`; same walk-pollution issue |

`.git/info/exclude` also carries 12 Kilo worktree entries that are **local-only — they do not exist in anyone
else's clone**. When searching this repository, exclude `.kilo/` and `.fallow/` explicitly: a debt-marker sweep
on pass 2 returned 8 false `TODO:` hits from `.kilo/node_modules/**` before that was corrected.

### Domain contract — the highest-risk file in the repo

```ts
export type ApplicationStatus = 'Saved' | 'Applied' | 'Interview' | 'Rejected' | 'Offer'
export interface JobApplication {
  id: number          // ← surrogate key is `number`; a backend will want UUID/string. Changing it is a Level 2 breaking contract change
  companyName: string
  jobTitle: string
  location: string
  status: ApplicationStatus
  appliedAt?: string  // ← free-form string, currently 'YYYY-MM-DD'. Not `Date`, not branded, unparsed
  notes?: string
}
```

`StatusBadge` derives its CSS class as `` `badge badge-${status.toLowerCase()}` `` — meaning the
`ApplicationStatus` union is **coupled to CSS class names**. Renaming a status breaks styling silently.
Any status change must touch: the union, the `.badge-*` rule in `src/styles.css`, `DESIGN.md` §2, and
the `DashboardPage` filters.

---

## 3. Project Commands (Root / Global)

| Task | Command | Status |
| :--- | :--- | :--- |
| Install | `npm install` | ✅ |
| Dev server | `npm run dev` | ✅ |
| **Typecheck (fast inner loop)** | `npm run typecheck` (= `tsc -p tsconfig.app.json --noEmit`) | ✅ exit 0; **emits no files**. The script exists as of DEBT-03 (2026-09-11); it did not at intake, which is why older records say "there is no `typecheck` script" |
| Build (typecheck + bundle) | `npm run build` (`tsc -b && vite build`) | ✅ re-verified after the DEBT-11 fix (`bd8a8b6`): exit 0, 41 modules, **no `vite.config.js` emitted** |
| Preview production build | `npm run preview` | ✅ |
| Tests (watch) | `npm test` | ⚠️ this is bare `vitest` — **watch mode, never exits**. Never use it as a verification step |
| **Tests (single run — CI-style)** | `npm run test:run` | ✅ 100 tests / 13 files, ~11s (intake figure was 2/2; the suite grew through M1–M2b) |
| Lint (JS/TS) | `npm run lint` (`eslint . --max-warnings 0`) | ✅ 0 problems. `--max-warnings 0` means a warning fails the gate |
| Lint (CSS) | `npm run lint:css` (`stylelint "src/**/*.css"`) | ✅ 0 problems. Proven against the real DEBT-15 orphan: `git show f1ae7af:src/styles.css` fails |
| **All checks, in order** | `npm run verify` | ✅ typecheck → lint → lint:css → test:run → build. **This is what CI runs** — do not hand-roll a chain of the five |
| Real-browser check — **manual, deliberately not in `verify`** | `docker run` Chromium + `node crosstab.mjs` over raw CDP | ✅ 13/13 on 2026-09-11, first time any page here was rendered by a non-jsdom engine. Needs Docker and a 2.8 GB image, so it cannot be a gate. Recipe, results and limits: `docs/spikes/2026-09-11-real-browser-cross-tab-check.md` |
| Format | — | ❌ **still no formatter, deliberately.** Prettier would rewrite all 35 TS/TSX files in a commit unrelated to any feature; add it alone or not at all |
| E2E | — | ❌ not installed (no Playwright/Cypress) |

🚫 **Do not run bare `npx tsc -b` as a typecheck shortcut, and never remove `"noEmit": true` from
`tsconfig.node.json`.** Without it, `tsc -b` writes `vite.config.js` beside the real `vite.config.ts`, and
**Vite resolves `.js` before `.ts`** (`DEFAULT_CONFIG_FILES`, `node_modules/vite/dist/node/constants.js:33`),
so the stale compiled copy silently takes over `vite dev`/`vite build` while Vitest keeps reading the `.ts` —
tests green, app built from a config it is not using. Proven by probe 2026-09-10, **closed in `bd8a8b6`**.
`*.tsbuildinfo` is still written (harmless, ignored); `vite.config.js` is deliberately **not** ignored, so a
regression shows up loudly in `git status` instead of being silently excluded.

**Definition of Done for any change here**: `npx tsc -p tsconfig.app.json --noEmit` exits 0 **and**
`npm run test:run` is green **and** new/changed behaviour has a Vitest assertion. Report the exact
commands run (per `AGENTS.md`).

---

## 4. Monorepo & Workspace Topology

**`N/A (Standalone Repository)`.** Single `package.json` at the root, no workspace globs, no
`apps/` or `packages/` directories, no task runner. All commands run from the repository root with
plain `npm run …`. Ignore any `--filter` / Turborepo guidance in other PromptKit templates.

The `.promptkit/` directory is a **git submodule** (`better-promptkit`), not a workspace package.
Treat it as read-only vendor tooling: never edit workflow or protocol files inside it from this repo,
and never import from it into `src/`.

---

## 5. Documentation & Artifact Storage Paths

- **ADRs**: `docs/adrs/` · **RFC Specs**: `docs/specs/` · **Task Records**: `docs/tasks/`
- **RCAs / Post-mortems**: `docs/rca/` · **Spikes**: `docs/spikes/` · **Design Specs**: `docs/design/`
- **Data Models**: `docs/data/` · **Auth Specs**: `docs/auth/` · **API Contracts**: `docs/api/`
- **Test Plans**: `docs/tests/` · **Review Reports**: `docs/reviews/` · **Perf Audits**: `docs/perf/`
- **Releases**: `docs/releases/` · **Living State**: `docs/STATE.md`
- **Visual Identity**: `./DESIGN.md` · **Session Handover**: `pk:checkpoint` → `docs/STATE.md`

All 13 `docs/` subdirectories exist and are empty as of intake.

---

## 6. Non-Negotiable Architecture Rules & Guardrails

Tailored to this codebase; generic template language removed.

- [ ] **Strict TypeScript, zero `any`.** `strict: true` is already on — never weaken `tsconfig.app.json`,
      and never add `@ts-ignore`. One documented exception: the `document.getElementById('root')!`
      bootstrap assertion in `src/main.tsx`. No `@ts-expect-error` without a comment explaining why.
- [ ] **The domain union is a contract, not a string.** `ApplicationStatus` changes are **Level 2
      Controlled Work**: they ripple into CSS class names, dashboard filters, and the future API. Add a
      Task Record + spec first. Never widen to `string` to silence a type error.
- [ ] **No business logic in presentation components.** Derivations (counts, filters, sorts, status
      transitions) belong in a pure module (`src/domain/…`) that Vitest can test without rendering.
      *Current drift*: `DashboardPage` computes three `filter()` passes inline at module scope.
- [ ] **Data access goes through one seam.** Today that seam is `src/data/mockApplications.ts`. When
      persistence lands, no page or component imports storage directly — they consume a repository
      interface, and the mock becomes its test double. Keep `src/data/` the only module allowed to know
      where records come from.
- [ ] **IDs and dates stay typed, not coerced.** `Number(id)` on a route param and `appliedAt ?? 'Not yet
      applied'` are today's shortcuts; parse-and-validate at the boundary instead of comparing `NaN`.
- [ ] **WCAG 2.2 AA is verified, not assumed.** 11/11 colour pairs currently pass ≥4.5:1 (see
      `DESIGN.md` §6 — re-measure after any colour change). Interactive elements keep a ≥44×44px hit
      area and a visible `:focus-visible` ring. Never communicate status with colour alone: the badge
      carries its text label, keep it that way.
- [ ] **Deterministic error handling.** No silent `catch {}`. Empty and unknown-route states render the
      existing `EmptyState` / not-found branch — never a blank page. A future API layer returns a typed
      `Result` or a thrown typed error, not `undefined`.
- [ ] **One canonical agent rule set.** `AGENTS.md` is the source of truth for working style, verification
      commands, and the "avoid" list. `.agents/rules/job-tracker-learning.md` is an IDE-facing copy: change
      `AGENTS.md` first and re-sync it in the same commit (DEBT-12) — never let a third copy appear, and never
      follow its `npm run test` line, which hangs in watch mode.
- [ ] **Never tree-walk or `git add .` blindly.** Exclude `.kilo/`, `.fallow/`, `node_modules/`, and
      `.promptkit/` from searches, and stage by explicit path (DEBT-13).
- [ ] **Learnable increments.** Every merged change must be explainable in an interview. Where a real
      trade-off was made, record it as an ADR in `docs/adrs/` (that is what `pk:retro` is for). Prefer
      boring, readable code over abstraction the 5-record dataset does not need.
- [ ] **Never claim a green build without running it**, and never report `pnpm`/`yarn`/`turbo` commands
      — this repository is npm-only.

profile: balanced
