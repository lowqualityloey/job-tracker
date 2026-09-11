import { act, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApplicationsProvider, useApplications } from '../state/applicationsProvider'
import type { ApplicationRepository } from '../domain/applicationRepository'
import type { JobApplication } from '../types/application'
import { createHttpApplicationRepository } from './httpApplicationRepository'

/**
 * BEHAVIOR-m3-backend-api-039 (AC-10) — **an older response that arrives after a newer one must not repaint what the
 * user is looking at.** This closes **P2-2**, which M2b handed over with the note that it could not be tested there:
 * the local adapter reads synchronously, so no ordering hazard existed until something could be slow.
 *
 * The guard lives in the **adapter**, not the provider, and that is a design decision rather than a convenience. A
 * provider-level guard has to *drop* the losing response, and a dropped response takes its outcome with it — including
 * an error. `send`/`list` already distinguish "the API is unreachable" (AC-9's refusal gate) from "here is the list",
 * so a guard that silently discards whichever re-read is older can also discard the one failure the user needed to
 * hear about. The adapter can do something strictly better: it never throws a response away, it hands the late caller
 * **the payload that already won**. The user-visible guarantee is identical; no information is lost, and `src/state/`
 * stays untouched, which AC-1 requires of this milestone.
 *
 * What the adapter cannot see is the caller that ignores resolution order. Both provider cases below render the real
 * `ApplicationsProvider` and assert on rendered text, because "cannot repaint stale data" is a claim about paint —
 * and one control case guards against the over-eager fix: a guard that suppresses *every* out-of-order response would
 * break the ordinary case where requests land in the order they were sent.
 */

const BASE = 'http://api.test:8080'
const ROW_A = '11111111-1111-4111-8111-111111111111'
const ROW_B = '22222222-2222-4222-8222-222222222222'

function wire(id: string, revision: number, companyName: string): JobApplication & { revision: number } {
  // `appliedAt`/`notes` are optional-and-absent rather than null: the wire shape says "no date", and writing null here
  // would be a claim about the API's JSON that no test in this file is checking.
  return {
    id,
    companyName,
    jobTitle: 'Frontend Engineer',
    location: 'Auckland, NZ',
    status: 'Applied',
    createdAt: '2026-03-01T09:00:00Z',
    revision,
  }
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}

/**
 * A `fetch` whose every response is held until the test releases it, in whatever order the scenario needs.
 *
 * `gates` is indexed by call order, so a scenario reads as the interleaving it describes: `release(1)` then
 * `release(0)` *is* "request 2 won the race". Real timers, real promises — the delay is in the transport, which is
 * the only place this bug can happen.
 */
function gatedFetch() {
  const gates: Array<(response: Response) => void> = []
  const failures: Array<(reason: unknown) => void> = []
  const mock = vi.fn((..._request: unknown[]) => {
    let succeed: (response: Response) => void = () => undefined
    let fail: (reason: unknown) => void = () => undefined
    const promise = new Promise<Response>((resolve, reject) => {
      succeed = resolve
      fail = reject
    })
    gates.push(succeed)
    failures.push(fail)
    return promise
  })
  vi.stubGlobal('fetch', mock)

  return {
    mock,
    /** Number of requests the adapter has put on the wire so far. */
    pending: () => gates.length,
    /**
     * Forgets requests already made. A scenario that counts requests *after* an earlier phase must call this, or
     * `pending()` reports the whole history and the assertion fails for arithmetic rather than for behaviour — which
     * is exactly what the first run of this file did.
     */
    reset: () => {
      gates.length = 0
      failures.length = 0
      mock.mockClear()
    },
    succeed: (index: number, body: unknown) => gates[index](jsonResponse(body)),
    /** §4.3's `DELETE` answer: 204, no body. Parsing it would invent a corrupt-data error for a success. */
    noContent: (index: number) => gates[index](new Response(null, { status: 204 })),
    fail: (index: number, reason: unknown) => failures[index](reason),
  }
}

/**
 * Lets a test *cause* a re-read.
 *
 * The adapter's `subscribe()` is an honest no-op until `-040` gives it SSE, so a provider-level out-of-order
 * scenario has no other way to trigger a second `list()`. This decorator captures the callback the provider passes
 * down and exposes it as `notify()`. It stands in for "a change notification arrived", which is the event `-040` will
 * make real; the code under test — ordering inside `list()`, and the provider's paint — stays untouched.
 */
/** Also captures each `list()` promise, so a test can wait for the adapter to have answered — the only reliable way
 * to know a negative assertion ("the stale count never appeared") was evaluated *after* the stale response landed. */
function withNotification(inner: ApplicationRepository) {
  let callback: (() => void) | null = null
  const reads: Array<Promise<unknown>> = []

  const repository: ApplicationRepository = {
    list: () => {
      const read = inner.list()
      reads.push(read)
      return read
    },
    get: (id) => inner.get(id),
    create: (input) => inner.create(input),
    update: (id, patch) => inner.update(id, patch),
    remove: (id) => inner.remove(id),
    subscribe: (onChange) => {
      callback = onChange
      return inner.subscribe(onChange)
    },
  }

  return { repository, notify: () => callback?.(), reads }
}

/**
 * Flushes React's state update that follows an awaited repository call.
 *
 * `waitFor` on a *negative* assertion is worthless here: it passes on the first tick, before the late response has
 * been applied, which is how this case went green against an unguarded adapter. Awaiting the adapter's own promise
 * inside `act` makes the ordering explicit — the response has been handled, React has rendered, and only then is the
 * absence asserted.
 */
async function settle(read: Promise<unknown> | undefined): Promise<void> {
  await act(async () => {
    await read
    await Promise.resolve()
  })
}

function ListProbe() {
  const { status, applications } = useApplications()

  if (status !== 'ready') {
    return <p>{status === 'loading' ? 'Loading applications' : 'Could not load applications'}</p>
  }

  return <p>Ready · {applications.length} applications</p>
}

describe('out-of-order re-reads — ignores an older response that lands late', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('ignores an older response that lands late and answers with the payload that won', async () => {
    const transport = gatedFetch()
    const repository = createHttpApplicationRepository(BASE)

    const first = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(1))
    const second = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(2))

    // Request 2 wins the race.
    transport.succeed(1, [wire(ROW_A, 9, 'Fishermend'), wire(ROW_B, 3, 'Sleek')])
    await expect(second).resolves.toMatchObject({
      ok: true,
      value: [{ companyName: 'Fishermend' }, { companyName: 'Sleek' }],
    })

    // Now request 1 lands with the state of the world as it looked before — one row, an older revision.
    transport.succeed(0, [wire(ROW_A, 7, 'Old Name')])
    const result = await first

    // It resolves **ok**, not dropped and not as an error, and with the newer rows. The request was answered; what
    // the answer described was superseded before it arrived, and the fresher answer is the truthful one to hand back.
    expect(result).toMatchObject({
      ok: true,
      value: [{ companyName: 'Fishermend' }, { companyName: 'Sleek' }],
    })
  })

  it('still paints the newest data when responses arrive in the order they were sent', async () => {
    // Control case, and the one that keeps the fix honest. A guard written as "ignore anything whose response is not
    // the newest issued" would fail this test only after a second scenario is added, so it is asserted here: two
    // sequential reads, both landing normally, must both reach the caller.
    const transport = gatedFetch()
    const repository = createHttpApplicationRepository(BASE)

    const first = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(1))
    transport.succeed(0, [wire(ROW_A, 1, 'Fishermend')])
    await expect(first).resolves.toMatchObject({ ok: true, value: [{ companyName: 'Fishermend' }] })

    const second = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(2))
    transport.succeed(1, [wire(ROW_A, 2, 'Fishermend'), wire(ROW_B, 1, 'Sleek')])
    await expect(second).resolves.toMatchObject({
      ok: true,
      value: [{ companyName: 'Fishermend' }, { companyName: 'Sleek' }],
    })
  })

  it('never hides a failure just because a newer request already succeeded', async () => {
    // The deliberate boundary of the guard. Dropping a stale *payload* is right; dropping a *failure* is not, because
    // a failure is the only thing that engages AC-9's refusal gate. A late transport error after a successful newer
    // read therefore reaches its caller as an error, and that is the choice recorded here — with its cost named in
    // the implementation comment, since the same rule means an old failing read can move a UI that already has fresh
    // data into the error state.
    const transport = gatedFetch()
    const repository = createHttpApplicationRepository(BASE)

    const first = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(1))
    const second = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(2))

    transport.succeed(1, [wire(ROW_A, 5, 'Fishermend')])
    await expect(second).resolves.toMatchObject({ ok: true })

    transport.fail(0, new TypeError('Failed to fetch'))
    await expect(first).resolves.toMatchObject({ ok: false, error: { code: 'storage-error' } })
  })

  it('does not let a superseded response write a revision into the concurrency table', async () => {
    // The half of this that is easy to miss, and the half a user hits later. The superseded payload carries
    // `revision: 7` for ROW_A where the accepted one carried `9`. If the adapter remembered the loser's revision, the
    // next `remove(ROW_A)` — which sends `If-Match` straight from the table, with no preceding read — would be
    // refused by a server holding 9, and the row would survive a delete the user was told had been rejected. The
    // response is discarded as data; it must be discarded as *evidence* too.
    const transport = gatedFetch()
    const repository = createHttpApplicationRepository(BASE)

    const first = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(1))
    const second = repository.list()
    await waitFor(() => expect(transport.pending()).toBe(2))

    transport.succeed(1, [wire(ROW_A, 9, 'Fishermend')])
    await second
    transport.succeed(0, [wire(ROW_A, 7, 'Old Name')])
    await first

    transport.reset()
    const removed = repository.remove(ROW_A)
    await waitFor(() => expect(transport.pending()).toBe(1))

    const init = transport.mock.mock.calls[0]?.[1] as RequestInit | undefined
    expect((init?.headers as Record<string, string>)?.['if-match']).toBe('"9"')

    transport.noContent(0)
    await expect(removed).resolves.toMatchObject({ ok: true })
  })

  it('cannot repaint the list a user is looking at when the provider is the caller', async () => {
    // AC-10 is written as a claim about the screen, so it is asserted on the screen: mount (read 1 in flight), a
    // notification arrives (read 2 in flight), read 2 lands, then the stale read 1 lands. The rendered count must
    // never fall from 2 back to 1.
    const transport = gatedFetch()
    const { repository, notify, reads } = withNotification(createHttpApplicationRepository(BASE))

    render(
      <ApplicationsProvider repository={repository}>
        <ListProbe />
      </ApplicationsProvider>,
    )

    await waitFor(() => expect(transport.pending()).toBe(1))
    notify()
    await waitFor(() => expect(transport.pending()).toBe(2))

    transport.succeed(1, [wire(ROW_A, 4, 'Fishermend'), wire(ROW_B, 1, 'Sleek')])
    await waitFor(() => expect(screen.getByText('Ready · 2 applications')).toBeInTheDocument())

    transport.succeed(0, [wire(ROW_A, 2, 'Old Name')])
    await settle(reads[0])
    expect(screen.queryByText('Ready · 1 applications')).not.toBeInTheDocument()
    expect(screen.getByText('Ready · 2 applications')).toBeInTheDocument()
  })
})

