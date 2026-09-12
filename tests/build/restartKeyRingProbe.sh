#!/usr/bin/env bash
#
# -074 probe — does an antiforgery token survive a **restart** of a *single* instance?
#
# WHY THIS EXISTS AS A SCRIPT AND NOT ONLY AS A TEST. `KeyRingPersistenceTests` covers the same mechanism with two hosts
# against one database, which is the form CI can run. The restart case cannot be expressed as an xunit test: the point of
# it is that the process dies and comes back, and a test that hosts the app in-process has no way to arrange that without
# pretending. This script is therefore the measurement behind `-074`'s central claim, kept runnable because a record that
# cites numbers nobody can re-produce is how this repository already ended up with orphaned measurements once (see
# `STATE.md`'s documentation-integrity ledger).
#
# WHY THE CLAIM WAS WORTH MEASURING AT ALL. `-063`'s code comment said the ephemeral key ring was a deployment concern
# that "single-instance dev cannot see". It is visible here, on 127.0.0.1, in one process at a time, in under a minute —
# and that difference is why a fault that fired on every deploy sat unfixed for two milestones.
#
# WHAT IT PRINTED on 2026-09-13, at `41b32af`-era schema with the -063 gate present and no -074 fix:
#
#     login, write with the issued token                       -> 201
#     restart the process against the same database
#       GET  /api/applications with the pre-restart cookie     -> 200   session survives, it is a row
#       POST /api/applications with the pre-restart token      -> 403   the key does not, it was memory
#       control: fresh login then write on the new process     -> 201   the new process is healthy
#
# and after -074's fix, the same script with two extra lines counting rows in `data_protection_keys`: **201** for the
# replayed token, and **1 / 1** key rows before and after — one row, not two, is what proves the second process *read*
# the stored key rather than minting its own.
#
# NOT A CI JOB. It needs the developer's Postgres container, it creates and drops a scratch database, and it binds a
# local port. It deliberately does not touch the `jobtracker` dev database.

set -uo pipefail

DB=${DB:-jobtracker_p074}                       # scratch database, created empty and dropped on exit
PORT=${PORT:-5099}
URL="http://127.0.0.1:${PORT}"
EMAIL=${EMAIL:-e2e074@example.test}
# Deliberately not a literal: this repository's AC-15 counts credential strings in source, and a dev-only password
# copied into a second file is how a "obviously harmless" value becomes a permanent one. Read it where it is documented
# (api/README.md) and pass it in.
PW=${JT_DEV_PG_PASSWORD:?export JT_DEV_PG_PASSWORD first -- the dev Postgres password, see the compose block in api/README.md}
PASS=${BOOTSTRAP_PASSWORD:?export BOOTSTRAP_PASSWORD first -- the dev bootstrap passphrase, see tests/browser/run-063.sh}
CONN="Host=127.0.0.1;Port=5432;Database=${DB};Username=jobtracker;Password=${PW}"

fail() { printf '\n\033[31mABORT: %s\033[0m\n' "$1" >&2; exit 1; }
trap 'pkill -x JobTracker.Api 2>/dev/null; docker exec api-db-1 psql -U jobtracker -d postgres \
        -c "DROP DATABASE IF EXISTS ${DB} WITH (FORCE);" >/dev/null 2>&1' EXIT

command -v curl >/dev/null || fail "curl is required"
docker inspect api-db-1 >/dev/null 2>&1 || fail "the api-db-1 container is not running; this probe needs the dev Postgres"
[ -f ./api/src/JobTracker.Api/Program.cs ] || fail "run this from the repository root"

psql() { docker exec api-db-1 psql -U jobtracker -d postgres -c "$1" >/dev/null; }
keyrows() { docker exec api-db-1 psql -U jobtracker -d "${DB}" -tAc 'select count(*) from data_protection_keys;' 2>&1 | tr -d '[:space:]'; }

boot() {                                   # `dotnet run` spawns the app as a child; teardown kills it by exact name
  ConnectionStrings__Default="$CONN" ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$URL" \
  Auth__Bootstrap__Email="$EMAIL" Auth__Bootstrap__Password="$PASS" \
  dotnet run --project api/src/JobTracker.Api --no-launch-profile >"$1" 2>&1 &
}
wait_up() {
  for _ in $(seq 60); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' "${URL}/api/applications" || echo 000)" = 401 ] && return 0
    sleep 1
  done
  tail -20 "$1"
  fail "the API never answered 401 at ${URL}; log above"
}
write() {                                   # $1 session cookie, $2 token — a state-changing verb, because login needs
  local id; id=$(cat /proc/sys/kernel/random/uuid)   # no token and would pass for an unrelated reason (-063's lesson)
  curl -s -o /dev/null -w '%{http_code}' -X POST "${URL}/api/applications" \
    -H 'content-type: application/json' -H "Cookie: $1; __Host-JTCsrf=$2" -H "X-CSRF-Token: $2" \
    -d "{\"id\":\"$id\",\"companyName\":\"Vandelay Industries\",\"jobTitle\":\"Import/Export Manager\",\"location\":\"New York\",\"status\":\"Saved\"}"
}

psql "DROP DATABASE IF EXISTS ${DB} WITH (FORCE);"
psql "CREATE DATABASE ${DB};"

echo "── boot #1"
boot /tmp/jt074-boot1.log; wait_up /tmp/jt074-boot1.log
hdr=$(mktemp)
curl -s -D "$hdr" -o /dev/null -X POST "${URL}/api/auth/login" -H 'content-type: application/json' \
  -d "{\"email\":\"${EMAIL}\",\"password\":\"${PASS}\"}"
# `Pair` semantics, not `values.First()`: login sets two cookies and their order is not the contract (-063 again).
S=$(grep -i '^set-cookie: __Host-JTSession=' "$hdr" | sed 's/^[Ss]et-[Cc]ookie: //;s/;.*//')
T=$(grep -i '^set-cookie: __Host-JTCsrf=' "$hdr" | sed 's/^[Ss]et-[Cc]ookie: //;s/;.*//' | sed 's/^__Host-JTCsrf=//')
[ -n "$S" ] && [ -n "$T" ] || fail "login returned no session/token pair; check the boot log"
echo "  write with the issued token            -> $(write "$S" "$T")   (201 expected)"
echo "  key rows in the database               -> $(keyrows)"

echo "── restart: same database, new process"
pkill -x JobTracker.Api 2>/dev/null; sleep 2
boot /tmp/jt074-boot2.log; wait_up /tmp/jt074-boot2.log
echo "  GET with the pre-restart session       -> $(curl -s -o /dev/null -w '%{http_code}' "${URL}/api/applications" -H "Cookie: $S")   (200 = the session survived)"
echo "  POST with the pre-restart token        -> $(write "$S" "$T")   (403 = the ring was ephemeral; 201 = it persisted)"
echo "  key rows after the restart             -> $(keyrows)   (1 = read the stored key; 2 = minted a new one and the pass is meaningless)"

curl -s -D "$hdr" -o /dev/null -X POST "${URL}/api/auth/login" -H 'content-type: application/json' \
  -d "{\"email\":\"${EMAIL}\",\"password\":\"${PASS}\"}"
S2=$(grep -i '^set-cookie: __Host-JTSession=' "$hdr" | sed 's/^[Ss]et-[Cc]ookie: //;s/;.*//')
T2=$(grep -i '^set-cookie: __Host-JTCsrf=' "$hdr" | sed 's/^[Ss]et-[Cc]ookie: //;s/;.*//' | sed 's/^__Host-JTCsrf=//')
echo "  control: fresh login on boot #2        -> $(write "$S2" "$T2")   (201 = this process is healthy, so the row above is about the token)"
rm -f "$hdr"
echo "── teardown (trap): scratch database dropped, app killed by exact name"
