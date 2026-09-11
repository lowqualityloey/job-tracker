# Real-browser cross-tab check — M2b's DEBT-01 residual

**Date:** 2026-09-11 · **Method:** manual one-off harness · **Browser:** Chromium 128.0.6613.18 (headless, Docker)
**Result: 13/13 checks passed.** This closes the residual on DEBT-01 and retires P2-3 and P2-4 of
`docs/reviews/2026-09-11-m2b-review.md` — with two side effects worth more than the check itself.

## Why this existed at all

M2b's behaviour 022 ("an edit in one tab updates the other without a reload") is the only behaviour in the
milestone that **jsdom structurally cannot test**. jsdom never fires `storage` on its own; the suite dispatches
the event by hand, which correctly models the browser rule *that the writer tab does not receive its own
event* — but it is still a simulation of a browser, run inside a reimplementation of one. And AC-10's 44 px
targets and `:focus-visible` ring had been verified by **reading `src/styles.css`**, which is a claim about a
stylesheet, not about what a browser paints.

The wider version of the same doubt: since M1, **no page in this repository had ever been rendered by a real
browser.** 100 tests, five milestones' worth of commits, and the production build had never been loaded by
anything that isn't jsdom.

## Two findings that came out of doing it

**1. The check found nothing wrong with the app and three things wrong with the harness.** Every red run below
was my own instrumentation. That distinction is the whole reason to run a check like this with its output
read line by line: a green harness with a bug says nothing, and a red harness with a bug *looks like a
defect in the product*. See "Harness bugs" at the bottom.

**2. It exposed a real defect in `eslint.config.js`, landed four hours earlier.** Adding a plain `.mjs` file
outside `src/` made `npm run lint` **crash with exit 2** rather than report findings: `recommendedTypeChecked`
was spread unscoped, so every file ESLint read demanded parser services, and `projectService` refuses any file
no tsconfig claims. The rescue was enumerated per pattern (`*.config.{js,ts}`), which meant each new *kind* of
file re-triggered the same crash — I hit it twice in one sitting. Fixed by scoping the type-checked preset
inside the `src/**` block, so typed rules apply only where types exist. Committed separately, and proved both
ways: a floating promise in `src/` is still an error, and the same code in plain JS is silently allowed —
which is the documented, intended asymmetry, not a gap.

## What was run

```bash
# The app and the browser must share a network namespace: this machine's Docker daemon is not in my
# shell's, so `--network=host` gave a container that could see neither my preview server nor its own
# published debug port. Everything therefore runs inside one container.
docker run -d --name e2e --shm-size=1g mcr.microsoft.com/playwright:latest sleep infinity
docker exec e2e mkdir -p /srv
docker cp dist e2e:/srv/dist && docker cp docs/spikes/2026-09-11-real-browser-cross-tab-check/ e2e:/srv/ -q
docker exec -d e2e node /srv/static.mjs                        # serves the production build on 4173
docker exec -d e2e /ms-playwright/chromium-1129/chrome-linux/chrome \
  --headless=new --no-sandbox --disable-dev-shm-usage --remote-debugging-port=9222 \
  --remote-allow-origins='*' --user-data-dir=/tmp/crprofile about:blank
docker exec e2e node --experimental-websocket /srv/crosstab.mjs   # Node 20 needs the flag for global WebSocket
```

`vite preview` was verified separately from the host: `curl http://127.0.0.1:4173/applications` → **HTTP 200**,
so the SPA fallback for a client-side route works in the real preview server too.

### Why raw CDP and not Playwright

The Playwright image ships browsers but **not** the `playwright` npm package, and its `latest` tag is stale —
Chromium build 1129, Node 20.16 — so installing a current `playwright` would have mismatched the browser
revision. Driving Chromium over the DevTools protocol from Node's built-in `WebSocket` needs **no dependency
at all**: nothing added to `package.json`, nothing added to `devDependencies`, no E2E framework entering the
repo by the back door. A throwaway check should not cost a permanent dependency.

### The one design decision that mattered most

Two tabs were opened as **two targets on one browser connection**. Playwright "contexts", or two profiles,
have *isolated* storage — the same-origin tabs would not have shared a storage area, and the test would have
either failed for the wrong reason or passed by dispatching events manually, which is exactly the simulation
this check exists to escape.

Typing used `Input.insertText` against a focused element, so React receives a genuine `input` event through
its own listener rather than a value assignment plus a hand-made `dispatchEvent`. Submit used `.click()`, a
real DOM click that bubbles to React's root handler.

## Results

| # | Check | Result |
| :-- | :--- | :--- |
| 1 | Production build boots and renders in a real browser | ✅ 5 cards (the seed path ran for real) |
| 2 | Filter count region renders | ✅ `"Showing 5 of 5 applications"` |
| 3 | Client-side navigation to the create form | ✅ not `MemoryRouter` |
| 4 | Tab A sees the record it created | ✅ |
| 5 | **Behaviour 022: tab B updated with no reload** | ✅ 6 cards |
| 6 | Tab B's count moved with its rows | ✅ `"Showing 6 of 6 applications"` |
| 7 | **The direction jsdom cannot model: tab B wrote, tab A saw it** | ✅ |
| 8 | Chip hit target as *rendered* | ✅ 45 px tall, `min-height: 44px` |
| 9 | Search input hit target | ✅ 46 px |
| 10 | Filter chip reachable by Tab alone | ✅ 3 tab stops from document start |
| 11 | `:focus-visible` paints a real ring | ✅ `solid 3px rgb(147, 197, 253)` |
| 12 | Envelope on disk | ✅ `schemaVersion: 1`, 7 applications |
| 13 | Only our own storage key written | ✅ `job-tracker:applications` |

## What this does *not* prove

- **Chromium only.** No Firefox, no Safari — and `storage` event semantics are where browsers have
  historically differed. M2's claims are now "verified in Chromium 128", not "verified everywhere".
- **Happy path only.** No quota-exceeded write, no `SecurityError` from a blocked third-party context, no
  corrupt-envelope quarantine — all of those remain jsdom-only, which is where they are *reachable* from a
  test (`static.mjs` and the harness have no way to make the browser run out of quota).
- **It is not CI.** This needs Docker, a 2.8 GB image, and a container. Nothing here can fail a build
  automatically; DEBT-15's sibling claim — no CSS assertion harness — **stays open**. What changed is that the
  specific numbers AC-10 asked for are now measured facts instead of readings of a stylesheet.
- One run, not a stability sample. Cross-tab races (P2-2's overlapping re-reads, which M3 owns) would not
  reliably show up in a single pass.

## Harness bugs found on the way (all three mine, none the app's)

1. **`Runtime.evaluate` answers with `{result: {value}}`.** Reading `.value` off the envelope returned
   `undefined`, so a fully rendered page looked like it had never rendered. The failure message — "app never
   rendered" — was *about my code*. Cost of not checking: I nearly reported a browser defect in M2.
2. **`clickEl` takes a CSS selector, and I passed it a whole JS expression**, which got `JSON.stringify`'d
   into `querySelector(...)` → `DOMException: SyntaxError`. Fixed by documenting the parameter's contract at
   the function instead of remembering it at the call site.
3. **`pkill -f "vite preview"` matched its own invoking command line** and SIGTERM'd the shell running it —
   a cleanup command that killed the cleanup. Use `pkill -f '[v]ite preview'` or `pgrep` first. Same family as
   "`set -e` does not cover a pipeline": the tool reports on whatever it is actually pointed at.
