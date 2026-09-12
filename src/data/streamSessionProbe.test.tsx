import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import LoginPage from '../pages/LoginPage'
import RequireSession from '../components/RequireSession'
import { createHttpApplicationRepository } from './httpApplicationRepository'
import { ApplicationsProvider, useApplications } from '../state/applicationsProvider'

/**
 * BEHAVIOR-062 — when the event stream fails, the client **probes the session once**. Seam Unit (FE), p1.
 *
 * ## The loop this closes
 *
 * `EventSource` reconnects on its own, forever, and it fires an `error` event on every failed attempt. Before M4 a stream
 * that could not be opened was merely an optimisation lost. Now a 401 is a *permanently* failing connection that the browser
 * will keep retrying: one `GET /api/applications/events` per backoff tick, per tab, for as long as the tab is open, while the
 * UI sits there showing rows from a session that no longer exists and a spinner that will never resolve.
 *
 * ## The M3 decision this has to respect
 *
 * `subscribe`'s comment says re-reading on `error` is deliberately NOT done, because "one outage turns into a request per
 * retry tick per tab." That reasoning is sound and predates authentication: it is about *unbounded* probing. So the contract
 * here is **one probe per subscription**, counted in the adapter — the component that actually sees the ticks — and the
 * provider acts only on the verdict. M3's bound survives; what changes is that the first tick is no longer ignored.
 *
 * ## The counterfactual that matters most
 *
 * A probe whose answer is *not* `unauthorized` closes nothing and changes nothing on screen. Without that assertion, "stop
 * the retry loop" is one `if` away from "any stream hiccup blanks the board" — trading a background optimisation for a
 * user-visible failure. The stream is a convenience over explicit reads (spec B-1), and a test suite that only proves the
 * 401 path would happily allow that trade.
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
    this.closed = true
  }

  emit(type: string): void {
    // Real semantics: a closed stream dispatches nothing, ever again.
    if (this.closed) {
      return
    }

    for (const listener of this.listeners.get(type) ?? []) {
      listener()
    }
  }

  static last(): FakeEventSource {
    // Index math, not `.at(-1)`: the app project targets ES2020 and `Array.prototype.at` is ES2022, so the
    // typecheck (TS2550) refuses it even though Node and every current browser have it. The tsconfig target is the
    // contract here, not the runtime I happen to test on.
    const instance = FakeEventSource.instances[FakeEventSource.instances.length - 1]
    if (!instance) {
      throw new Error('no EventSource was constructed')
    }
    return instance
  }
}

// A plain literal, like eventStream.test.tsx builds: the adapter's `WireApplication` is a module-local type, so importing
// it would mean exporting it for a test's convenience -- and the shape IS the contract under test, so spelling it out here
// is the point rather than an inconvenience.
function wire(): Record<string, unknown> {
  return {
    id: 'a1b2c3d4-0000-4000-8000-000000000001',
    companyName: 'Hooli',
    jobTitle: 'Engineer',
    location: 'Auckland, NZ',
    status: 'Applied',
    createdAt: '2026-03-01T09:00:00Z',
    revision: 1,
  }
}

type Answer = 'ok' | 'unauthorized' | 'unavailable'

const fetchMock = vi.fn()

/** Counts only the catalog reads, so the mount read and the probe are separable. */
function readCount(): number {
  return fetchMock.mock.calls.filter(([url]) => String(url) === `${BASE}/api/applications`).length
}

function answerAs(answer: Answer): void {
  fetchMock.mockImplementation((url: string) => {
    if (String(url) !== `${BASE}/api/applications`) {
      return Promise.resolve(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } }))
    }

    if (answer === 'unauthorized') {
      return Promise.resolve(new Response(JSON.stringify({ code: 'unauthorized' }), {
        status: 401,
        headers: { 'content-type': 'application/problem+json' },
      }))
    }

    if (answer === 'unavailable') {
      return Promise.resolve(new Response('', { status: 503 }))
    }

    return Promise.resolve(new Response(JSON.stringify([wire()]), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }))
  })
}

function Board(): JSX.Element {
  const { applications, status, error } = useApplications()
  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="count">{applications.length}</span>
      {/* A harness that cannot name its own failure is how a fixture bug gets read as a feature Red: the first run of this
          file showed count 0 with no hint that the mount read had returned `unsupported-version`. */}
      <span data-testid="error">{error?.code ?? 'none'}</span>
    </div>
  )
}

function Harness(): JSX.Element {
  const [viewMounted, setViewMounted] = useState(true)

  return (
    <>
      <button type="button" onClick={() => setViewMounted(false)}>unmount the view</button>
      {viewMounted ? (
        <ApplicationsProvider repository={createHttpApplicationRepository(BASE)}>
          <Routes>
            <Route path="/login" element={<LoginPage onAuthenticated={vi.fn()} />} />
            <Route path="/applications" element={<RequireSession><Board /></RequireSession>} />
          </Routes>
        </ApplicationsProvider>
      ) : null}
    </>
  )
}

function mount(answer: Answer): void {
  answerAs(answer)
  render(
    <MemoryRouter initialEntries={['/applications']}>
      <Harness />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  fetchMock.mockReset()
  FakeEventSource.instances = []
  // Both globals, both every time. The first draft of this hook stubbed only EventSource, so every case in the file called
  // Node's REAL fetch against http://api.test:8080, the provider went to `storage-error` with "TypeError: fetch failed",
  // and all seven tests failed for a reason that had nothing to do with the behaviour under test. A stub that is never
  // installed is worse than no stub, because it looks like a fixture and behaves like the network.
  vi.stubGlobal('EventSource', FakeEventSource)
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('a failing event stream probes the session once', () => {
  it('five retry ticks produce exactly one probe, not five reads', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))
    const baseline = readCount()

    const stream = FakeEventSource.last()
    for (let tick = 0; tick < 5; tick += 1) {
      stream.emit('error')
    }

    await waitFor(() => expect(readCount()).toBe(baseline + 1))
    expect(stream.closed).toBe(false)
    // The bound is the point: the adapter counts ticks so the provider never sees a stampede.
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(readCount()).toBe(baseline + 1)
  })

  it('when the probe comes back 401, the stream is closed and the user is at the login screen', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))

    // The session dies between the read and the tick: the probe is what finds out.
    answerAs('unauthorized')
    FakeEventSource.last().emit('error')

    expect(await screen.findByRole('heading', { name: /sign in/i })).toBeTruthy()
    expect(FakeEventSource.last().closed).toBe(true)
    // The intended URL survives the loop-breaking, so closing the stream does not cost the user their place.
    expect(screen.getByTestId('destination').textContent).toBe('/applications')
  })

  it('a closed stream never probes again', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))
    const baseline = readCount()

    answerAs('unauthorized')
    const stream = FakeEventSource.last()
    stream.emit('error')
    await waitFor(() => expect(screen.getByRole('heading', { name: /sign in/i })).toBeTruthy())

    const afterFirst = readCount()
    stream.emit('error')
    stream.emit('error')
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(readCount()).toBe(afterFirst)
    expect(afterFirst).toBeGreaterThan(baseline)
  })

  it('when the probe succeeds the stream stays open — the outage was the network, not the session', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))

    FakeEventSource.last().emit('error')
    await waitFor(() => expect(readCount()).toBe(2))

    expect(FakeEventSource.last().closed).toBe(false)
    expect(screen.getByTestId('count').textContent).toBe('1')
    expect(screen.getByTestId('status').textContent).toBe('ready')
  })

  it('a probe that fails for a NON-session reason changes nothing on screen', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))

    // 503 -> `unavailable`. If the probe path shared the normal read path's state handling, this would flip the board to
    // an error screen and close a stream that was never the problem.
    answerAs('unavailable')
    const stream = FakeEventSource.last()
    stream.emit('error')
    await waitFor(() => expect(readCount()).toBe(2))

    expect(stream.closed).toBe(false)
    expect(screen.getByTestId('count').textContent).toBe('1')
    expect(screen.getByTestId('status').textContent).toBe('ready')
  })

  it('a change event still reloads, and is not counted as a probe', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))
    const baseline = readCount()

    FakeEventSource.last().emit('change')
    await waitFor(() => expect(readCount()).toBe(baseline + 1))
    expect(FakeEventSource.last().closed).toBe(false)
  })

  it('the stream is closed when the provider unmounts, so no probe can outlive the tab view', async () => {
    mount('ok')
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))

    fireEvent.click(screen.getByRole('button', { name: /unmount the view/i }))
    await waitFor(() => expect(FakeEventSource.last().closed).toBe(true))

    const baseline = readCount()
    FakeEventSource.last().emit('error')
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(readCount()).toBe(baseline)
  })
})

