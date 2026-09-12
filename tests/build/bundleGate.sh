#!/usr/bin/env bash
#
# -076 — AC-14's bundle gate, running on every pull request instead of on one agent's memory.
#
# `-065` measured 1,242 B against a 3,072 B budget and then nothing checked it again: `grep -c bundleDelta
# .github/workflows/ci.yml` → 0. A dependency minor bump that adds 2 kB reaches `main` with every job green, and AC-14
# stays a checked box describing a build that no longer exists. This script is the teeth.
#
# WHAT IT COMPARES AGAINST, and the one compromise in it. `-065` refused to quote its baseline and rebuilt `41b32af` in
# the same run. That is the right call for a record and the wrong one for CI: `actions/checkout` defaults to depth 1, and
# four builds per push buys a number that only moves when a dependency moves. So the baseline is the fixture in
# `bundle-baseline.json` — which carries its provenance, the URL literal, the toolchain and the reproduction claim —
# and the counterweight is the dispatch-only `bundle-baseline-recheck` job in ci.yml, which runs `bundleDelta.sh`
# (rebuilds both trees) and fails if the fixture has drifted. The gate is cheap; the thing the gate asserts is still
# verified against history, just not on every commit.
#
# THE URL LITERAL IS LOAD-BEARING, and the script refuses to build without it. `import.meta.env` substitution inlines
# the string, so a build at `localhost` and a build at the IP differ by 9 gzip bytes on identical code. A gate that
# measured a different literal than the baseline would be comparing two configurations and calling the difference a
# regression.

set -euo pipefail

REPO=$(git rev-parse --show-toplevel)
FIXTURE="$REPO/tests/build/bundle-baseline.json"
[ -f "$FIXTURE" ] || { echo "::error::$FIXTURE missing; the gate has no baseline and must not guess one" >&2; exit 1; }

# node rather than jq: it is already the toolchain here (the frontend build), and `node -p` is the form this
# repository's other measured checks use.
read_fixture() { node -e 'const f=require(process.argv[1]); const v=f[process.argv[2]]; if (v===undefined) { console.error(`fixture field ${process.argv[2]} missing`); process.exit(1) } console.log(v)' "$FIXTURE" "$1"; }

BASE_COMMIT=$(read_fixture baseline_commit)
BASE_ON=$(read_fixture flag_on_bytes)
BASE_OFF=$(read_fixture flag_off_bytes)
URL_LITERAL=$(read_fixture url_literal)
FIX_GATE=$(read_fixture gate_bytes)
GATE_BYTES=${GATE_BYTES:-$FIX_GATE}          # overridable so the gate's own teeth can be tested

printf '── AC-14 bundle gate (`-076`)\n'
printf '  baseline      %s B at %s (fixture: %s, measured %s)\n' "$BASE_ON" "$URL_LITERAL" "$BASE_COMMIT" "$(read_fixture measured_utc)"
printf '  budget        %s B (fixture says %s; GATE_BYTES override%s)\n' "$GATE_BYTES" "$FIX_GATE" "$( [ "$GATE_BYTES" = "$FIX_GATE" ] && echo " not in use" || echo " IS IN USE — this run is not the ratified gate" )"
printf '  toolchain     node %s · npm %s\n' "$(node -v)" "$(npm -v)"

# Build the head exactly as `-065` built it. The literal is required, not defaulted.
step() { printf '  %s\n' "$1"; }
rm -rf "$REPO/dist"
VITE_API_BASE_URL="$URL_LITERAL" npm run build > /tmp/bundle-gate-build.log 2>&1 || {
  tail -25 /tmp/bundle-gate-build.log
  echo "::error::the head build failed; the bundle gate cannot evaluate it" >&2
  exit 1
}
ls "$REPO"/dist/assets/*.js > /dev/null 2>&1 || { echo "::error::no JS chunks emitted" >&2; exit 1; }
HEAD_ON=$(cat "$REPO"/dist/assets/*.js | gzip -c | wc -c | tr -d '[:space:]')

DELTA=$((HEAD_ON - BASE_ON))
printf '  head          %s B at the same literal\n' "$HEAD_ON"
printf '  delta         %+d B  (AC-14 budget %s B)  → %s\n' "$DELTA" "$GATE_BYTES" \
  "$( [ "$DELTA" -lt "$GATE_BYTES" ] && echo 'PASS' || echo 'FAIL' )"

if [ "$DELTA" -ge "$GATE_BYTES" ]; then
  printf '\n::error::AC-14 breached: the bundle grew %+d B over the %s B baseline, budget was %s B.\n' "$DELTA" "$BASE_ON" "$GATE_BYTES"
  cat <<'WHY'

  What this failure means, and what it does not:

    * It does NOT mean "raise GATE_BYTES until it passes". The budget is AC-14's ratified figure; spending more of it is
      an acceptance-criterion decision, and `-065`'s record says why the number is meaningful at all.
    * It means something added ~kB to the shipped JS since `41b32af`. The usual suspect is a dependency: run
      `git diff --stat <base> HEAD -- package.json package-lock.json` and read it before reading the app code.
    * To see where the bytes are: `for f in dist/assets/*.js; do printf "%8s  %s\n" "$(gzip -c "$f" | wc -c)" "$f"; done | sort -rn`
    * If the baseline itself is now suspect (a toolchain bump, a vite minor that compresses differently), do not patch
      the fixture by hand: re-run `tests/build/bundleDelta.sh`, which rebuilds the baseline commit in the same run, and
      update `tests/build/bundle-baseline.json` from what it prints, keeping the provenance fields honest.

WHY
  exit 1
fi

# Hygiene, bounded to what a build can actually do. The first draft reused the DEBT-11 step's `git status --porcelain`
# check and failed on this row's own uncommitted source files — which is the right instinct aimed at the wrong set:
# untracked work-in-progress is not build leakage. What a build *can* leave behind is a config shadow (DEBT-11), a
# tsbuildinfo, or a modification to a tracked file. Those three are what is asserted here.
shadow=$(git status --porcelain --untracked-files=all -- vite.config.js vite.config.d.ts '*.tsbuildinfo' | head -5)
tracked=$(git status --porcelain --untracked-files=no | head -5)
if [ -n "$shadow" ] || [ -n "$tracked" ]; then
  echo "::error::the bundle build left artifacts or tracked changes behind:"
  [ -n "$shadow" ] && printf '%s\n' "$shadow"
  [ -n "$tracked" ] && printf 'tracked: %s\n' "$tracked"
  echo "   (DEBT-11: an emitted vite.config.js shadows vite.config.ts for vite build/dev while Vitest reads the .ts)"
  exit 1
fi

# flag-off is reported but not gated: `-065` found the auth UI ships either way (1,022 B), so a flag-off number is
# context for a future decision, not a promise this row made.
printf '  note          flag-off baseline on file is %s B; AC-14 gates the flag-on figure only\n' "$BASE_OFF"
printf '\n\033[32m✓ -076: the gate ran the build, compared it to a provenance-carrying baseline, and had teeth.\033[0m\n'
