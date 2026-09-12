import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApplicationsProvider, useApplications } from '../state/applicationsProvider'
import { createHttpApplicationRepository } from './httpApplicationRepository'

/**
 * BEHAVIOR-m3-backend-api-040 (AC-11) — the client's half of the stream `-046` put on the server.
 *
 * The shape being protected against is a **re-read storm**, and it is a shape the browser API creates for you:
 * `EventSource` reconnects on its own, and a reconnect emits `open` again. Wire the re-read to `open` naively and one
 * flapping network produces a fetch per event *per connection*, with several tabs multiplying it. The bound the grill
 * recorded as F-8 — "at most one re-list per `open`" — is asserted here in the case that makes it real: **three rapid
 * reconnects, three re-reads, no more.**
 *
 * The other half of the design is what happens on the *first* `open`: nothing. The provider reads the catalog when it
 * mounts, so a re-read on initial connect is a second request for data the caller already asked for, on every page
 * load. `subscribe()` therefore distinguishes "connected" from "re-connected", which is not a distinction the API
 * makes for you — it takes one bit of state to tell them apart, and that bit is the behaviour.
 *
 * A fake `EventSource` is the right boundary: it is a browser platform API, not this project's code. What is *not*
 * faked is the repository, the provider, or the ordering logic — and the server side of this contract has its own
 * real-bytes tests in `api/tests/…/EventStreamTests.cs` (`-046`), because a fake `EventSource` is exactly the thing
 * that could not notice a missing endpoint.
 */

const BASE = 'http://api.test:8080'
class FakeEventSource {
  static instances: FakeEventSource[] = []

  readonly listeners = new Map<string, Array<() => void>>()
  closed = false

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this)
  }

  addEventListener(type: string, listener: () => void): void {
    const existing = this.listeners.get(type) ?? []
    existing.push(listener)
    this.listeners.set(type, existing)
  }

  close(): void {
    // Real semantics: after `close()` the stream dispatches nothing, ever again. A fake that kept dispatching would
    // let an unsubscribe bug look correct, which is the one thing case 5 is here to catch.
    this.closed = true
  }

  emit(type: string): void {
    if (this.closed) {
      return
    }

    for (const listener of this.listeners.get(type) ?? []) {
      listener()
    }
  }

  static last(): FakeEventSource {
    // Indexed rather than `.at(-1)`: `at` needs an `es2022` lib, and widening the app's `target`/`lib` to satisfy one
    // test line is the config change that outlives the test. The `undefined` check below is what `noUncheckedIndexedAccess`
    // asks for either way.
    const instance = FakeEventSource.instances[FakeEventSource.instances.length - 1]
    if (instance === undefined) {
      throw new Error('no EventSource was constructed — subscribe() is not opening a stream at all')
    }

    return instance
  }
}

/** `crypto.randomUUID` is used by the adapter's create; unrelated here but jsdom needs no shim for it. */
function listResponse(rows: unknown[]): Response {
  return new Response(JSON.stringify(rows), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}

function wire(id: string, companyName: string, revision = 1) {
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

describe('subscribe — the adapter over Server-Sent Events', () => {
  beforeEach(() => {
    FakeEventSource.instances = []
    vi.stubGlobal('EventSource', FakeEventSource)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('subscribe delivers exactly one callback per change event', () => {
    const onChange = vi.fn()
    createHttpApplicationRepository(BASE).subscribe(onChange)

    const stream = FakeEventSource.last()
    stream.emit('change')
    stream.emit('change')

    // Two writes, two notifications, two re-reads. Coalescing them would be the "optimisation" that reintroduces the
    // bug: a client that merges two events into one re-read is fine until the two events are three seconds apart and
    // the second is a delete the user is looking at in another tab.
    expect(onChange).toHaveBeenCalledTimes(2)
  })

  it('subscribe ignores events the stream was not asked about', () => {
    const onChange = vi.fn()
    createHttpApplicationRepository(BASE).subscribe(onChange)

    const stream = FakeEventSource.last()
    stream.emit('message')
    stream.emit('ping')

    // The server sends `event: change`; a default `message` listener would also receive SSE comment frames' siblings
    // and any future event type the endpoint grows, driving re-reads for reasons nobody subscribed to.
    //
    // `error` was emitted in this case until BEHAVIOR-062 made it a channel the client DOES listen for: after M4 a stream
    // that never reopens may mean "your session ended", not "the network is busy". Leaving it here would have this test
    // asserting the opposite of its own title, so it moved to the probe suite (streamSessionProbe.test.tsx) and what stays
    // is the part still true — event types nobody subscribed to must not drive reads.
    expect(onChange).not.toHaveBeenCalled()
  })

  it('subscribe re-reads once per reconnect, including three rapid reconnects', () => {
    const onChange = vi.fn()
    createHttpApplicationRepository(BASE).subscribe(onChange)

    const stream = FakeEventSource.last()
    stream.emit('open')
    // Nothing yet: this is the connection that arrived alongside the caller's own first read.
    expect(onChange).not.toHaveBeenCalled()

    stream.emit('open')
    stream.emit('open')
    stream.emit('open')
    expect(onChange).toHaveBeenCalledTimes(3)
  })

  it('subscribe reports a dropped connection once, then re-reads when it comes back', () => {
    const onChange = vi.fn()
    createHttpApplicationRepository(BASE).subscribe(onChange)

    const stream = FakeEventSource.last()
    stream.emit('open')
    // Still nothing: this is the connection that arrived alongside the caller's own first read. Unchanged by -062.
    expect(onChange).not.toHaveBeenCalled()

    // The re-read boundary, asserted from the other side, and the one place `-062` narrowed `-040`'s decision rather than
    // overturning it. `-040`'s reason stands verbatim: `error` fires while the stream is still trying to reconnect,
    // `EventSource` retries on a backoff of its own, and re-reading per error "turns one outage into a request per retry
    // tick". What M4 changed is that the FIRST tick can no longer be ignored, because a permanently 401ing stream would
    // otherwise leave a board full of another session's rows under a spinner that never resolves.
    //
    // So the bound moved from ZERO reports per subscription to ONE. Two errors, one report -- that arithmetic is the whole
    // case, and removing the adapter's `errorReported` guard turns it into 2 here and 5 in the probe suite.
    stream.emit('error')
    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('error')

    stream.emit('error')
    expect(onChange).toHaveBeenCalledTimes(1)

    stream.emit('open')
    expect(onChange).toHaveBeenCalledTimes(2)
    expect(onChange).toHaveBeenNthCalledWith(2, 'change')
  })

  it('subscribe interleaves change events and reconnects without dropping either', () => {
    const onChange = vi.fn()
    createHttpApplicationRepository(BASE).subscribe(onChange)

    const stream = FakeEventSource.last()
    stream.emit('open')
    stream.emit('change')
    stream.emit('open')
    stream.emit('change')
    stream.emit('error')

    // one per change (2) + one for the reconnect (1) + one for the drop (1) = 4, the last term being BEHAVIOR-062's
    // addition. The count is the contract; the ordering inside the adapter is not observable from here, which is the point
    // of asserting the total. -062 moved one term from 0 to 1 — not to "once per tick", which the case above pins down.
    expect(onChange).toHaveBeenCalledTimes(4)
  })

  it('subscribe opens the stream at the configured base url, trailing slash and path prefix intact', () => {
    createHttpApplicationRepository(`${BASE}/trailing/slash/`).subscribe(() => undefined)

    // The trailing slash is half the case: `baseUrl` comes from `VITE_API_BASE_URL`, which a human types into a
    // `.env`, and a doubled slash yields a 404 from a router that is otherwise behaving perfectly.
    //
    // The path prefix is the half this assertion was wrong about. `/trailing/slash` is not decoration — it is the
    // shape of an API mounted behind a reverse proxy (`https://example.com/jobtracker`), and the expectation I wrote
    // demanded that segment be *discarded* to satisfy a URL built off the bare host. So the first Red run caught a
    // bug in the test, not in the adapter: a case that cannot fail for the right reason is worth nothing, and this one
    // would have failed for a reason I had to go looking for.
    expect(FakeEventSource.last().url).toBe(`${BASE}/trailing/slash/api/applications/events`)
  })

  it('unsubscribe closes the stream and silences everything that follows', () => {
    const onChange = vi.fn()
    const unsubscribe = createHttpApplicationRepository(BASE).subscribe(onChange)

    const stream = FakeEventSource.last()
    stream.emit('change')
    expect(onChange).toHaveBeenCalledTimes(1)

    unsubscribe()
    expect(stream.closed).toBe(true)

    stream.emit('change')
    stream.emit('open')
    // The provider unsubscribes on unmount (spec B-1). If a closed stream kept dispatching, every unmounted provider
    // would still hold a live callback into a component that no longer exists — the React warning that M2's
    // `mounted.current` guard was written against, arriving from the network instead of from a promise.
    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('subscribe without an EventSource degrades to no push channel, not to a broken page', () => {
    // jsdom has no `EventSource` at all, which is how three provider tests in other files discovered this path: the
    // constructor call sat in the provider's mount effect, so the ReferenceError did not fail a request, it took down
    // the component. A push channel that is unavailable must cost auto-refresh and nothing else — M2 shipped without
    // one, and every mutation still re-reads what it changed.
    vi.stubGlobal('EventSource', undefined)

    const onChange = vi.fn()
    expect(() => createHttpApplicationRepository(BASE).subscribe(onChange)).not.toThrow()
    expect(FakeEventSource.instances).toHaveLength(0)

    const unsubscribe = createHttpApplicationRepository(BASE).subscribe(onChange)
    expect(() => unsubscribe()).not.toThrow()
    expect(onChange).not.toHaveBeenCalled()  })

  it('subscribe warns when the configured url is unusable, because that is a mistake someone made', () => {
    // The other decline, and the one worth a log line: `VITE_API_BASE_URL` is typed by a human and never validated,
    // and the browser throws on a malformed URL rather than reporting a failed connection. Without the warning the
    // symptom is "the other tab stopped refreshing" and the cause is one stray character in a `.env`.
    class ThrowingEventSource {
      constructor(_url: string) {
        throw new SyntaxError('Invalid URL')
      }
    }

    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    vi.stubGlobal('EventSource', ThrowingEventSource)

    const onChange = vi.fn()
    const unsubscribe = createHttpApplicationRepository(BASE).subscribe(onChange)
    expect(onChange).not.toHaveBeenCalled()
    expect(() => unsubscribe()).not.toThrow()
    expect(warn).toHaveBeenCalledTimes(1)
    expect(warn.mock.calls[0]?.[0]).toContain(BASE)
  })

  it('unsubscribe is safe to call twice', () => {
    const unsubscribe = createHttpApplicationRepository(BASE).subscribe(() => undefined)

    unsubscribe()
    expect(() => unsubscribe()).not.toThrow()
  })
})

/** Consumer for the end-to-end case below. */
function CountProbe() {
  const { status, applications } = useApplications()

  if (status !== 'ready') {
    return <p>{status === 'loading' ? 'Loading applications' : 'Could not load applications'}</p>
  }

  return <p>Ready · {applications.length} applications</p>
}

describe('subscribe — another client writes and this tab notices', () => {
  beforeEach(() => {
    FakeEventSource.instances = []
    vi.stubGlobal('EventSource', FakeEventSource)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('subscribe keeps the rendered list current when a change event arrives', async () => {
    const rowA = wire('11111111-1111-4111-8111-111111111111', 'Fishermend')
    const rowB = wire('22222222-2222-4222-8222-222222222222', 'Sleek', 2)
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(listResponse([rowA]))
      .mockResolvedValueOnce(listResponse([rowA, rowB]))
    vi.stubGlobal('fetch', fetchMock)

    render(
      <ApplicationsProvider repository={createHttpApplicationRepository(BASE)}>
        <CountProbe />
      </ApplicationsProvider>,
    )

    await waitFor(() => expect(screen.getByText('Ready · 1 applications')).toBeInTheDocument())
    expect(fetchMock).toHaveBeenCalledTimes(1)

    FakeEventSource.last().emit('change')

    // The full chain, with nothing faked except the two browser APIs the code is written against: another client's
    // write → a `change` frame → the adapter's callback → the provider's reload → a second request → new rendered
    // text. This is the client half of what `-046` does on the server, and `-042` is where the two finally meet in
    // Chromium over a real socket.
    await waitFor(() => expect(screen.getByText('Ready · 2 applications')).toBeInTheDocument())
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
