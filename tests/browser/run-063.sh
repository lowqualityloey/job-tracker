#!/usr/bin/env bash
# BEHAVIOR-m4-auth-063 driver: the antiforgery gate proven in real Chromium, as the owner-approved two cases.
#
# Run from the repository root, on the host that also runs the API.
#
# WHAT THIS RUN NEEDS THAT run-064.sh DOES NOT
# `-064` measures one origin talking to itself. AC-11's case 2 is about a **second origin** — that is the whole
# mechanism — so this runner adds a plain static server on another port and hands the harness its URL. It is
# deliberately un-TLS'd: the claim is about cookies and headers, and a second certificate would only add a reason for
# the run to fail for a reason unrelated to what it measures.
#
# The shared boot recipe (scratch DB, certificate, vite build, API boot + poll) lives in lib-e2e.sh, extracted by
# TDD-EXEC-m4-authentication-075. Only the cross-origin server and the DB assertions that are specific to this row
# remain here.
set -euo pipefail
source tests/browser/lib-e2e.sh
e2e_defaults

SCRATCH_DB=${SCRATCH_DB:-jobtracker_e2e063}
SCRATCH_ROOT=/tmp/jt-e2e063-dist
CROSS_PORT=${CROSS_PORT:-4179}
CROSS_ORIGIN="http://${HOST_IP}:${CROSS_PORT}"
CROSS_ROOT=/tmp/jt-e2e063-cross
HARNESS_SRC=tests/browser/csrfGate.mjs
HARNESS_DST=/srv/csrfGate.mjs
CERT=/tmp/jt-e2e063-cert.pem
KEY=/tmp/jt-e2e063-key.pem
API_PID=''
CROSS_PID=''
trap e2e_cleanup EXIT

step "preconditions"
command -v python3 >/dev/null || fail "python3 is needed for the second origin"
e2e_preconditions

step "1 — scratch database"
e2e_scratch_db

step "2 — self-signed certificate for $HOST_IP"
e2e_cert

step "3 — build the front end pointed AT THE APP ORIGIN"
e2e_build

step "4 — the second origin, serving one blank page"
mkdir -p "$CROSS_ROOT"
# Not empty: `document.body` must exist for the harness to evaluate in that page's context, and a 404 directory
# listing has no body to run against on every server.
printf '<!doctype html><title>cross-site probe</title><body>probe</body>\n' > "$CROSS_ROOT/probe.html"
python3 -m http.server "$CROSS_PORT" --bind 0.0.0.0 --directory "$CROSS_ROOT" \
  > /tmp/jt-e2e063-cross.log 2>&1 &
CROSS_PID=$!
sleep 1
kill -0 "$CROSS_PID" 2>/dev/null || fail "the cross-site server died immediately (port ${CROSS_PORT} busy?)"
echo "serving $CROSS_ROOT at $CROSS_ORIGIN (pid $CROSS_PID)"

step "5 — boot the API"
e2e_boot_api

# The two origins must be mutually reachable from the browser's namespace, or case 2 measures a network failure.
CROSS_CODE=$(docker exec "$CONTAINER" curl -s -m 4 -o /dev/null -w '%{http_code}' "${CROSS_ORIGIN}/probe.html" || echo 000)
[ "$CROSS_CODE" = "200" ] || fail "${CROSS_ORIGIN}/probe.html returned $CROSS_CODE from inside $CONTAINER"
echo "GET ${CROSS_ORIGIN}/probe.html -> 200 from inside the container (case 2 has somewhere to run)"

step "6 — the two cases"
run_status=0
e2e_run_harness -e E2E_APP="$ORIGIN" -e E2E_CROSS="$CROSS_ORIGIN" \
  -e E2E_LOGIN_EMAIL="$BOOTSTRAP_EMAIL" -e E2E_LOGIN_PASSWORD="$BOOTSTRAP_PASSWORD" \
  || run_status=$?
echo "run: exit $run_status"
[ "$run_status" = 0 ] || { grep -E '^(PASS|FAIL|HARNESS)' /tmp/e2e-run.log || true; fail "-063's browser cases failed (exit $run_status)"; }

step "7 — the database agrees with what the browser did"
# Every marker the harness writes is deleted by its own cleanup, and a refusal must have written nothing at all, so a
# correct run leaves zero probe rows. The count is the part that cannot be faked by a status code: C1a, C1b and C1e
# all answered "403", and if any of them had refused *after* the handler ran, a row would be here.
LEFT=$(docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$SCRATCH_DB" -tAc \
  "SELECT count(*) FROM applications WHERE company_name LIKE 'CSRF-063-%';" | tr -d '[:space:]')
echo "probe rows left behind: $LEFT (expected 0)"
[ "$LEFT" = "0" ] || fail "a refused write left $LEFT row(s): the gate answers 403 but does not stop the effect"

# Two logins happened (the second one is case 1e's rotation), so at least two session rows must exist — that is the
# evidence that 1e compared a token against a *rotated* session rather than reusing one.
SESSIONS=$(docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$SCRATCH_DB" -tAc "SELECT count(*) FROM sessions;" | tr -d '[:space:]')
echo "session rows: $SESSIONS (expected >= 2, because case 1e logs in twice)"
[ "${SESSIONS:-0}" -ge 2 ] || fail "only $SESSIONS session row(s): case 1e's rotation did not happen server-side"

printf '\n\033[32m-063 complete: a same-site write without the token was refused and left no row; the same write\033[0m\n'
printf '\033[32mwith it was served; a cross-site write never delivered the cookie and answered 401, not 403.\033[0m\n'
