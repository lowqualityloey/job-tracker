# Brand & Design Identity Specification (`DESIGN.md`)

> **Instructions for AI**: Read this file during every session. It is the source of truth for the
> project's visual identity, tokens, and accessibility floor. Everything in §1–6 was **extracted from
> `src/styles.css` (187 lines) and measured** by `pk:onboard` on 2026-09-10 — it describes the design
> system as it is *built today*, not as it is wished to be. §7 lists where the build deviates from the
> Better-PromptKit anti-slop defaults, and §8 is the fix order.

---

## 1. Brand Essence & Visual Character

- **Product Name**: Job Tracker
- **Brand Personality**: Utilitarian, calm, high-signal. A personal dashboard, not a marketing site.
- **Theme Default**: **Dark only.** No light theme, no `prefers-color-scheme` block, no toggle. The
  entire stylesheet is written against dark surfaces; anything new must be verified on dark.
- **Aesthetic Tone**: Deep navy field with a subtle vertical gradient, matte translucent cards, generous
  16–20px grid gaps, pill-shaped status chips as the only saturated elements on a page. Colour is used
  semantically (pipeline state), never decoratively.
- **Voice in UI copy**: Plain and task-oriented ("Track your job search in one place", "Not yet applied",
  "No notes yet."). Lowercase-free sentence case, no exclamation marks, no emoji, no hype adjectives.

---

## 2. Colour Palette (extracted) & Semantic System

**Neutral base: Slate.** The palette is Tailwind's slate/blue/violet/emerald/rose families, but imported
as raw hex literals rather than as tokens.

| Role | Value | Where used |
| :--- | :--- | :--- |
| Background (page) | `#0b1220` → gradient `linear-gradient(180deg, #0b1220 0%, #111827 100%)` | `:root`, `body` |
| Surface (card) | `rgba(15, 23, 42, 0.82)` → effective `#0e1628` over the page bg | `.stat-card`, `.application-card`, `.empty-state` |
| Surface (control idle) | `rgba(148, 163, 184, 0.1)` → effective `#19212f` | `.nav-link` |
| Border (card) | `1px solid rgba(148, 163, 184, 0.18)` | cards, empty state |
| Foreground (primary text) | `#e5eef5` | `:root` |
| Foreground (muted) | `#94a3b8` | `.muted-text`, `.stat-card span` |
| Foreground (nav idle) | `#cbd5e1` | `.nav-link` |
| Accent / link | `#93c5fd` | `.eyebrow`, `.details-link`, focus ring |
| Brand primary (active nav) | `#2563eb` (text on it: `#eff6ff`) | `.nav-link-active` |

### Pipeline status semantic colours (`.badge-*`)

Status colour is **additive only** — the label text is always rendered inside the badge, so state is never
carried by hue alone. Keep that invariant.

| Status | Background | Text | Class |
| :--- | :--- | :--- | :--- |
| `Saved` | `#334155` | `#e2e8f0` | `.badge-saved` |
| `Applied` | `#1d4ed8` | `#dbeafe` | `.badge-applied` |
| `Interview` | `#7c3aed` | `#ede9fe` | `.badge-interview` |
| `Rejected` | `#b91c1c` | `#fee2e2` | `.badge-rejected` |
| `Offer` | `#047857` | `#d1fae5` | `.badge-offer` |

The mapping is implicit and load-bearing: `StatusBadge.tsx` builds its class name as
`` `badge badge-${status.toLowerCase()}` ``, so **the CSS class name is part of the domain contract**.
A new or renamed `ApplicationStatus` without a matching `.badge-*` rule renders an unstyled chip with no
warning. Add a test fixture that asserts every union member resolves to a styled badge.

---

## 3. Typography Hierarchy

- **Stack (single, no webfont file)**: `Inter, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`
- **Base**: `font-weight: 400`, `line-height: 1.5`, `margin: 0` on body.
- **Scale in use**: `0.8rem` eyebrow (uppercase, `letter-spacing: 0.08em`) → `0.85rem` badge (weight 700)
  → body `1rem` → `2rem` `.stat-card strong` → page headings `h1`/`h2`/`h3` at **browser defaults**
  (never restyled beyond `margin: 0` on `.site-header h1`, `.page-stack h2`, `.application-card h3`).
- **Not yet applied**: `text-wrap: balance` on headings, and `font-variant-numeric: tabular-nums` on
  `.stat-card strong` — counters currently jitter as digits change. Both are §8 fixes.
- **No monospace / numeric face** is declared anywhere.

---

## 4. Surfaces, Radius & Elevation

- **Radii**: cards `18px`; nav links, badges, and chips `999px` (pill). Two values only, applied consistently.
- **Spacing rhythm**: page shell `max-width: 1100px`, padding `24px` (`16px` ≤640px). Section gaps `20px`;
  grid gaps `16px`; card padding `20px`; badge padding `6px 12px`; nav padding `10px 14px`.
- **Elevation**: **zero shadows anywhere.** Depth comes purely from the translucent surface + 1px border.
  This is a strength — keep it; do not introduce drop shadows to "make cards pop".
- **Glassmorphism dose**: cards are `0.82` alpha but have **no `backdrop-filter`**, so there is no frosting
  cost. Stay under 1 translucent-blur surface per view.
- **Grids**: `.stats-grid` is `repeat(auto-fit, minmax(180px, 1fr))`; `.card-list` is a single-column stack.

---

## 5. Mobile Ergonomics & Responsive Reflow

- **One breakpoint**: `@media (max-width: 640px)` — shell padding shrinks, `.site-header` and
  `.card-header` flip to `flex-direction: column`, `.nav-list` goes full-width and wraps.
- **Reflow over shrinking**: ✅ multi-column stat grid collapses via `auto-fit`/`minmax` naturally.
- **Body guard**: `body { min-width: 320px }` — smallest supported viewport.
- **Touch targets**: ✅ `.nav-link` and `.details-link` both declare `min-height: 44px`. This is the
  accessibility floor for every future button, input, and row action.
- **Unmet**: `min-width: 0` is not declared on flex/grid children, and no truncation rules exist, so long
  company names or notes will push or wrap unpredictably at 320px. Add `min-w-0` equivalents now.

---

## 6. Accessibility — measured, not assumed

Contrast ratios computed programmatically at intake (sRGB relative luminance, blended alpha over the page
background). **11 measurements across 10 distinct foreground/background rows** (eyebrow and `.details-link`
share the same `#93c5fd` on `#0e1628` pair). All clear the WCAG 2.2 AA text threshold of 4.5:1.

| Foreground / Background | Ratio | Verdict |
| :--- | ---: | :--- |
| `#e5eef5` on `#0b1220` (body text) | **15.94** | ✅ AA |
| `#cbd5e1` on `#19212f` (nav idle) | **10.88** | ✅ AA |
| `#93c5fd` on `#0e1628` (eyebrow, links) | **10.00** | ✅ AA |
| `#e2e8f0` on `#334155` (`Saved`) | **8.40** | ✅ AA |
| `#94a3b8` on `#0e1628` (muted text) | **7.04** | ✅ AA |
| `#dbeafe` on `#1d4ed8` (`Applied`) | **5.49** | ✅ AA |
| `#fee2e2` on `#b91c1c` (`Rejected`) | **5.30** | ✅ AA |
| `#ede9fe` on `#7c3aed` (`Interview`) | **4.80** | ⚠️ AA, thin margin |
| `#d1fae5` on `#047857` (`Offer`) | **4.84** | ⚠️ AA, thin margin |
| `#eff6ff` on `#2563eb` (active nav) | **4.75** | ⚠️ AA, thin margin |

**Do not darken these three backgrounds.** `Interview`, `Offer`, and active-nav have under `0.4` ratio of
headroom; any darkening, alpha overlay, or new translucent layer on top of them silently drops the pair
below AA. Re-run the measurement after touching any colour.

- **Focus**: ✅ `outline: 3px solid #93c5fd; outline-offset: 3px` on `:focus-visible` — but declared **only**
  for `.details-link` and `.nav-link`. Any new interactive element must get an equivalent ring; `outline: none`
  is banned outright.
- **Semantics**: ✅ `aria-label="Primary navigation"` on `<nav>`, `<main>`, `<header>`, `<article>` in place;
  `key={item.to}`/`key={application.id}` present.
- **Gaps**: no skip link, no visible `<title>` change per route (static `"Job Tracker"`), and the stats are
  `<article>` elements rather than a list/definition structure — screen readers hear three unrelated regions.

---

## 7. Deviations from Better-PromptKit anti-slop defaults

Honest register of where the built system differs from the generic template. Deviations are **accepted**
unless marked *decide*; changing one is a design decision, not a cleanup.

1. **No token layer.** `src/styles.css` contains **18 distinct hex/rgba colour literals used 22 times** and
   **zero CSS custom properties** (`#93c5fd` ×3, `#94a3b8` ×2, counted). The anti-slop rule requires tokens.
   This is the single highest-leverage §8 fix.
2. **`100vh`** on `body`/`#root` instead of `dvh` — causes the mobile URL-bar jump the template warns about.
3. **Pill shapes on nav links and badges** (`999px`) vs the template's anti-pill rule. *Accepted* for the
   status chip (it reads as a tag) — **decide** for nav: pills + a solid blue active state are the only
   hierarchy cue besides weight.
4. **No motion system at all.** No `transition` declarations; hover is an instant `opacity: 0.92`. The
   compositor-only + `prefers-reduced-motion` rules are therefore *unmet by absence*, not violated. The
   moment animation is added it must satisfy both.
5. **No `:active` state** → hover-to-tap parity is unmet; touch users get no press feedback.
6. **Hover feedback is `opacity` only**, which fails to meet the ≥3:1 non-text contrast expectation for
   state change on already-low-contrast idle fills. Prefer a background/border shift.
7. **Gradient backdrop** is a subtle vertical navy→slate (`#0b1220`→`#111827`), *not* the prohibited
   blue→purple AI gradient. Accepted.
8. **Single 640px breakpoint** leaves 641–1100px with three-across stat cards at awkward widths.

---

## 8. Design remediation order (feed to `pk:tasks`)

1. Introduce CSS custom properties in `:root` (`--color-*`, `--radius-card`, `--space-*`, `--hit-target`)
   and rewrite the 187 lines to consume them — zero visual change, verified by re-measuring all 11 pairs.
2. Add `font-variant-numeric: tabular-nums` to `.stat-card strong`, `text-wrap: balance` to headings,
   `min-width: 0` to card/flex children.
3. Swap `100vh` → `100dvh`; add `env(safe-area-inset-*)` to the shell padding.
4. Add a global `:focus-visible` ring rule, an `:active` state, and one `@media (prefers-reduced-motion: reduce)`
   reset — as a single a11y PR with a contrast re-measurement in the description.
5. Only then introduce any motion or a second breakpoint.
