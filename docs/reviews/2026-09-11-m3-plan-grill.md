# Design Grill — `PLAN-m3-backend-api` (pre-implementation interrogation)

- **Record**: `GRILL-m3-backend-api` · **Date**: 2026-09-11 03:50 UTC (measured with `date -u`; the first value I wrote, 03:52, was from intent and is
> two minutes in the future — the seventh commit-discipline rule applies to documents, so it is corrected in place)
- **Subject**: [`PLAN-m3-backend-api`](../specs/2026-09-11-spec-m3-backend-api.md#PLAN-m3-backend-api) as approved by PR #7
- **Gate it satisfies**: `pk:plan` Related Workflows — *"Stress-test your architecture before implementation (required for complex Level 2 work)"*; and `TASK-m3-backend-api` §5's single Next Action
- **Verdict**: **Proceed to `pk:test`**, with **two conditions** (F-1, F-7) and **four amendments to the plan** (F-2, F-3, F-4, F-5) recorded below. Nothing in this document changes an approved *decision*; two changes alter the *contract*, and both are flagged for the owner's eye.

> **A limitation of this grill, stated up front rather than hidden in a conclusion:** `.promptkit/workflows/grill.md`
> **does not exist** — `pk:grill` is advertised in the trigger list but the vendored submodule ships 20 workflow
> files and none of them is grill (see F-7). There is therefore **no external rubric** here, and a design
> interrogated by the mind that designed it is weaker than one interrogated by a stranger. The countermeasure used
> was to attack each decision for the outcome that would *embarrass it in a review of finished code*, and to record
> every question that the plan survives. The countermeasure that would be better is you — see §Open questions.

---

## 1. Interrogation

### Q1 — "You wrote AC-12 as if it were one test. It is two languages and two test stacks. How does one command prove that `src/domain/validation.ts` and the C# validator agree?"

**This is the strongest objection available, and the plan had no answer.** `dotnet test` cannot import TypeScript;
`vitest` cannot execute C#. A claim that "both validators are checked against the same boundaries" with no
mechanism is a wish written as an acceptance criterion — and AC-12 would have been marked green by two
independent test files that *happen* to encode different opinions, which is exactly the failure mode the AC exists
to prevent.

> **CORRECTED — read §4 before relying on this paragraph.** Reading `src/domain/validation.ts` while writing the test
> plan showed the claim below is **wrong in part**: `MAX_TEXT_LENGTH = 120` *is* enforced on `companyName`,
> `jobTitle` and `location`. The original wording is preserved, not rewritten.

- **Discovery during the grill**: the frontend's own rules are **not** length-based for `location`/`notes` — the
  domain type is optional-or-non-empty (invariant 3: `""` fails, `"   "` passes). So "max length" boundaries in
  AC-12 would have tested a rule **that does not exist**, and a server limit of 500 would be a server-only
  invention — meaning a record a user can save locally becomes unsaveable over HTTP. **That divergence is a
  behavioural bug the plan was about to ship, found by asking one question.**
- **Disposition — CONDITION F-1**: the shared source of truth is a **fixture file**, not a clever test.
  `api/tests/fixtures/validation-cases.json` holds `{ name, input, expect: "valid" | { field: code } }`;
  the xUnit theory consumes it, and a vitest case consumes the same file (checked in **one** place, read by
  both stacks). The server may add *extra* rules (length ceilings) only where the client is silent, and each such
  rule must appear in the fixture with an explicit case — so "the server is stricter in this way" is written down
  rather than discovered by a user.
- **Status**: **blocking for Slice 2** — Slice 1 may proceed; Slice 2 must not start on an underspecified AC-12.

### Q2 — "Your §2 lists `p50 < 30 ms`, `p95 < 80 ms`, SSE observed `< 500 ms`. What command measures them, and on what hardware?"

None. There is no load generator in the repository, no harness planned, and adding one (`k6`, `hey`, NBomber, a
BenchmarkDotNet suite) to a five-row app would be theatre with a dependency attached. The numbers *look* like
acceptance criteria and are therefore more dangerous than an honest absence: a future reader could believe the API
was measured.

- **Disposition — AMENDMENT F-2**: the §2 latencies are **demoted from targets to observations**. Slice 4 records
  whatever the acceptance run actually produces on this machine — `curl -w '%{time_total}'` samples are enough for
  a dev-mode anecdote, and no AC asserts a latency. The plan's own sign-off line said *"if any cannot be checked
  by a command, it is deleted rather than defended"*; this is that clause being honoured instead of argued past.
- **Kept, because it is real**: the frontend constraint that the HTTP adapter must **not** grow the runtime
  bundle beyond the `fetch` baseline (the build output already prints the number, so this one *is* checkable).

### Q3 — "`PUT` requires `If-Match`. `DELETE` says it is *optional*. Why is destroying a record the only write you let a stale tab perform blind?"

It shouldn't be. The FMEA row that motivated concurrency is *two tabs, same record* — and the destructive version of
that scenario is tab A deleting a record tab B is editing, which `localStorage` also could not express, and
which "optional" reduces to last-request-wins on an **irreversible** operation.

- **Disposition — AMENDMENT F-3**: `DELETE /api/applications/{id}` **requires `If-Match`**, mismatch or absence →
  `409` with `code:"conflict"`. New behaviour **`BEHAVIOR-m3-backend-api-044`**: *"a delete carrying a stale
  revision is refused and the row still exists."* Contract change flagged for owner attention because it alters
  the approved §4.3 table — the decision (optimistic concurrency) is untouched; the asymmetry was a drafting slip
  inside it.

### Q4 — "No CORS policy means no cross-origin *reads*. It does not mean no cross-origin *writes*. What stops a hostile page from POSTing to your loopback API from a visitor's browser?"

The plan's claim — "a whole class of misconfiguration is structurally impossible" — is true for the wildcard-origin-
plus-credentials mistake, and **overstated for writes**. A "simple" cross-origin request (`text/plain`,
form-urlencoded) *is* sent by a browser without a preflight; the attacker cannot read the response, but a
create/delete that lands is not a read. ASP.NET Core's JSON binding in the 8+ templates does accept JSON in a
`text/plain` body, which is precisely the shape such a request takes.

- **Disposition — AMENDMENT F-4**: the API accepts **only `Content-Type: application/json`** on bodies
  (`[Consumes("application/json")]` / a filter returning `415` otherwise). Since a JSON-content-type cross-origin
  `POST` forces a preflight, and no CORS policy is configured, **the preflight fails and the write never happens** —
  which is the claim Q4 found the plan making too broadly, now made correctly. New behaviour
  **`BEHAVIOR-m3-backend-api-045`**: *"`text/plain` body → `415`, no row created."*
- **Also named honestly**: the same-origin dev proxy does **not** protect a user who visits a hostile page while
  the dev server runs — the mitigation above is the control, not the proxy.

### Q5 — "`revision?: number` sits in the *domain* type. You call that 'the honest place to put it'. I call it transport leaking into the domain because it made your own AC weaker."

Half-true, and the honest answer is that the field's safety rests on a comment, and **comments are not gates**.
Today the isolation argument is: nothing outside `src/data/` may read it. But AC-1 did **not** check it — AC-1
checked that no page *file changed*, which would not notice a future page reading `record.revision`, nor a
component gaining a `key={record.revision}` that quietly makes transport state load-bearing in the UI.

- **Disposition — AMENDMENT F-5**: AC-14's invariant scan gains a **command**, not a sentence:
  `grep -rn "revision" src --include=*.ts --include=*.tsx | grep -v '^src/data/' | grep -v '^src/domain/types.ts'`
  must print **nothing**. On that, `revision` stays in the domain type as a *deliberately opaque* row token and the
  objection is answered by an enforceable boundary rather than by rhetoric. (Rejected alternative: keeping it in a
  side `Map` inside the adapter — one more mutable module-global to leak, for a field that a test can fence.)

### Q6 — "`xmin` as an ETag. It is a 32-bit transaction counter. What happens on wraparound, and what does the documentation say?"

Wraparound is real (≈2^32 row updates); the EF Core concurrency-token docs recommend `xmin` for exactly this use
while noting it is not a durable unique identifier. At 5–50 rows on a portfolio project the honest risk is
negligible; the dishonest move would be pretending it is impossible rather than measuring the claim.

- **Disposition — F-6, upheld with an accepted risk**: keep `xmin`, record the ceiling here, and *name* the
  alternative that was rejected and why (a trigger-maintained `bigint` revision column costs a migration and a
  hand-written sequence for a risk this dataset cannot reach). M5 may revisit before anything is public.

### Q7 — "Your SSE adapter re-reads 'once on reconnect'. `EventSource` reconnects automatically and aggressively. A flapping network is a re-list loop hammering your own API — and it re-enters a provider whose only idea is to reload."

- **Answer, then a concession**: the provider's reload is guarded by a mounted-ref check and one in-flight load at
  a time, so a loop is *bounded per-connection*, not per-tick; and a re-read that lands out of order is exactly
  what AC-10's monotonic sequence now absorbs. But "bounded" was reasoned, not tested — the plan has no case for a
  reconnect storm.
- **Disposition — F-8, upheld with a new test**: extend `BEHAVIOR-…-040` to assert **at most one re-list per
  `open`**, including three rapid reconnects. Cheap, and it converts the reassurance into evidence.

### Q8 — "Two tabs, and the writer now receives its own change over SSE. What does that cost on every keystroke-saving write, and did you choose it or inherit it?"

Inherited from M2b's provider, which re-reads on any external change. Cost: one extra `GET /api/applications` per
local write, on a screen that re-renders anyway. Not a problem at five rows; *is* the reason AC-10 stops being
theoretical on the day Slice 3 lands — the writer's own event and its own `await` both arrive, in either order.

- **Disposition — F-9, upheld**: keep the interface honest rather than special-case the writer; the plan already
  says the server cannot tell which client wrote, and "suppress your own events" would need per-client ids in a
  protocol (`EventSource`) that cannot send headers. **Recorded as the reason AC-10 is p0, not p2.**

### Q9 — "CI with `paths:` filters, and a PR that touches only `docs/`. Which check is green, and what happens the day you turn on branch protection?"

An excluded job reports **no status**, which to required-status checks is indistinguishable from a check that
never ran — so the day protection is enabled with "require all checks", docs-only PRs become unmergeable, and the
usual discovery is a rushed `if: always()` hack that runs a job with no reason to exist.

- **Disposition — F-10, plan addition**: the API job's step is gated internally (`if: <api paths changed>`) rather
  than via `on.pull_request.paths`, so the **job always reports and only its steps skip**. Slice 0 must verify both
  outcomes: a docs-only push (job runs, steps skipped) and an `api/**` push (steps run). AC-13 extended to name it.

### Q10 — "You kept the localStorage adapter as a flag-off fallback and called it a safety property. Is that a bridge, or a product that now has two persistence implementations forever?"

The sharpest question a learning project should ask itself, because the answer determines whether M4 inherits a
couple or a corpse. Two adapters mean every future domain change must be expressed twice, and "we'll delete it in
M4" is the most reliable promise in software.

- **Disposition — F-11, upheld with a deadline**: dual-medium is a **migration device with an expiry date**, so it
  is written as one: §4.2 Phase 3 (M4) is the removal commit, and the Task Record's Approval Boundary already
  requires the owner's sign-off to delete the adapter, gated on Assumption 003 being confirmed. The honest
  counter-cost is stated plainly: **while both exist, M3's frontend tests must cover the local path too**, so the
  suite grows rather than moves.

### Q11 — "The eighth error code. M2a froze it at seven. Show me the code path that breaks today if you *don't* add it."

`applicationsProvider`'s refusal gate engages on **`unavailable`** and stops further writes; the create/edit paths
render a sentence from the code. Without `conflict`, a 409 must land on `unavailable` — which would *stop the user
from writing at all* after a mere version mismatch — or on `storage-error`, which the form renders as a storage
fault. Both are wrong in different directions, and `defaultCriteria()`-style exhaustive matching in the union means
the compiler will force every consumer to acknowledge the new variant.

- **Disposition — F-12, upheld**: `DECISION-m3-backend-api-006` stands, and this exchange is its evidence. The
  "no eighth code" rule was about not inventing codes; the vocabulary grew because the medium grew.

### Q12 — "Three times this session your prose ran ahead of your artefacts. What stops this grill being a performance of scrutiny?"

Nothing structural — which is why the findings above are all *falsifiable by a command* and two of them
(F-1, F-3) are marked **blocking** or **contract-changing** rather than filed and forgotten. The absence of a
`grill.md` rubric (F-7) makes this self-assessment weaker, not stronger.

---

## 2. Findings register

| ID | Severity | Finding | Disposition | Effect on the plan |
| :--- | :--- | :--- | :--- | :--- |
| F-1 | **P1** | AC-12 (validators agree) had no cross-language mechanism, and its boundary list assumed length rules the client does not have | **Blocking condition** for Slice 2 | Shared `validation-cases.json` fixture read by both suites; server-only rules must each appear as an explicit case |
| F-2 | P2 | §2 latencies are unmeasurable by anything M3 will build | Amendment | Demoted from targets to recorded observations; bundle-size check kept (it *is* checkable) |
| F-3 | P2 | `DELETE` allowed a blind stale-tab destroy while `PUT` required `If-Match` | Amendment — **contract change** | `If-Match` required on delete; new `BEHAVIOR-…-044` |
| F-4 | P2 | Cross-origin *writes* were under-addressed by "no CORS policy exists" | Amendment | JSON content-type only → `415` otherwise; new `BEHAVIOR-…-045` |
| F-5 | P2 | `revision?: number` isolation rested on a comment, not a gate | Amendment | AC-14 scan extended with the grep above |
| F-6 | P3 | `xmin` wraparound | Upheld, risk accepted and named | Alternative recorded as rejected-with-reason |
| F-7 | **P1** (process) | `pk:grill` is advertised as a trigger but **`.promptkit/workflows/grill.md` does not exist** — the gate M3's Task Record names has no definition | Filed as **DEBT-19** | Gate executed from its two-line advertised contract; limitation stated at the head of this record |
| F-8 | P3 | Reconnect storm was reasoned, not tested | Upheld with a new assertion | `BEHAVIOR-…-040` extended: ≤1 re-list per `open` |
| F-9 | P3 | Writer receives its own SSE change → extra re-read per write | Upheld | Explains AC-10's p0 |
| F-10 | P2 | `paths:`-filtered CI reports *no status*, which breaks required checks later | Plan addition | Internal `if:` gating; AC-13 must show both outcomes |
| F-11 | P2 | Two persistence adapters is a permanent tax unless it expires | Upheld with a deadline | Removal pinned to §4.2 Phase 3, owner-gated |
| F-12 | — | Eighth error code defended | Upheld | Becomes the decision's evidence |

## 3. Open questions for the owner

1. **F-3 changes an endpoint contract you approved.** Require `If-Match` on `DELETE`, or keep it optional and accept
   that a stale tab can destroy a record the user is editing elsewhere?
2. **F-4 adds a `415` path.** Worth a behaviour and a test, or over-engineering for a localhost dev API that M4
   will put behind auth anyway?
3. **F-7 — how do you want a missing gate handled when the plan itself names it?** Options: I keep self-grilling
   and disclose the weakness (done here); you add a `grill.md` rubric to the `.promptkit` submodule (it is a
   gitlink, so that means the upstream repo); or `pk:grill` is dropped from M3's gates and replaced by
   `pk:review` on the spec as a *second reader* — the cheapest substitute with real external structure.
4. **F-11**: do you want the dual-adapter expiry written as a **hard gate** (M4's Task Record may not start until
   the removal slice is scheduled), or is the current owner-gate enough pressure to keep it from calcifying?

---

## 4. Correction to F-1 (added 2026-09-11 05:03 UTC, by `pk:test`)

While writing `docs/tests/2026-09-11-test-m3-backend-api.md` I read `src/domain/validation.ts` — which is what
AGENTS.md working-style step 2 asks for *before* changing anything, and which I owed F-1 before asserting what the
client validates. **F-1's supporting claim was wrong in part**, so it is corrected here rather than quietly dropped:

- **Wrong**: "the client has no length ceiling on `location`/`notes` at all." `MAX_TEXT_LENGTH = 120` is enforced
  on `companyName`, `jobTitle` and `location` (`validation.ts:74–85`). Invariant 3 was cited loosely — the real
  code is both non-empty *and* length-capped for those three fields.
- **Right, and narrower**: **`notes` alone has no client-side ceiling**, so a server limit on `notes` is a genuine
  server-only rule needing its own fixture case.
- **New, and it invalidates part of AC-12's own wording**: `status` has **no runtime client validation at all** — it
  is a TypeScript union (`types/application.ts:14`) and nothing else. So "feed a sixth status to *both* validators"
  is impossible as written: the client has no verdict to return. The fixture must carry a **per-side expectation**
  (`expect.server`, and `expect.client: "not-applicable"` where the type system is the only gate) instead of
  pretending both sides answer every case. `BEHAVIOR-…-032` covers the DB side; `-031` covers what each side can
  actually answer.

**The mechanism F-1 prescribed is unchanged** (one shared fixture read by both suites) and remains the fix; only its
justification was inaccurate. Effect on the plan: **none** on scope, one correction to AC-12's phrasing (now made in
the Task Record), and one more reason the fixture must be explicit about *which side* answers each row.

**Process note, since this record is about scrutiny:** this is the fourth time this session that narration outran the
artefact, and the first one inside a document whose subject was scrutiny itself. The correct step was reading the
file *before* writing the claim; it happened one step late. That is not a reason to soften the grill — it is the
reason the grill's findings are required to be falsifiable by a command, which is how this was caught.

---

## 5. Supplementary pass — the four challenges §7 required and §1–§4 never took
*(2026-09-11 21:10 UTC, agent-owned. §7 lists six required challenges; `grep -ci` over the original pass gives `ENUM` 0,
`timestamptz` 0, `fan` 0, `instance` 0. **This pass is the reason to run a grill you think you already did: it found a
live 500.**)*

### Q13 — "`text + CHECK` for `status`. You have a five-value product concept and you stored it as a string with a
###       guardrail. `ENUM` is the type the database *is*. Why the weaker one?"

**Defence.** `status` is not a domain enum, it is a **product workflow stage**, and those change for different reasons.
`ENUM` makes the value set a *schema* fact: adding `"Interview"` becomes `ALTER TYPE … ADD VALUE`, which **cannot run in a
transaction that also uses the new value** — so a status addition is a two-phase migration with a window where app and
type disagree, on a project whose migration story is one `dotnet ef database update`. With `text + CHECK` the set is
reachable from **one fixture** both validators read, so `-032` proves the constraint is real while the client's list stays
the single source. `CHECK` also fails better: a constraint violation maps to `validation`, whereas an `ENUM` violation
arrives as a cast error whose text is a type name.

**Conceded.** Two costs accepted without saying so: **(1)** `text` permits a value valid in the database and unknown to the
app *if the fixture and the `CHECK` drift* — the guardrail is **two lists, not one**, and nothing asserts they match;
**(2)** ordering: `Saved → Applied → …` has meaning and neither type encodes it, so a future "sort by stage" is a third
list. **Action: a test that the `CHECK`'s values and the fixture's five are the same set** — the same two-sources-of-truth
shape as gap 10a, found by the same kind of question.

### Q14 — "Client-minted ids. You let an untrusted caller choose the primary key of a row."

**Defence.** It is what makes offline create and retry-identity possible at all: the client's id **is** the idempotency
key, which is why `DECISION-007` could promise a retried `POST` cannot create a second row. With a server-generated id the
retry has nothing to deduplicate on. It also drops a round trip from the create path and makes `Location` meaningful
before the body is parsed.

**And here the challenge found a defect, not a nuance.** The id is attacker-controlled input that becomes a PK, so the
question was "is it validated *before* Npgsql sees it?" — and the answer, measured against the running API, is **no**:

```
POST /api/applications  {"id":"not-a-guid", …}   →   HTTP 500
{"type":"…/rfc9110#section-15.6.1","title":"An error occurred while processing your request.","status":500,"traceId":"…"}
```

Server log: `System.Text.Json.JsonException: The JSON value could not be converted to
JobTracker.Api.NewApplicationRequest. Path: $.id` → `---> System.FormatException: The JSON value is not in a supported
Guid format.`

**Mechanism: the type is the validator.** `NewApplicationRequest` declares `Guid Id`, so **deserialization throws before
`ApplicationValidation.Validate` is ever reached** — the request never enters the pipeline that produces
`400` + `errors[].pointer`, and the exception handler faithfully converts a *client's* mistake into a *server's* 500.
Note the asymmetry that makes this easy to miss: **`ApplicationCatalog` does call `Guid.TryParse` on the route `{id}`**
(lines 34–42, 151, with a comment explaining why that beats a `{id:guid}` constraint). The read paths are guarded; the
write path's body is not. **Filed as gap 12.**

**Two further costs, named.** Enumeration is replaced by **collision**: a client can try to write over a row it does not
own, which is harmless today *only because M3 has no authentication* and catastrophic the moment it does — **client-minted
ids are a single-user design, which is precisely why §7 makes M4 precede any public exposure.** And a UUIDv4/v7 mix is
invisible here but real at scale (index locality).

### Q15 — "`appliedAt` is a `date`. An application happens at an *instant*. You threw away half the fact."

**Defence.** The user is asked *"when did you apply?"* and answers with a **calendar day**, from memory, often days later.
Storing an instant implies a precision the input never had and, worse, **silently chooses a time zone for them**:
`2026-03-14T00:00:00Z` is the evening of 2026-03-13 in Auckland, so a date typed late at night renders as the day before —
the most likely reason it was typed late at night. `date` + `yyyy-MM-dd` on the wire keeps the stored fact equal to the
entered fact. `created_at`/`updated_at` **are** `timestamptz` and server-generated, and that split is the actual rule:
**machine facts get instants, human facts get days.**

**Conceded.** No time-of-day means "applied at 3pm, callback at 4" is unrepresentable, and any future sort across a mixed
`date`/`timestamptz` set needs a rule for what a bare date means at midnight in whose zone. **The honest version is "we
chose day precision because the input is day precision," not "because `date` is simpler."**

### Q16 — "Single-instance SSE fan-out. `ApplicationEventBus` is an in-memory `Channel`. Two instances behind a balancer
###       and half your users never see a change. Why is this not a bug?"

**It is a bug with a documented boundary, and the boundary is the decision.** Broadcast is per-process, so with N instances
a write reaches only subscribers on the instance that handled it. **Sticky sessions do not fix it** — stickiness pins a
*reader* to an instance; it does not move a *write* to where the readers are. The failure is also **silent and partial**,
the worst shape: a tab that misses an event stays stale until an unrelated write happens to land on its instance, and
**`-042`'s cross-tab proof is same-process, so the harness cannot see this at all.**

**Why acceptable for M3, not for later.** `ASSUMPTION-m3-backend-api-002` says single instance through M5, and the client's
recovery path is already the cheap one: an SSE event is only a **nudge to re-read**, so a missed event costs staleness and
never corruption — and the store re-reads on mount and on focus regardless. **The mitigation is built; the delivery is
not.** When it matters the fix is not Redis: **we already run PostgreSQL, and `NOTIFY` on a transaction-scoped channel
gives cross-instance fan-out with no new dependency** — the same reason `xmin` was free. **Action: M5's deployment spec
carries this as a gate, not a nice-to-have**, because "add a second replica" is exactly the move that turns a documented
limit into an outage.

### What this pass changed, in one line each

**Q14 → gap 12, a live 500 on malformed input, and the first concrete counterexample to §2.8's `0 unhandled exceptions`
target.** Q13 → a named two-sources risk (fixture vs `CHECK`) needing one test. Q15 → the decision restated as
*precision*, not convenience. Q16 → M5 gains a gate, and `-042` gains a documented blind spot.
**Methodological finding: three of the four challenges I had skipped were the ones that mattered, and I had marked the
grilling "done" on a record that grep shows never mentioned `ENUM`, `timestamptz`, fan-out or instances.**
