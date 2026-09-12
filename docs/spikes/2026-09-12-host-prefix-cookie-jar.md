# Spike — Can a `__Host-` session cookie exist in a real browser over plain HTTP?

- **Date**: 2026-09-12 17:37 UTC · **Level**: measurement, no product change
- **Question source**: `TDD-EXEC-m4-authentication-053`'s parked practice task — *"practice-task result pending from the human
  (browser cookie jar over plain HTTP) is **not** a dependency of this behaviour; it gates `-063`/`-064`"* — and
  `DECISION-m4-auth-007` option (a), ratified 51 minutes earlier on the assumption that it was true.
- **Verdict**: **No. `__Host-` requires a cryptographic *scheme*, not a secure *context*.** The difference is invisible to
  every test that exists in this repository, and it falsifies `ASSUMPTION-m4-auth-001`.
- **Browser**: Chromium **128.0.6613.18** (`jt-bridge`, `/ms-playwright/chromium-1129`) — the exact browser `-064` would run
  in. **Re-measure on any image bump**; this is a statement about that build.

## Why this had never been noticed

`-051` proves the cookie **on the response header** (`204` + `Set-Cookie: __Host-JTSession=…; Secure; HttpOnly;
SameSite=Lax; Path=/`, no `Domain=`), and that is the correct seam — a body assertion cannot detect an absent attribute. But a
header assertion can only prove the server *said* it. **Nothing between `-050` and `-063` has ever asked whether a browser
keeps what it was given**, because every test in the suite talks to the app through `WebApplicationFactory` or `HttpClient`,
and neither has a cookie jar. The gap is structural, not sloppy: the first test that could see it is `-064`, and `-064` is
the row this spike was supposed to unblock.

`AuthCatalog.cs:26` holds `public const string SessionCookieName = "__Host-JTSession"`, and the `Secure` in the emitted
`Set-Cookie` (`AuthCatalog.cs:125`, and the clearing form at `:156`) is a literal. **Neither is environment-conditional** —
which is the discipline `ASSUMPTION-001`'s own mitigation column demanded ("dev uses a distinct cookie name → the name
differs between environments, which is exactly how tests drift from production"). So the finding below applies to the shipped
cookie exactly as written, not to a dev-only variant.

## Method

Four probes, each a plain Node server with a handler that sets cookies (`/set`) and one that **echoes the `Cookie` request
header back** (`/echo`) — so the verdict is *what the server received*, not what JavaScript believed. Driven by real Chromium
over CDP (`Page.navigate` → `fetch('/set')` → `fetch('/echo')`), from inside `jt-bridge`.

The variable is isolated three times over: **identical attributes, only the prefix changes**; **identical prefix, only the
scheme changes**; **identical everything, only the address form changes** (`127.0.0.1` / `localhost` / `172.17.0.3`).

```
docker exec -e PROBE_HOST_IP=172.17.0.3 jt-bridge node --experimental-websocket /srv/probeN.mjs
```

## Results

`Set-Cookie: NAME=1; Secure; HttpOnly; Path=/; SameSite=Lax` — accepted means the second request carried it.

| Origin | `isSecureContext` | `JTPlain` (Secure) | `__Secure-JTProbe` | `__Host-JTSession` |
| :--- | :--- | :--- | :--- | :--- |
| `http://127.0.0.1` | **true** | **accepted** | refused | **refused** |
| `http://localhost` | **true** | **accepted** | refused | **refused** |
| `http://172.17.0.3` | false | refused | refused | refused |
| `https://127.0.0.1` (self-signed) | true | **accepted** | — | **accepted** |
| `https://localhost` (self-signed) | true | **accepted** | — | **accepted** |

**Read the first two columns together and the finding is exact**: at `http://127.0.0.1` the browser reports
`isSecureContext === true` **and** stores a `Secure` cookie, yet **discards the `__Host-` one**. So the two checks are not
the same check —

- the `Secure` **attribute** is gated on *secure context*, and Chromium's loopback allowance makes `http://127.0.0.1` one;
- the `__Host-` / `__Secure-` **prefix** is gated on the **scheme being cryptographic**, and loopback gets no allowance there.

That is why `ASSUMPTION-m4-auth-001`'s first half is true and its conclusion is false. It reasons from "localhost is a secure
context, so a `Secure` cookie is settable" to "so `__Host-` works in dev without TLS" — **the prefix never inherits the
context allowance, because it was written to be stricter than it.**

## What it invalidates

1. **`ASSUMPTION-m4-auth-001` is false**, and its stated mitigation is the one option that remains closed: the app must not
   rename the cookie per environment, so "dev uses `__JTSession`" is not available without breaking the invariant AC-2 pins.
2. **`DECISION-m4-auth-007` option (a) does not work as ratified.** Serving the harness from the API's origin over
   `http://127.0.0.1:5080` fixes the *site* problem (no preflight, cookie same-site) and replaces it with a worse one: in the
   first probe the cookie was **never stored at all**, so an authenticated browser run would fail every request with `401`
   and look like a broken login. Same-origin was necessary; it is not sufficient.
3. **Option (c) was never a candidate, and this proves it.** `SameSite=None; Secure` addresses cross-*site* sending. The
   failure measured here is *storage*, on a request that is not cross-site at all. (c) would have changed nothing.
4. **The current dev setup cannot log in from a browser.** `launchSettings.json`'s default `http` profile is
   `http://localhost:5039`, and the `https` profile exists but is not the one `dotnet run` picks without `--launch-profile`.
   Over the http profile, login answers `204` — and Chromium throws the cookie away. **Unverified in the real app** (see
   "Not measured"), but the app emits the same shape the probe emitted.

## The surviving options

| | Shape | Cost |
| :--- | :--- | :--- |
| **(a′)** | same-origin **and HTTPS in dev**: `dotnet dev-certs https` (trusted) or the API's own `https` profile, harness at `https://127.0.0.1:<port>` | Keeps `__Host-JTSession` byte-identical to production — the property AC-2 exists to pin. One dev-cert step, and the CDP harness must either trust it or call `Security.setIgnoreCertificateErrors`, which is a **harness** concession, not a product one, and must be disclosed wherever the run is quoted |
| **(b′)** | Option (b)'s reverse proxy, but **terminating TLS** in front of the API | Same cert question plus a moving part existing only in Development |
| **(d)** | Ship AC-11/AC-12/AC-14 unverified, documented | Honest, and it is what the ladder has been doing all along |
| ~~(a)~~ | same-origin over plain http | **Closed by the measurement above** |
| ~~(c)~~ | `SameSite=None; Secure` in dev | **Closed, and closed for an unrelated failure mode** |

**Recommendation: (a′).** It is the only row of the table that leaves the shipped cookie attributes untouched, which is the
thing this milestone was built to get right. The cert concession belongs in the harness and must be visible in the record.

## Harness findings, because three of them nearly became product findings

1. **The container's Node 20 has no global `WebSocket`.** `node --experimental-websocket` is required
   (`typeof WebSocket` → `undefined` bare, `function` flagged). The host runs Node 24, where it is global, so the same
   harness file works on one and dies on the other. **The first failure looked like "no CDP"** because my `catch {}` swallowed
   it — handoff §4.1's "a broken harness looks like a broken product", met again, this time in code I wrote five minutes in.
2. **The cookie jar persists across targets.** Run 2's HTTPS echo began
   `HOST_SEC=1; HOST_NOSEC=1; PLAIN_SEC=1; JTPlain=1; …` — leftovers from the earlier probe's tabs. A `-064` run must call
   `Storage.clearCookies` (or use a fresh browser context) **per case**, or a stale session reads as a working one. That is
   the single most dangerous shape this spike found: it fails *green*.
3. **`Security.setPermission` does not exist on this build** (`-32601`); `Security.setIgnoreCertificateErrors` does. Assert
   the CDP method exists rather than assuming the spec you read matches the browser you have.
4. **Two of my own process faults, in the same hour.** `docker exec … | tail` reported **`PROBE_EXIT=0` for a probe that had
   crashed** — the `pipefail` rule from `AGENTS.md`, now broken twice in one session, once against the real test suite and
   once against this spike; every command after that redirected to a file and read `$?`. And the first TLS probe died on
   `ENOENT /srv/k.pem` because `docker cp` had copied the script and not the key material — a missing-file error, correctly
   loud, because nothing was piped.

## Not measured

- **The real `/api/auth/login` endpoint in Chromium.** The probes used a synthetic server emitting the same `Set-Cookie`
  string. The conclusion about the *prefix rule* does not depend on the app, but "**dev login is broken over http**" is
  inference from an identical wire shape and is labelled as such.
- **Any Chromium newer than 128.** Loopback handling of `__Host-` could change; the image pin is what makes this a
  reproducible claim, and the re-measurement instruction is in the table above.
- **Whether `dotnet dev-certs https --trust` works in this sandbox** (the container cannot route into the Docker network, and
  trust is per-user on the host). That is the first thing (a′) would have to establish, before any `-064` code is written.

## Reproduce

```bash
# 1. the prefix/address matrix — needs no certificates
docker cp docs/spikes/2026-09-12-host-prefix-cookie-jar/probes/probe3-address-forms.mjs jt-bridge:/srv/
docker exec -e PROBE_HOST_IP=172.17.0.3 jt-bridge node --experimental-websocket /srv/probe3-address-forms.mjs > /tmp/p3.out 2>&1
echo $?                                                       # read the exit code, never `| tail`'s

# 2. the HTTPS case — mint a THROWAWAY cert first (two days, CN=127.0.0.1, meaningless outside this host)
openssl req -x509 -newkey rsa:2048 -keyout /tmp/k.pem -out /tmp/c.pem -days 2 -nodes \
  -subj "/CN=127.0.0.1" -addext "subjectAltName=IP:127.0.0.1,DNS:localhost"
docker cp /tmp/k.pem jt-bridge:/srv/k.pem && docker cp /tmp/c.pem jt-bridge:/srv/c.pem
docker cp docs/spikes/2026-09-12-host-prefix-cookie-jar/probes/probe4-https.mjs jt-bridge:/srv/
docker exec jt-bridge node --experimental-websocket /srv/probe4-https.mjs > /tmp/p4.out 2>&1; echo $?
```

**Probe sources are filed beside this record under `probes/`. No key material is committed** — the HTTPS probe reads
`/srv/k.pem` and `/srv/c.pem` from inside the container, and the two commands above mint them. A throwaway self-signed key is
still a private key, and `docs/` is the wrong place to learn that distinction under a secret scanner.

`--experimental-websocket` is **required** (finding 1 above); without it Node 20 in the image has no `WebSocket` and the
probe dies reporting what looks like an unreachable browser.
