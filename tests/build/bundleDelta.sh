#!/usr/bin/env bash
#
# `-065` — AC-14: the login route's bundle delta must be under 3 kB against **Slice 0's** baseline, with the
# configuration named.  This script is the evidence, and its exit code is the assertion.
#
# WHY THE BASELINE IS REBUILT INSTEAD OF QUOTED.
# Slice 0 recorded 60,118 B (flag-off) and 61,368 B (flag-on, IP literal) at `41b32af`, and the AC's own recipe says
# `git worktree add … <Slice 0 sha>`.  Quoting the remembered number would make the delta a subtraction between one
# measurement taken at 05:25 UTC and one taken now, across whatever the toolchain did in between.  So both trees are
# built in this run, with the same dependency tree, the same URL literal, and the same command Slice 0 used.  The
# reproduced baseline is what the gate compares against, and any disagreement with 61,368 is printed rather than
# smoothed over.  `package.json` and `package-lock.json` are verified byte-identical to `41b32af` before a build
# starts: a dependency that moved would make this row's number about npm rather than about the login route.
#
# WHY THE URL LITERAL IS FIXED HERE AND NOT DISCOVERED.
# `import.meta.env.VITE_API_BASE_URL` is substituted into the bundle at build time, so the *length of the literal*
# changes the gzip sum.  Slice 0 measured that directly: the same code with `http://localhost:5080` versus the
# 14-character sandbox IP differ by 9 bytes.  The ratified comparison is the **IP** configuration, so that literal is
# hard-coded below.  It is not necessarily the address this machine answers today — it is the input that makes the
# baseline number mean what it says.  Those are different concerns and merging them is how a figure becomes fiction.
#
# WHAT "THE LOGIN ROUTE'S BUNDLE" MEANS IN THIS TREE, stated because the AC's wording implies a chunk that does not
# exist: `src/App.tsx:4` imports `LoginPage` **statically**, so `/login` is not code-split and its bytes land in the
# main bundle.  The measurement is therefore the whole-JS gzip sum at each configuration — exactly the command Slice 0
# recorded.  If a later slice adds `lazy()`, what this row means changes, and this header has to change with it.
#
# ONE SAFETY NOTE WORTH ITS OWN PARAGRAPH, because the first draft of this script nearly destroyed the repository's
# dependencies.  The baseline worktree needs a `node_modules`, and a symlink is the obvious move.  `rm -rf` on a
# symlink removes only the link — but `git worktree remove --force` is not `rm -rf`, and nothing here proves how it
# recurses.  An unprovable destructive path in a measurement script is a defect regardless of whether it fires, so:
# dependencies are **hard-linked** (`cp -al`, same filesystem, no copy cost; falling back to a real copy otherwise),
# and teardown is `rm -rf` + `git worktree prune` rather than git's recursive removal.  The run then verifies the
# repository's own `node_modules` is still populated, because the only acceptable proof of a safety argument is one
# that is checked on the machine that holds the data.

set -euo pipefail

BASE_SHA=${BASE_SHA:-41b32af}                             # Slice 0's commit, from the task record's §6 heading
GATE_BYTES=${GATE_BYTES:-3072}                            # "< 3 kB", taken as 3 * 1024
URL_LITERAL=${URL_LITERAL:-http://172.23.124.252:5080}    # Slice 0's flag-on configuration, verbatim
WORKTREE=${WORKTREE:-/tmp/m4base-065}
REPO=$(git rev-parse --show-toplevel)
VITE_VERSION=$(node -p "require('$REPO/node_modules/vite/package.json').version")

fail() { printf '\n\033[31mABORT: %s\033[0m\n' "$1" >&2; exit 1; }
step() { printf '\n\033[1m── %s\033[0m\n' "$1"; }

[ -d "$REPO/node_modules/vite" ] || fail "node_modules/vite missing in $REPO; run npm install first"

cleanup() {
  [ -n "${WORKTREE:-}" ] && [ "$WORKTREE" != "/" ] && rm -rf "$WORKTREE"
  git -C "$REPO" worktree prune 2>/dev/null || true
}
trap cleanup EXIT

# Slice 0's command, verbatim: build the app, then measure the gzip of every JS chunk concatenated.
# $1 = tree to build in, $2 = URL literal, or "" for the flag-off configuration.
build_sum() {
  local tree=$1 url=$2 log bytes
  log=/tmp/bundle-065-$(basename "$tree")-$( [ -n "$url" ] && echo on || echo off ).log
  rm -rf "$tree/dist"
  (
    cd "$tree"
    if [ -z "$url" ]; then unset VITE_API_BASE_URL; else export VITE_API_BASE_URL="$url"; fi
    npm run build
  ) >"$log" 2>&1 || { tail -25 "$log"; fail "build failed in $tree (url='${url}') — log above, and in $log" ; }
  ls "$tree"/dist/assets/*.js >/dev/null 2>&1 || fail "no JS chunks emitted in $tree; the build log is $log"
  bytes=$(cat "$tree"/dist/assets/*.js | gzip -c | wc -c | tr -d '[:space:]')
  printf '%s' "$bytes"
}

step "0 — are the two trees comparable at all?"
dirty=$(git -C "$REPO" status --porcelain | wc -l | tr -d '[:space:]')
printf '  working tree: %s uncommitted files · HEAD %s\n' "$dirty" "$(git -C "$REPO" rev-parse --short HEAD)"
[ "$dirty" = 0 ] || fail "-065 must measure a committed tree; the numbers go into a record that cites a SHA"
for f in package.json package-lock.json; do
  a=$(git -C "$REPO" rev-parse "$BASE_SHA:$f")
  b=$(git -C "$REPO" rev-parse "HEAD:$f")
  printf '  %-20s baseline %s  head %s  %s\n' "$f" "${a:0:8}" "${b:0:8}" \
    "$([ "$a" = "$b" ] && echo identical || echo DIFFERS)"
  [ "$a" = "$b" ] || fail "$f changed between $BASE_SHA and HEAD: the delta would measure npm, not the login route"
done
printf '  node %s · npm %s · vite %s\n' "$(node -v)" "$(npm -v)" "$VITE_VERSION"

step "1 — the baseline tree at $BASE_SHA"
rm -rf "$WORKTREE"
git worktree add --detach "$WORKTREE" "$BASE_SHA" >/dev/null 2>&1 || fail "git worktree add failed for $BASE_SHA"
printf '  baseline HEAD: %s   (main repo stays at %s)\n' \
  "$(git -C "$WORKTREE" rev-parse --short HEAD)" "$(git -C "$REPO" rev-parse --short HEAD)"
if cp -al "$REPO/node_modules" "$WORKTREE/node_modules" 2>/dev/null; then
  printf '  dependencies hard-linked into the worktree (same filesystem, no second copy)\n'
else
  printf '  hard-linking failed; copying node_modules instead\n'
  cp -a "$REPO/node_modules" "$WORKTREE/node_modules"
fi
[ -d "$WORKTREE/node_modules/vite" ] || fail "the worktree has no vite; nothing comparable can be built"

step "2 — four builds, two trees, two configurations"
b_off=$(build_sum "$WORKTREE" "")
b_on=$(build_sum "$WORKTREE" "$URL_LITERAL")
h_off=$(build_sum "$REPO" "")
h_on=$(build_sum "$REPO" "$URL_LITERAL")

delta=$((h_on - b_on))
delta_off=$((h_off - b_off))
printf '  %-34s %10s %10s %10s\n' configuration baseline head delta
printf '  %-34s %10s %10s %10s\n' "flag-off (VITE_API_BASE_URL unset)" "$b_off" "$h_off" "+$delta_off"
printf '  %-34s %10s %10s %10s\n' "flag-on (IP literal, 34 chars)" "$b_on" "$h_on" "+$delta"
printf '  %s\n' "  (the pinned literal is $URL_LITERAL)"

step "3 — the gate"
printf '  AC-14: login-route bundle delta < %s B (3 kB), at the flag-on configuration\n' "$GATE_BYTES"
printf '  reproduced baseline %s B · head %s B · delta %s B → \033[1m%s\033[0m\n' \
  "$b_on" "$h_on" "$delta" "$( [ "$delta" -lt "$GATE_BYTES" ] && echo PASS || echo FAIL )"
# No apostrophes in a single-quoted printf format: the first draft wrote M4s with one, which ended the string, turned
# the rest of the sentence into extra arguments, and ran on through a comment line into the format below it -- while
# still exiting 0. A report whose numbers are right and whose prose is shredded is a report nobody checks.
printf '  flag-off delta %s B: what the non-lazy auth code costs a build that never turns the API on\n' "$delta_off"
# Reported beside the number, never inside it, so drift in the reproduction is visible in this run.
printf '  Slice 0 recorded 60,118 / 61,368 B; reproduced %s / %s B, off by %+d / %+d bytes\n' \
  "$b_off" "$b_on" "$((b_off - 60118))" "$((b_on - 61368))"

step "4 — where the bytes are, head build, flag-on, gzip bytes per chunk"
for f in "$REPO"/dist/assets/*.js; do
  printf '%8s  %s\n' "$(gzip -c "$f" | wc -c | tr -d '[:space:]')" "$(basename "$f")"
done | sort -rn | head -12

step "5 — teardown did not take the repository's dependencies with it"
printf '  main node_modules entries: %s · vite present: %s\n' \
  "$(ls "$REPO/node_modules" | wc -l | tr -d '[:space:]')" "$([ -d "$REPO/node_modules/vite" ] && echo yes || echo NO)"
[ -d "$REPO/node_modules/vite" ] || fail "the worktree teardown destroyed node_modules; restore it before anything else"
printf '  dist/ ignored: %s · porcelain after builds: %s files\n' \
  "$(git -C "$REPO" check-ignore -q dist && echo yes || echo NO)" \
  "$(git -C "$REPO" status --porcelain | wc -l | tr -d '[:space:]')"

# Machine-readable output for CI. The script's own assertions are about the *head vs a rebuilt baseline*; the committed
# fixture in bundle-baseline.json is a separate claim, and only the caller that holds both can compare them. Emitting the
# numbers here is what lets the workflow assert "the fixture still reproduces" without parsing a human-readable table —
# which would be a test that breaks whenever someone improves the wording above it.
if [ -n "${RESULT_FILE:-}" ]; then
  {
    printf 'REPRODUCED_BASELINE_ON_B=%s\n' "$b_on"
    printf 'REPRODUCED_BASELINE_OFF_B=%s\n' "$b_off"
    printf 'HEAD_ON_B=%s\n' "$h_on"
    printf 'HEAD_OFF_B=%s\n' "$h_off"
    printf 'DELTA_B=%s\n' "$delta"
    printf 'BASE_COMMIT_REF=%s\n' "$BASE_SHA"
    printf 'URL_LITERAL_B=%s\n' "$URL_LITERAL"
  } > "$RESULT_FILE"
fi

[ "$delta" -lt "$GATE_BYTES" ] || fail "AC-14 gate exceeded: ${delta} B >= ${GATE_BYTES} B at $URL_LITERAL"
printf '\n\033[32m-065 complete: the flag-on delta is %s B against a reproduced %s B baseline (under the %s B gate),\n' \
  "$delta" "$b_on" "$GATE_BYTES"
printf 'both trees built with an identical dependency tree and the same URL literal %s.\033[0m\n' "$URL_LITERAL"
