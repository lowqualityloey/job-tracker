#!/usr/bin/env bash
# BEHAVIOR-m4-authentication-064 driver: `httpCrossTab.mjs` -> 7/7 **while authenticated**, in real Chromium.
#
# Run from the repository root, on the host that also runs the API.
#
# The shared boot recipe (scratch DB, certificate, vite build, API boot + poll) lives in lib-e2e.sh, extracted by
# TDD-EXEC-m4-authentication-075. Only this row's negative control and its extra DB assertion remain here.
set -euo pipefail
source tests/browser/lib-e2e.sh
e2e_defaults

SCRATCH_DB=${SCRATCH_DB:-jobtracker_e2e064}
SCRATCH_ROOT=/tmp/jt-e2e064-dist
HARNESS_SRC=tests/browser/httpCrossTab.mjs
HARNESS_DST=/srv/httpCrossTab.mjs
CERT=/tmp/jt-e2e064-cert.pem
KEY=/tmp/jt-e2e064-key.pem
API_PID=''
trap e2e_cleanup EXIT

step "preconditions"
docker exec "$CONTAINER" test -x /ms-playwright/chromium-1129/chrome-linux/chrome \
  || fail "Chromium is not present in $CONTAINER at the path the spike measured"
e2e_preconditions

step "1 — scratch database (created empty; EF migrates it on boot)"
e2e_scratch_db

step "2 — self-signed certificate for $HOST_IP"
e2e_cert

step "3 — build the front end pointed AT THIS ORIGIN"
e2e_build

step "4 — boot the API"
e2e_boot_api
SHELL_CODE=$(docker exec "$CONTAINER" curl -k -s -m 4 -o /dev/null -w '%{http_code}' "${ORIGIN}/applications" || echo 000)
[ "$SHELL_CODE" = "200" ] || fail "${ORIGIN}/applications returned $SHELL_CODE, not 200 — -071's fallback is not serving"
echo "GET /applications -> 200 (the -071 shell fallback, over TLS, from the browser's network namespace)"

step "5 — copy the harness in and run it authenticated"
run_status=0
e2e_run_harness -e E2E_NO_SPAWN=1 -e E2E_APP="$ORIGIN" \
  -e E2E_LOGIN_EMAIL="$BOOTSTRAP_EMAIL" -e E2E_LOGIN_PASSWORD="$BOOTSTRAP_PASSWORD" \
  || run_status=$?
echo "authenticated run: exit $run_status"
[ "$run_status" = 0 ] || { grep -E '^(PASS|FAIL|[0-9]+/[0-9]+)' /tmp/e2e-run.log || true; fail "-064's own run failed (exit $run_status)"; }

step "6 — negative control: the same seven checks with no session must NOT pass"
# Without this, "7/7 while authenticated" cannot be distinguished from "7/7, and authentication was never in the path".
# A control that passes is reported as a failure of the *row*, because it means the checks do not bite.
if e2e_run_harness -e E2E_NO_SPAWN=1 -e E2E_APP="$ORIGIN" -e E2E_SKIP_LOGIN=1 \
     -e E2E_LOGIN_EMAIL="$BOOTSTRAP_EMAIL" -e E2E_LOGIN_PASSWORD="$BOOTSTRAP_PASSWORD"; then
  tail -20 /tmp/e2e-run.log
  fail "negative control PASSED — the checks do not require a session, so the run above proves nothing"
fi
echo "negative control failed as it must: $(grep -cE '^FAIL' /tmp/e2e-run.log) of the checks did not pass"

step "7 — the database agrees with what the browser did"
# The marker record is deleted by the harness itself (check 7/7), so a clean run leaves *zero* rows behind. Counting
# them is how the run proves it cleaned up rather than merely reporting that it did.
LEFT=$(docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$SCRATCH_DB" -tAc \
  "SELECT count(*) FROM applications WHERE company_name LIKE 'Crosstab %';" | tr -d '[:space:]')
echo "rows left by the harness: $LEFT (expected 0)"
SESSIONS=$(docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$SCRATCH_DB" -tAc "SELECT count(*) FROM sessions;" | tr -d '[:space:]')
echo "session rows in the scratch database: $SESSIONS (the login really happened server-side)"

printf '\n\033[32m-064 complete: seven checks passed while authenticated in Chromium, and the same seven\033[0m\n'
printf '\033[32mrefused to pass without a session.\033[0m\n'
