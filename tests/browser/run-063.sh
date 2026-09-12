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
# WHY THE GATE CANNOT BE PROVEN WITHOUT A BROWSER
# The API cases in `AntiforgeryGateTests` assert the server's answer. What only a browser can show is the *premise*:
# that a page on another origin cannot read the token and cannot make the cookie arrive. Both halves are decided by
# the platform, not by this repository, and a unit test can only restate them as an assumption.
#
# The TLS origin, the scratch database, the `VITE_API_BASE_URL` requirement and the container topology are inherited
# from `run-064.sh` unchanged — including its comments, which is duplication on purpose while `-064`'s evidence still
# names that file. `-075` is the row that unifies them; by the time `-065` lands there will be three copies.
set -euo pipefail

CONTAINER=${CONTAINER:-jt-bridge}
DB_CONTAINER=${DB_CONTAINER:-api-db-1}
DB_USER=${DB_USER:-jobtracker}
HOST_IP=${HOST_IP:-$(hostname -I | awk '{print $1}')}
PORT=${PORT:-5443}
# 4179, not 4173: the first run aborted with `Address already in use` because 4173 is vite's `preview` default and
# something on this machine holds it on loopback. The message that follows names the port rather than the mystery.
CROSS_PORT=${CROSS_PORT:-4179}
ORIGIN="https://${HOST_IP}:${PORT}"
CROSS_ORIGIN="http://${HOST_IP}:${CROSS_PORT}"
SCRATCH_DB=${SCRATCH_DB:-jobtracker_e2e063}
SCRATCH_ROOT=/tmp/jt-e2e063-dist
CROSS_ROOT=/tmp/jt-e2e063-cross
DB_PASSWORD=${DB_PASSWORD:-jobtracker-dev-only}
BOOTSTRAP_EMAIL=${BOOTSTRAP_EMAIL:-e2e063@example.test}
BOOTSTRAP_PASSWORD=${BOOTSTRAP_PASSWORD:-a-sufficiently-long-dev-passphrase}
HARNESS_SRC=tests/browser/csrfGate.mjs
HARNESS_DST=/srv/csrfGate.mjs
CERT=/tmp/jt-e2e063-cert.pem
KEY=/tmp/jt-e2e063-key.pem
API_PID=''
CROSS_PID=''

step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
fail() { printf '\033[31mABORT: %s\033[0m\n' "$*" >&2; exit 1; }

cleanup() {
  # `|| true` on every branch: under `set -e` a cleanup failing for an uninteresting reason would decide the exit
  # code of a run that had already succeeded or already failed for a real one.
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then kill "$API_PID" 2>/dev/null || true; fi
  if [ -n "$CROSS_PID" ] && kill -0 "$CROSS_PID" 2>/dev/null; then kill "$CROSS_PID" 2>/dev/null || true; fi
  pkill -x JobTracker.Api 2>/dev/null || true
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres \
    -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" >/dev/null 2>&1 || true
  rm -f "$CERT" "$KEY" || true
  rm -rf "$SCRATCH_ROOT" "$CROSS_ROOT" || true
}
trap cleanup EXIT

step "preconditions"
command -v dotnet >/dev/null || fail "dotnet is not on PATH (source the SDK env first)"
command -v openssl >/dev/null || fail "openssl is not on PATH"
command -v python3 >/dev/null || fail "python3 is needed for the second origin"
docker inspect -f '{{.State.Running}}' "$CONTAINER" | grep -q true || fail "$CONTAINER is not running"
[ -n "$HOST_IP" ] || fail "could not determine a host address the container can route to"
echo "app origin $ORIGIN · cross-site $CROSS_ORIGIN · container $CONTAINER · scratch db $SCRATCH_DB"

step "1 — scratch database"
# A leftover process from an earlier run holds it: `dotnet run` does not forward SIGTERM to the application
# it launched, so killing the parent leaves the server connected, and `DROP DATABASE` then fails with
# "being accessed by other users" — measured on the run after this one was aborted. `WITH (FORCE)` (PG 13+) is the
# difference between a runner that cleans up and one that reports its own predecessor.
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" >/dev/null
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -c "CREATE DATABASE ${SCRATCH_DB};" >/dev/null
echo "created $SCRATCH_DB"

step "2 — self-signed certificate for $HOST_IP (in /tmp, valid 2 days, deleted on exit)"
openssl req -x509 -newkey rsa:2048 -sha256 -days 2 -nodes \
  -keyout "$KEY" -out "$CERT" -subj "/CN=${HOST_IP}" \
  -addext "subjectAltName=IP:${HOST_IP}" >/dev/null 2>&1 || fail "openssl could not create the certificate"
chmod 600 "$KEY"

step "3 — build the front end pointed AT THE APP ORIGIN"
# Empty `VITE_API_BASE_URL` would silently select the localStorage adapter and every check below would pass against a
# fake; the value is the origin under test, stated here rather than inherited.
VITE_API_BASE_URL="$ORIGIN" npx vite build --outDir "$SCRATCH_ROOT" --emptyOutDir > /tmp/vite-e2e063.log 2>&1 \
  || { tail -20 /tmp/vite-e2e063.log; fail "vite build failed"; }
test -s "$SCRATCH_ROOT/index.html" || fail "no build output at $SCRATCH_ROOT"

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

step "5 — boot the API: Development, TLS, serving the bundle, on the scratch database"
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://0.0.0.0:${PORT}" \
Kestrel__Certificates__Default__Path="$CERT" \
Kestrel__Certificates__Default__KeyPath="$KEY" \
ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=${SCRATCH_DB};Username=${DB_USER};Password=${DB_PASSWORD}" \
Web__SpaRoot="$SCRATCH_ROOT" \
Auth__Bootstrap__Email="$BOOTSTRAP_EMAIL" \
Auth__Bootstrap__Password="$BOOTSTRAP_PASSWORD" \
  dotnet run --project api/src/JobTracker.Api --no-launch-profile > /tmp/api-e2e063.log 2>&1 &
API_PID=$!
echo "api pid $API_PID; waiting for $ORIGIN from INSIDE the container"
OK=''
for _ in $(seq 1 90); do
  code=$(docker exec "$CONTAINER" curl -k -s -m 4 -o /dev/null -w '%{http_code}' "${ORIGIN}/api/applications" || echo 000)
  if [ "$code" = "401" ]; then OK=1; break; fi
  sleep 2
done
[ -n "$OK" ] || { tail -25 /tmp/api-e2e063.log; fail "API never answered 401 at ${ORIGIN}/api/applications"; }
echo "GET /api/applications -> 401 (the session gate, which runs before the antiforgery gate)"

# The two origins must be mutually reachable from the browser's namespace, or case 2 measures a network failure.
CROSS_CODE=$(docker exec "$CONTAINER" curl -s -m 4 -o /dev/null -w '%{http_code}' "${CROSS_ORIGIN}/probe.html" || echo 000)
[ "$CROSS_CODE" = "200" ] || fail "${CROSS_ORIGIN}/probe.html returned $CROSS_CODE from inside $CONTAINER"
echo "GET ${CROSS_ORIGIN}/probe.html -> 200 from inside the container (case 2 has somewhere to run)"

step "6 — the two cases"
docker cp "$HARNESS_SRC" "$CONTAINER":"$HARNESS_DST" >/dev/null
run_status=0
docker exec -e E2E_APP="$ORIGIN" -e E2E_CROSS="$CROSS_ORIGIN" \
  -e E2E_LOGIN_EMAIL="$BOOTSTRAP_EMAIL" -e E2E_LOGIN_PASSWORD="$BOOTSTRAP_PASSWORD" \
  "$CONTAINER" node --experimental-websocket "$HARNESS_DST" 2>&1 | tee /tmp/e2e063-run.log || run_status=$?
echo "run: exit $run_status"
[ "$run_status" = 0 ] || { grep -E '^(PASS|FAIL|HARNESS)' /tmp/e2e063-run.log || true; fail "-063's browser cases failed (exit $run_status)"; }

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
