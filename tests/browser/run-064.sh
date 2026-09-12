#!/usr/bin/env bash
# BEHAVIOR-m4-authentication-064 driver: `httpCrossTab.mjs` -> 7/7 **while authenticated**, in real Chromium.
#
# Run from the repository root, on the host that also runs the API.
#
# WHY A SEPARATE RUNNER FROM run-043.sh
# `-043` could not do any of its steps from inside the browser container (restarting the browser restarts the
# container's entrypoint) and needed `psql` in a second container. This one has a different reason: the thing under test
# is a **TLS origin**, and the three parties that must agree about it live in different namespaces —
#   · the **API** runs on the host, because that is where `dotnet` and the certificate key are;
#   · the **harness** runs inside `$CONTAINER`, because the host cannot reach Chromium's DevTools socket even though the
#     port is published — measured, not assumed: `curl http://127.0.0.1:9222/json/version` from the host exits **52**
#     (empty reply), while the same call inside the container returns the version document;
#   · the **database** is a scratch one in `$DB_CONTAINER`, dropped on the way out.
# So the script is the only place all three are visible, and it is where the ordering constraints get enforced.
#
# THE TOPOLOGY IS THE POINT, NOT THE PLUMBING
# Page and API are served from **one origin over TLS** (`DECISION-m4-auth-007 (a′)`, implemented by `-071`). Two origins
# cannot be authenticated at all here: `SameSite=Lax` (DECISION-006) will not carry `__Host-JTSession` on a cross-origin
# subresource request, and plain `http` makes Chromium discard the prefix outright. `VITE_API_BASE_URL` therefore has to
# be **this origin** — left empty, `applicationStore.ts` selects the localStorage adapter and the run would be seven
# checks against a fake. A green harness built that way is the specific false-green `-071`'s record names.
set -euo pipefail

CONTAINER=${CONTAINER:-jt-bridge}
DB_CONTAINER=${DB_CONTAINER:-api-db-1}
DB_USER=${DB_USER:-jobtracker}
HOST_IP=${HOST_IP:-$(hostname -I | awk '{print $1}')}
PORT=${PORT:-5443}
ORIGIN="https://${HOST_IP}:${PORT}"
# A scratch database, never the shared dev one: this row logs in, creates records and deletes them, and the dev
# database has hand-made rows in it that predate the session and are not mine to remove.
SCRATCH_DB=${SCRATCH_DB:-jobtracker_e2e064}
SCRATCH_ROOT=/tmp/jt-e2e064-dist
# The dev postgres password, which is already committed in `api/README.md` as part of the local connection string and is
# not a secret — it authenticates a container that binds to loopback on a laptop.
DB_PASSWORD=${DB_PASSWORD:-jobtracker-dev-only}
BOOTSTRAP_EMAIL=${BOOTSTRAP_EMAIL:-e2e064@example.test}
BOOTSTRAP_PASSWORD=${BOOTSTRAP_PASSWORD:-a-sufficiently-long-dev-passphrase}
HARNESS_SRC=tests/browser/httpCrossTab.mjs
HARNESS_DST=/srv/httpCrossTab.mjs
CERT=/tmp/jt-e2e064-cert.pem
KEY=/tmp/jt-e2e064-key.pem
API_PID=''

step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
fail() { printf '\033[31mABORT: %s\033[0m\n' "$*" >&2; exit 1; }

cleanup() {
  # Every branch `|| true`: under `set -e` a cleanup that fails for an uninteresting reason (already dead, already
  # dropped) would mask the real result of the run above it by deciding the script's exit code.
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then kill "$API_PID" 2>/dev/null || true; fi
  pkill -x JobTracker.Api 2>/dev/null || true
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres \
    -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" >/dev/null 2>&1 || true
  rm -f "$CERT" "$KEY" || true
}
trap cleanup EXIT

step "preconditions"
command -v dotnet >/dev/null || fail "dotnet is not on PATH (source the SDK env first)"
command -v openssl >/dev/null || fail "openssl is not on PATH"
docker inspect -f '{{.State.Running}}' "$CONTAINER" | grep -q true || fail "$CONTAINER is not running"
docker exec "$CONTAINER" test -x /ms-playwright/chromium-1129/chrome-linux/chrome \
  || fail "Chromium is not present in $CONTAINER at the path the spike measured"
[ -n "$HOST_IP" ] || fail "could not determine a host address the container can route to"
echo "origin $ORIGIN · container $CONTAINER · scratch db $SCRATCH_DB · bootstrap user $BOOTSTRAP_EMAIL"

step "1 — scratch database (created empty; EF migrates it on boot)"
# A leftover process from an earlier run holds it: `dotnet run` does not forward SIGTERM to the application
# it launched, so killing the parent leaves the server connected, and `DROP DATABASE` then fails with
# "being accessed by other users" — measured on the run after this one was aborted. `WITH (FORCE)` (PG 13+) is the
# difference between a runner that cleans up and one that reports its own predecessor.
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" >/dev/null
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -c "CREATE DATABASE ${SCRATCH_DB};" >/dev/null
echo "created $SCRATCH_DB"

step "2 — self-signed certificate for $HOST_IP (temporary, in /tmp, never in the repository)"
openssl req -x509 -newkey rsa:2048 -sha256 -days 2 -nodes \
  -keyout "$KEY" -out "$CERT" -subj "/CN=${HOST_IP}" \
  -addext "subjectAltName=IP:${HOST_IP}" >/dev/null 2>&1 || fail "openssl could not create the certificate"
chmod 600 "$KEY"
echo "cert $(basename "$CERT") + key, valid 2 days, SAN IP:${HOST_IP}"

step "3 — build the front end pointed AT THIS ORIGIN"
# Absolute --outDir outside the repository: the shipped `dist/` is `-065`'s measurement subject and must not be
# overwritten by a harness build, which would silently change what the bundle-size row measures.
VITE_API_BASE_URL="$ORIGIN" npx vite build --outDir "$SCRATCH_ROOT" --emptyOutDir > /tmp/vite-e2e064.log 2>&1 \
  || { tail -20 /tmp/vite-e2e064.log; fail "vite build failed"; }
test -s "$SCRATCH_ROOT/index.html" || { tail -20 /tmp/vite-e2e064.log; fail "no build output at $SCRATCH_ROOT"; }
echo "bundle in $SCRATCH_ROOT, VITE_API_BASE_URL=$ORIGIN"

step "4 — boot the API: Development, TLS, serving that bundle, on the scratch database"
# `--no-launch-profile` so nothing silently substitutes the http profile; the URL, the certificate and the SPA root are
# all stated here rather than inherited, because what this row measures is *this* configuration.
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://0.0.0.0:${PORT}" \
Kestrel__Certificates__Default__Path="$CERT" \
Kestrel__Certificates__Default__KeyPath="$KEY" \
ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=${SCRATCH_DB};Username=${DB_USER};Password=${DB_PASSWORD}" \
Web__SpaRoot="$SCRATCH_ROOT" \
Auth__Bootstrap__Email="$BOOTSTRAP_EMAIL" \
Auth__Bootstrap__Password="$BOOTSTRAP_PASSWORD" \
  dotnet run --project api/src/JobTracker.Api --no-launch-profile > /tmp/api-e2e064.log 2>&1 &
API_PID=$!
echo "api pid $API_PID; waiting for $ORIGIN to answer from INSIDE the container"
# Polled from inside `$CONTAINER`, not here: a 200 from the host would prove nothing about the only route that matters,
# which is browser-container → host. `-k` because the certificate is self-signed.
OK=''
for _ in $(seq 1 90); do
  code=$(docker exec "$CONTAINER" curl -k -s -m 4 -o /dev/null -w '%{http_code}' "${ORIGIN}/api/applications" || echo 000)
  if [ "$code" = "401" ]; then OK=1; break; fi   # the gate answering is exactly what "up and protected" means
  sleep 2
done
[ -n "$OK" ] || { tail -25 /tmp/api-e2e064.log; fail "API never answered 401 at ${ORIGIN}/api/applications"; }
echo "GET /api/applications -> 401 (protected, as BEHAVIOR-055 requires)"
SHELL_CODE=$(docker exec "$CONTAINER" curl -k -s -m 4 -o /dev/null -w '%{http_code}' "${ORIGIN}/applications" || echo 000)
[ "$SHELL_CODE" = "200" ] || fail "${ORIGIN}/applications returned $SHELL_CODE, not 200 — -071's fallback is not serving"
echo "GET /applications -> 200 (the -071 shell fallback, over TLS, from the browser's network namespace)"

step "5 — copy the harness in and run it authenticated"
docker cp "$HARNESS_SRC" "$CONTAINER":"$HARNESS_DST" >/dev/null
# `--experimental-websocket`: the image ships Node 20 and global WebSocket is flagged there (the host's Node 24 does not
# need it). Without the flag the script dies before CDP and the failure reads as "no browser".
run_status=0
docker exec -e E2E_NO_SPAWN=1 -e E2E_APP="$ORIGIN" \
  -e E2E_LOGIN_EMAIL="$BOOTSTRAP_EMAIL" -e E2E_LOGIN_PASSWORD="$BOOTSTRAP_PASSWORD" \
  "$CONTAINER" node --experimental-websocket "$HARNESS_DST" 2>&1 | tee /tmp/e2e064-run.log || run_status=$?
# `|| status=$?` rather than `${PIPESTATUS[0]}`: `pipefail` is on, so the pipeline already carries the harness's exit
# code and the bare `PIPESTATUS` line below it was unreachable code pretending to report a result.
echo "authenticated run: exit $run_status"
[ "$run_status" = 0 ] || { grep -E '^(PASS|FAIL|[0-9]+/[0-9]+)' /tmp/e2e064-run.log || true; fail "-064's own run failed (exit $run_status)"; }

step "6 — negative control: the same seven checks with no session must NOT pass"
# Without this, "7/7 while authenticated" cannot be distinguished from "7/7, and authentication was never in the path".
# A control that passes is reported as a failure of the *row*, because it means the checks do not bite.
if docker exec -e E2E_NO_SPAWN=1 -e E2E_APP="$ORIGIN" -e E2E_SKIP_LOGIN=1 \
     -e E2E_LOGIN_EMAIL="$BOOTSTRAP_EMAIL" -e E2E_LOGIN_PASSWORD="$BOOTSTRAP_PASSWORD" \
     "$CONTAINER" node --experimental-websocket "$HARNESS_DST" > /tmp/e2e064-control.log 2>&1; then
  tail -20 /tmp/e2e064-control.log
  fail "negative control PASSED — the checks do not require a session, so the run above proves nothing"
fi
echo "negative control failed as it must: $(grep -cE '^FAIL' /tmp/e2e064-control.log) of the checks did not pass"

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
