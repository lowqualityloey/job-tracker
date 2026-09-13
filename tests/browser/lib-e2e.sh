#!/usr/bin/env bash
# Shared boot recipe for the M4 real-browser runs (run-063.sh, run-064.sh).
#
# Extracted by TDD-EXEC-m4-authentication-075: both runners previously duplicated the scratch-DB /
# certificate / vite-build / API-boot / poll sequence. One definition, sourced by both.
#
# The sourcing script must, before calling e2e_*, set the run-specific values:
#   SCRATCH_DB, HARNESS_SRC, HARNESS_DST
#   (063 only) CROSS_PORT / CROSS_ORIGIN / CROSS_ROOT / CROSS_PID
# and is responsible for its own steps (cross-origin server, negative control, DB assertions). It also owns
# its own cleanup of any extra background pids (e.g. CROSS_PID) — see e2e_cleanup below.

step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
fail() { printf '\033[31mABORT: %s\033[0m\n' "$*" >&2; exit 1; }

e2e_defaults() {
  CONTAINER=${CONTAINER:-jt-bridge}
  DB_CONTAINER=${DB_CONTAINER:-api-db-1}
  DB_USER=${DB_USER:-jobtracker}
  HOST_IP=${HOST_IP:-$(hostname -I | awk '{print $1}')}
  PORT=${PORT:-5443}
  ORIGIN="https://${HOST_IP}:${PORT}"
  DB_PASSWORD=${DB_PASSWORD:-jobtracker-dev-only}
  BOOTSTRAP_EMAIL=${BOOTSTRAP_EMAIL:-e2e@example.test}
  BOOTSTRAP_PASSWORD=${BOOTSTRAP_PASSWORD:-a-sufficiently-long-dev-passphrase}
  API_PID=''
  CROSS_PID=''
}

# The sourcing script sets SCRATCH_DB / CERT / KEY / SCRATCH_ROOT / CROSS_ROOT before calling this.
e2e_cleanup() {
  # `|| true` on every branch: under `set -e` a cleanup failing for an uninteresting reason would decide the exit
  # code of a run that had already succeeded or already failed for a real one.
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then kill "$API_PID" 2>/dev/null || true; fi
  if [ -n "$CROSS_PID" ] && kill -0 "$CROSS_PID" 2>/dev/null; then kill "$CROSS_PID" 2>/dev/null || true; fi
  pkill -x JobTracker.Api 2>/dev/null || true
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres \
    -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" >/dev/null 2>&1 || true
  rm -f "$CERT" "$KEY" || true
  rm -rf "$SCRATCH_ROOT" "${CROSS_ROOT:-}" || true
}

e2e_preconditions() {
  command -v dotnet >/dev/null || fail "dotnet is not on PATH (source the SDK env first: source /tmp/dotenv.sh)"
  command -v openssl >/dev/null || fail "openssl is not on PATH"
  docker inspect -f '{{.State.Running}}' "$CONTAINER" | grep -q true || fail "$CONTAINER is not running"
  [ -n "$HOST_IP" ] || fail "could not determine a host address the container can route to"
  echo "app origin $ORIGIN · container $CONTAINER · scratch db $SCRATCH_DB"
}

e2e_scratch_db() {
  # A leftover process from an earlier run holds it: `dotnet run` does not forward SIGTERM to the application
  # it launched, so killing the parent leaves the server connected, and `DROP DATABASE` then fails with
  # "being accessed by other users" — measured on the run after this one was aborted. `WITH (FORCE)` (PG 13+) is the
  # difference between a runner that cleans up and one that reports its own predecessor.
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" >/dev/null
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -c "CREATE DATABASE ${SCRATCH_DB};" >/dev/null
  echo "created $SCRATCH_DB"
}

e2e_cert() {
  # Self-signed, in /tmp, valid 2 days, deleted on exit. A second certificate would only add a reason for the
  # run to fail for something unrelated to what it measures.
  openssl req -x509 -newkey rsa:2048 -sha256 -days 2 -nodes \
    -keyout "$KEY" -out "$CERT" -subj "/CN=${HOST_IP}" \
    -addext "subjectAltName=IP:${HOST_IP}" >/dev/null 2>&1 || fail "openssl could not create the certificate"
  chmod 600 "$KEY"
  echo "cert $(basename "$CERT") + key, valid 2 days, SAN IP:${HOST_IP}"
}

e2e_build() {
  # Empty `VITE_API_BASE_URL` would silently select the localStorage adapter and every check below would pass against a
  # fake; the value is the origin under test, stated here rather than inherited. The build goes to an outDir outside the
  # repository so it never overwrites the bundle `-065` measures.
  VITE_API_BASE_URL="$ORIGIN" npx vite build --outDir "$SCRATCH_ROOT" --emptyOutDir > /tmp/vite-e2e.log 2>&1 \
    || { tail -20 /tmp/vite-e2e.log; fail "vite build failed"; }
  test -s "$SCRATCH_ROOT/index.html" || { tail -20 /tmp/vite-e2e.log; fail "no build output at $SCRATCH_ROOT"; }
  echo "bundle in $SCRATCH_ROOT, VITE_API_BASE_URL=$ORIGIN"
}

# Boots the API (Development, TLS, serving the built bundle, on the scratch DB) and waits, polled from *inside* the
# container, for GET /api/applications -> 401. The gating answer is exactly what "up and protected" means.
e2e_boot_api() {
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://0.0.0.0:${PORT}" \
  Kestrel__Certificates__Default__Path="$CERT" \
  Kestrel__Certificates__Default__KeyPath="$KEY" \
  ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=${SCRATCH_DB};Username=${DB_USER};Password=${DB_PASSWORD}" \
  Web__SpaRoot="$SCRATCH_ROOT" \
  Auth__Bootstrap__Email="$BOOTSTRAP_EMAIL" \
  Auth__Bootstrap__Password="$BOOTSTRAP_PASSWORD" \
    dotnet run --project api/src/JobTracker.Api --no-launch-profile > /tmp/api-e2e.log 2>&1 &
  API_PID=$!
  echo "api pid $API_PID; waiting for $ORIGIN to answer from INSIDE the container"
  OK=''
  for _ in $(seq 1 90); do
    code=$(docker exec "$CONTAINER" curl -k -s -m 4 -o /dev/null -w '%{http_code}' "${ORIGIN}/api/applications" || echo 000)
    if [ "$code" = "401" ]; then OK=1; break; fi
    sleep 2
  done
  [ -n "$OK" ] || { tail -25 /tmp/api-e2e.log; fail "API never answered 401 at ${ORIGIN}/api/applications"; }
  echo "GET /api/applications -> 401 (protected, as BEHAVIOR-055 requires)"
}

# Copies the harness (and the shared recipe it imports) into the browser container, then runs it. Extra `docker exec`
# arguments (typically `-e E2E_*` env) are forwarded via "$@". Returns the harness's exit status.
e2e_run_harness() {
  docker cp "$HARNESS_SRC" "$CONTAINER":"$HARNESS_DST" >/dev/null
  docker cp tests/browser/harness.mjs "$CONTAINER":/srv/harness.mjs >/dev/null
  local run_status=0
  # `--experimental-websocket`: the image ships Node 20 and global WebSocket is flagged there (the host's Node 24 does not
  # need it). Without the flag the script dies before CDP and the failure reads as "no browser".
  docker exec "$@" "$CONTAINER" node --experimental-websocket "$HARNESS_DST" 2>&1 | tee /tmp/e2e-run.log || run_status=$?
  echo "run: exit $run_status"
  return $run_status
}
