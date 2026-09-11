import { render, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'

import { ApplicationsProvider, useApplications } from '../state/applicationsProvider'
import type { ApplicationRepository, Result } from '../domain/applicationRepository'
import type { ApplicationInput } from '../types/application'
import { createHttpApplicationRepository } from './httpApplicationRepository'

/**
 * BEHAVIOR-m3-backend-api-038 (AC-9, wording corrected per register §14) — **what happens when the API is not
 * there at all**, asserted at both layers that can get it wrong.
 *
 * The adapter half pins the *label*: a `fetch` rejection is `storage-error`, and specifically **not** `unavailable`.
 * §4.3 says so with a reason ("'we could not learn anything' is not 'the server said it is busy'"), and the
 * distinction is invisible from the outside — the provider's refusal gate engages on any error state — which is
 * exactly why it needs asserting. An assertion nobody can falsify is the reason the spec carried two contradictory
 * sentences about this case for a whole milestone.
 *
 * The provider half pins the *consequence*: after a read that never reached the server, a write is refused **without
 * another request**. That is the user-facing claim in AC-9 ("degrades like storage blocked"), and the fetch call
 * count is what proves it: a refusal that still sent the POST would be a queue of retries the user cannot see.
 *
 * Both halves drive the real adapter against a stubbed `fetch`. No internal module is mocked — a test that replaces
 * the code under test with a double can only ever prove the double works.
 */

const BASE = 'http://api.test:8080'

const draft: ApplicationInput = {
  companyName: 'Fishermend',
  jobTitle: 'Frontend Engineer',
  location: 'Auckland, NZ',
  status: 'Saved',
}

function transportDown(): TypeError {
  // What browsers actually surface: `fetch` rejects with a TypeError whose message varies by engine, and none of
  // them name a status code, because there is no response.
  return new TypeError('Failed to fetch')
}

describe('server unreachable — the adapter reports what it actually knows', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const calls: Array<[string, (repository: ApplicationRepository) => Promise<Result<unknown>>]> = [
    ['list', (repository) => repository.list()],
    ['get', (repository) => repository.get('3f9b4d0c-6c1e-4a2f-9b7f-4a3d2e1f0a9b')],
    ['create', (repository) => repository.create(draft)],
    ['update', (repository) => repository.update('3f9b4d0c-6c1e-4a2f-9b7f-4a3d2e1f0a9b', { status: 'Applied' })],
    ['remove', (repository) => repository.remove('3f9b4d0c-6c1e-4a2f-9b7f-4a3d2e1f0a9b')],
  ]

  it.each(calls.map(([name, invoke]) => [name, invoke] as const))(
    'a rejected %s becomes storage-error, never unavailable',
    async (_name, invoke) => {
      vi.mocked(fetch).mockRejectedValue(transportDown())

      const repository = createHttpApplicationRepository(BASE)
      const result = await invoke(repository)

      expect(result.ok).toBe(false)
      if (result.ok) {
        return
      }

      expect(result.error.code).toBe('storage-error')
      // Asserted negatively as well as positively, because the two codes are behaviourally identical through the
      // provider and `unavailable` is the answer the spec's *ladder* gives. If someone "fixes" this toward AC-9's
      // original wording, this line is what makes that a deliberate edit rather than a quiet one.
      expect(result.error.code).not.toBe('unavailable')

      if (result.error.code === 'storage-error') {
        // The detail is the transport failure's own words. A mapping that swallowed `cause` would still return the
        // right code and leave a support conversation with nothing to look at.
        expect(result.error.detail).toContain('Failed to fetch')
      }
    },
  )

  it('still asks the server exactly once per call when nothing answers', async () => {
    // No retry loop, no fallback-to-cache, no second attempt on a different port: `-038`'s contract is one request,
    // one honest failure. A retry here would double every write the moment a connection blips, and a POST is only
    // idempotent because of the client-minted id — which is a guarantee about *deliberate* retries, not about
    // machinery the user never asked for.
    vi.mocked(fetch).mockRejectedValue(transportDown())
    await createHttpApplicationRepository(BASE).list()

    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('names the configured host in the request it never got an answer from', async () => {
    vi.mocked(fetch).mockRejectedValue(transportDown())
    await createHttpApplicationRepository(BASE).list()

    const target = (fetch as Mock).mock.calls[0]?.[0] as RequestInfo | URL | undefined
    expect(typeof target === 'string' && target.startsWith(BASE)).toBe(true)
  })
})

/** A consumer, so the assertion is made on what a UI could render rather than on hook internals. */
function WriteProbe() {
  const { status, createApplication } = useApplications()
  const [outcome, setOutcome] = useState('none')

  return (
    <div>
      <p>{status === 'ready' ? 'Ready' : status === 'loading' ? 'Loading applications' : 'Could not load applications'}</p>
      <button
        type="button"
        onClick={() => {
          void createApplication(draft).then((result) => {
            setOutcome(result.ok ? 'saved' : `refused:${result.error.code}`)
          })
        }}
      >
        Try anyway
      </button>
      <p>Outcome: {outcome}</p>
    </div>
  )
}

describe('server unreachable — the provider refuses the write it was not given data for', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('refuses a create after a failed load, without reaching the network again', async () => {
    vi.mocked(fetch).mockRejectedValue(transportDown())

    render(
      <ApplicationsProvider repository={createHttpApplicationRepository(BASE)}>
        <WriteProbe />
      </ApplicationsProvider>,
    )

    // The error state first: a page that showed an empty list here would be the M2a defect AC-9 inherits — "no
    // records" and "I could not ask" are different sentences, and only one of them is true.
    await waitFor(() => expect(screen.getByText('Could not load applications')).toBeInTheDocument())
    expect(await screen.findByText('Outcome: none')).toBeInTheDocument()

    // The button is deliberately always enabled. No page in this app offers a write control while the provider is in
    // `error`, so this probe is testing the provider's own gate — defence in depth for a state the UI does not
    // currently reach, which is the level M2b's `refusal` was written at. Making the probe mirror a page would have
    // meant disabling the thing under test.
    screen.getByRole('button', { name: 'Try anyway' }).click()

    await waitFor(() => expect(screen.getByText('Outcome: refused:storage-error')).toBeInTheDocument())
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1)
  })

  it('hands back the load failure itself rather than a fresh error invented for the refusal', async () => {
    // The refusal is not a new event: it is the same unread-store answer, returned again. A second code would give
    // the UI two sentences about one problem, and the user would have to work out which one to act on.
    vi.mocked(fetch).mockRejectedValue(transportDown())

    render(
      <ApplicationsProvider repository={createHttpApplicationRepository(BASE)}>
        <WriteProbe />
      </ApplicationsProvider>,
    )

    await waitFor(() => expect(screen.getByText('Could not load applications')).toBeInTheDocument())
    screen.getByRole('button', { name: 'Try anyway' }).click()

    await waitFor(() => expect(screen.getByText('Outcome: refused:storage-error')).toBeInTheDocument())
    expect(screen.queryByText(/refused:unavailable/)).not.toBeInTheDocument()
    // Added after a mutation check, because without it this case did not test what its name claims. Deleting the
    // provider's refusal gate left the assertion green: the write then reached the repository, which returned
    // `storage-error` anyway, and the probe's own `refused:` prefix made the outcome text identical either way.
    // Counting requests is what separates "the provider declined" from "the server declined" — one call means the
    // POST never happened.
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1)
  })
})
