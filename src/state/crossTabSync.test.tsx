import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationsPage from '../pages/ApplicationsPage'
import {
  APPLICATIONS_STORAGE_KEY,
  createLocalStorageRepository,
} from '../data/localStorageApplicationRepository'
import type { ApplicationRepository } from '../domain/applicationRepository'
import { ApplicationsProvider } from './applicationsProvider'

beforeEach(() => {
  // jsdom's Storage is module-scoped, so a leftover envelope from a sibling file would seed
  // this one's provider with the wrong record count. Reset at file scope, not per describe.
  localStorage.clear()
})

function mount(repository: ApplicationRepository, route = '/applications') {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <ApplicationsProvider repository={repository}>
        <Routes>
          <Route path="/applications" element={<ApplicationsPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
}

/**
 * Stands in for a second tab.
 *
 * A real `StorageEvent` never fires in the tab that wrote, so this is the only way to drive the
 * path in one process — and `newValue` is deliberately garbage: the provider must **re-read**
 * through the repository, not decode the event payload (spec §4 forbids parsing `newValue` because
 * that bypasses envelope validation, the field-by-field rebuild, quarantine, and the version gate).
 */
function externalWrite(key: string | null = APPLICATIONS_STORAGE_KEY) {
  fireEvent(
    window,
    new StorageEvent('storage', { key, newValue: '{ not valid json — must not be parsed', url: location.href }),
  )
}

/** A writer that is not the mounted reader, i.e. the other tab. */
function otherTab() {
  return createLocalStorageRepository({ storage: localStorage })
}

const fishermend = {
  companyName: 'Fishermend Limited',
  jobTitle: 'Staff Frontend Engineer',
  location: 'Auckland, NZ',
  status: 'Interview' as const,
}

// BEHAVIOR-m2b-filters-cross-tab-022 — a write made in another tab appears in the already-mounted
// UI, and B-1's mitigation (the listener is detached on unmount) is asserted in the same file
// because a leaked subscription is only visible from the outside as a read that should not happen.
describe('reconciling a write from another tab', () => {
  it('shows a record another tab created, with no reload', async () => {
    mount(createLocalStorageRepository({ storage: localStorage }))
    await screen.findByText('Datacom')
    expect(screen.getByRole('status')).toHaveTextContent('5 of 5 applications')

    await otherTab().create(fishermend)
    externalWrite()

    expect(await screen.findByText('Fishermend Limited')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('6 of 6 applications')
  })

  // BEHAVIOR-023 / spec B-3. The count region is the observable: a spurious reload would replace
  // the snapshot, and a re-read storm would show up as extra list() calls.
  it('ignores a storage event for an unrelated key', async () => {
    const repository = createLocalStorageRepository({ storage: localStorage })
    const list = vi.spyOn(repository, 'list')
    mount(repository)
    await screen.findByText('Datacom')

    list.mockClear()
    // No timer: the handler runs synchronously inside dispatchEvent, so if it had decided to
    // re-read, the spy would already have been called by the time this line executes.
    externalWrite('job-tracker:theme')

    expect(list).not.toHaveBeenCalled()
    expect(screen.getByRole('status')).toHaveTextContent('5 of 5 applications')
    list.mockRestore()
  })

  it('detaches the listener on unmount (spec B-1)', async () => {
    const repository = createLocalStorageRepository({ storage: localStorage })
    const list = vi.spyOn(repository, 'list')
    const { unmount } = mount(repository)
    await screen.findByText('Datacom')

    list.mockClear()
    unmount()
    externalWrite()

    expect(list).not.toHaveBeenCalled()
    list.mockRestore()
  })
})
