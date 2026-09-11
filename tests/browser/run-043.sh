#!/usr/bin/env bash
# BEHAVIOR-m3-backend-api-043 driver. Run from the repository root on the host that also runs the API.
#
# WHY A SHELL SCRIPT AND NOT JUST THE .mjs: two of this behaviour's steps are impossible from inside the browser
# container — restarting the browser *is* restarting the container's entrypoint process, and reading the row back with
# `psql` means talking to a different container. Keeping those in the shell also makes the evidence quotable verbatim,
# which is what AC-3's Evidence clause asks for.
#
# Topology (measured, see docs/STATE.md §8 2026-09-11): the sandbox cannot route *into* the Docker network, so the
# harness runs *inside* the container; the container *can* route out to $HOST_IP. Build the bundle with
# VITE_API_BASE_URL=http://$HOST_IP:5080 before starting.
set -euo pipefail

CONTAINER=${CONTAINER:-jt-bridge}
HOST_IP=${HOST_IP:?set HOST_IP to an address the container can reach, e.g. 172.23.124.252}
API=${API:-http://127.0.0.1:5080}          # from *this* shell, the API is on loopback
DB_CONTAINER=${DB_CONTAINER:-api-db-1}
DB_USER=${DB_USER:-jobtracker}
DB_NAME=${DB_NAME:-jobtracker}
MARKER="JT043-$(date +%s)-$$"
NODE_ARGS="--experimental-websocket"        # the image ships Node 20; global WebSocket is flagged there

step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
fail() { printf '\033[31mABORT: %s\033[0m\n' "$*" >&2; exit 1; }

harness() {  # run one phase inside the container; $1 = E2E_PHASE
  docker exec -e E2E_PHASE="$1" -e E2E_MARKER="$MARKER" \
    -e E2E_APP=http://127.0.0.1:4173 -e E2E_API="http://$HOST_IP:5080" \
    "$CONTAINER" node $NODE_ARGS /srv/harness043.mjs
}

psqlq() { docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "$1"; }

step "preconditions"
curl -sf -m 5 "$API/api/applications" >/dev/null || fail "API not reachable at $API (start it with ASPNETCORE_ENVIRONMENT=Development, bound to 0.0.0.0)"
docker inspect -f '{{.State.Running}}' "$CONTAINER" | grep -q true || fail "container $CONTAINER is not running"
docker exec "$CONTAINER" test -f /srv/harness043.mjs || fail "harness not copied into $CONTAINER:/srv/harness043.mjs"
echo "api ok · container $CONTAINER up · marker $MARKER"

step "phase 1 — create the record through the real form, in Chromium"
harness create-then-verify

step "phase 2 — close the browser for real (docker restart: Chromium *is* the entrypoint process)"
docker restart "$CONTAINER" >/dev/null
# The static server was co-located and died with the browser; it must come back before the page can load.
docker exec -d "$CONTAINER" node /srv/static.mjs
sleep 4
echo "container restarted · static server relaunched · Chromium is a new process with a new CDP browser id"

step "phase 3 — reopen a tab in the fresh browser and assert the record is present"
harness verify-visible

step "phase 4 — the row exists in PostgreSQL, independent of every client (AC-3's second clause)"
psqlq "SELECT id, company_name, job_title, status FROM applications WHERE company_name LIKE '%${MARKER}%';"
ROWS=$(psqlq "SELECT count(*) FROM applications WHERE company_name LIKE '%${MARKER}%';" | sed -n '3p' | tr -d ' |')
[ "$ROWS" = "1" ] || fail "expected exactly 1 row for the marker, got '$ROWS'"

step "phase 5 — delete it over HTTP, then confirm the database agrees"
# Two mistakes this block had to earn. (1) A DELETE without `If-Match` is answered **409 conflict** by design — the same
# optimistic-concurrency contract `-033`/`-034` built for PUT — so the revision must be *read*, never guessed: real
# values are Postgres `xmin` counters (783 here, not 1). (2) `curl -f` converts that 409 into exit 22, and under
# `set -e` the script aborts *after* the row exists — leaving it behind to poison the next run, which is precisely the
# hazard `-042` recorded about harnesses that do not clean up. So: no `-f` on the DELETE, and the status is asserted.
PAIR=$(curl -sf -m 5 "$API/api/applications" | python3 -c "
import json,sys
m = sys.argv[1]
hit = [a for a in json.load(sys.stdin) if m in a['companyName']]
print(f\"{hit[0]['id']} {hit[0]['revision']}\" if hit else '')" "$MARKER")
[ -n "$PAIR" ] || fail "the marker record was not found through the API"
ID=${PAIR% *}; REV=${PAIR#* }
echo "found $ID at revision $REV"
code=$(curl -s -m 5 -o /dev/null -w '%{http_code}' -X DELETE "$API/api/applications/$ID" -H "If-Match: \"$REV\"")
echo "DELETE $ID -> $code"
[ "$code" = "204" ] || fail "cleanup did not happen (HTTP $code): this row now outlives the run"
psqlq "SELECT count(*) AS remaining FROM applications WHERE company_name LIKE '%${MARKER}%';"

step "phase 6 — the negative control: a fresh tab must now NOT show it"
harness verify-absent

printf '\n\033[32m-043 complete: created in a browser, survived a browser restart, confirmed by psql, and the check\033[0m'
printf '\n\033[32mproved it can fail.\033[0m\n'
