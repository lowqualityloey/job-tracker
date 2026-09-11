import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'

import type { ApplicationRepository } from '../domain/applicationRepository'
import { APPLICATIONS_STORAGE_KEY } from './localStorageApplicationRepository'
import { createApplicationStore } from './applicationStore'
import { seedApplications } from './seedApplications'

/**
 * BEHAVIOR-m3-backend-api-036 (AC-1) — the environment variable selects the adapter, and both adapters satisfy the
 * same interface, so nothing above `src/data/` has to learn which one it got.
 *
 * Every assertion here goes through `createApplicationStore()` and the six methods of `ApplicationRepository`.
 * That is deliberate. The tempting version imports the HTTP adapter directly and tests its constructor, which would
 * pass even if the composition root never called it — and "the flag selects the adapter" is a claim *about the
 * composition root*, which is the only place in this app allowed to know that two implementations exist.
 *
 * The observable difference between the two adapters is not their type (both satisfy it, forever, by `tsc`) but
 * where their bytes go: one reads `window.localStorage`, the other performs a network request. So the tests watch
 * `fetch` and `localStorage.getItem`, and the interesting one is the negative claim — with the flag set, the store
 * must **not** read Web Storage, because a record served from local storage while the user believes they are looking
 * at the server is the failure mode AC-1 exists to prevent.
 */

const API_BASE = 'http://api.test:8080'

/**
 * The one place this file writes configuration, and the reason it is a function rather than four assignments.
 *
 * `src/vite-env.d.ts` declares `VITE_API_BASE_URL` **readonly**, which is the app's real rule: nothing in `src/`
 * writes its own configuration. A test has to vary that input to reach both branches, so the write goes through an
 * explicitly mutable view of the same object here — instead of dropping `readonly` from the type and letting
 * production code assign it too, which is what loosening a type to satisfy a test actually costs.
 */
function withApiBaseUrl(url: string | undefined) {
  const env = import.meta.env as { VITE_API_BASE_URL?: string }
  if (url === undefined) {
    delete env.VITE_API_BASE_URL
    return
  }

  env.VITE_API_BASE_URL = url
}

/**
 * The first argument `fetch` was called with, as the string it is.
 *
 * `String(fetchMock.mock.calls[0][0])` is what I wrote first and eslint refused for a good reason
 * (`no-base-to-string`): that argument's type is `RequestInfo | URL`, so a `Request` object would have produced
 * `"[object Object]"` and the assertion below would have been comparing against a string that never describes a
 * real request. Narrowing the union explicitly means a future adapter that passes a `Request` fails with a named
 * error instead of a misleading one.
 */
function requestedUrl(fetchMock: Mock): string {
  // The annotation is load-bearing: `Mock` types `calls` as `any[]`, so without it the value below is `any` and
  // every check after it is unchecked. Naming the real type of fetch's first argument is what makes the narrowing
  // below mean something.
  const target = fetchMock.mock.calls[0]?.[0] as RequestInfo | URL | undefined
  if (typeof target === 'string') {
    return target
  }

  if (target instanceof URL) {
    return target.href
  }

  throw new Error(`the adapter requested with an unexpected target: ${target === undefined ? 'no call' : 'a Request object'}`)
}

function writeLocalSeed() {
  window.localStorage.setItem(
    APPLICATIONS_STORAGE_KEY,
    // `schemaVersion`, not `version`: the envelope is `ApplicationsEnvelopeV1`, and a wrong field name here makes
    // the *guard* test fail, which reads like the behaviour under test failing and sends you debugging the wrong
    // file. Verified against the interface rather than guessed from the existing test's assertion.
    JSON.stringify({ schemaVersion: 1, applications: seedApplications }),
  )
}

describe('adapter selection by VITE_API_BASE_URL', () => {
  // File scope, not inside one describe: the sibling block below needs the same reset, and a beforeEach that only
  // covers its own block is how a later test starts reading an env var a previous test set.
  beforeEach(() => {
    window.localStorage.clear()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    withApiBaseUrl(undefined)
    vi.unstubAllGlobals()
  })

  it('sends reads to the API when the base URL is set', async () => {
    withApiBaseUrl(API_BASE)
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(new Response('[]', { status: 200 }))

    const store = createApplicationStore()
    const result = await store.repository.list()

    expect(result.ok).toBe(true)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requestedUrl(fetchMock)).toContain(`${API_BASE}/api/applications`)
  })

  it('does not read Web Storage once the API is configured', async () => {
    // The negative half, and the one that actually fails today: the store still returns the localStorage adapter,
    // so the test above's `list()` succeeds while quietly serving local records. A flag that changes the *label*
    // and not the *source* is worse than no flag, because it reads as if the switch happened.
    withApiBaseUrl(API_BASE)
    writeLocalSeed()
    // Spying on Storage.prototype, not on window.localStorage: jsdom's `window.localStorage` accessor can hand
    // back a different object per read, so an instance spy observes nothing and the assertion passes because it is
    // looking at the wrong object. First run of this file proved it — the test below went green while the adapter
    // was plainly reading Web Storage. A negative assertion needs a spy that cannot be bypassed.
    const getItem = vi.spyOn(Storage.prototype, 'getItem')
    vi.mocked(fetch).mockResolvedValue(new Response('[]', { status: 200 }))

    await createApplicationStore().repository.list()

    expect(getItem).not.toHaveBeenCalledWith(APPLICATIONS_STORAGE_KEY)
  })

  it('keeps using Web Storage when the variable is absent, with no network call', async () => {
    // AC-2's guarantee, asserted at the seam rather than inferred from a build: unset means today's behaviour,
    // byte for byte. Without this row the first test could be satisfied by an adapter that always fetches.
    writeLocalSeed()

    const result = await createApplicationStore().repository.list()

    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.value).toHaveLength(seedApplications.length)
    }

    expect(fetch).not.toHaveBeenCalled()
  })

  it.each([
    ['the API adapter', () => { withApiBaseUrl(API_BASE) }],
    ['the local adapter', () => { withApiBaseUrl(undefined) }],
  ])('hands pages a complete ApplicationRepository from %s', async (_label, configure) => {
    configure()
    vi.mocked(fetch).mockResolvedValue(new Response('[]', { status: 200 }))

    // The assignment is the assertion TypeScript gets to make: `ApplicationRepository` is a structural contract, so
    // an adapter missing `subscribe` or returning the wrong `Result` shape cannot be written at all. The runtime
    // loop below is not a substitute for it — it catches the case where a method exists but the object was built by
    // hand, which is exactly how an adapter under construction ships half-finished.
    const repository: ApplicationRepository = createApplicationStore().repository
    const members = ['subscribe', 'list', 'get', 'create', 'update', 'remove'] as const

    for (const member of members) {
      expect(typeof repository[member], `${member} is missing from the selected adapter`).toBe('function')
    }
  })
})
