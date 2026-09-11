import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationDetailsPage from '../pages/ApplicationDetailsPage'
import ApplicationFormPage from '../pages/ApplicationFormPage'
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
          <Route path="/applications/:id" element={<ApplicationDetailsPage />} />
          <Route path="/applications/:id/edit" element={<ApplicationFormPage />} />
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

/** The rendered cards in the order the page shows them, keyed by job title. */
const cardTitles = () => screen.getAllByRole('article').map((card) => card.querySelector('h3')?.textContent)

/** A fixed seed id, so "the row another tab deleted" names one specific record. */
const XERO = 'd36b4d5f-291b-40c0-b96c-c1a61a23bf93'

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

  // BEHAVIOR-024 — `key: null` means storage was cleared wholesale. That says "something changed",
  // not "there is nothing", so the provider must re-read instead of rendering an empty list it
  // invented. Re-reading is also the only branch that survives a clear() followed by a write.
  it('re-reads on a null-key event instead of assuming the store is empty', async () => {
    mount(createLocalStorageRepository({ storage: localStorage }))
    await screen.findByText('Datacom')

    // Another tab drops two records and does not tell us how.
    const repository = otherTab()
    await repository.remove('d36b4d5f-291b-40c0-b96c-c1a61a23bf93')
    await repository.remove('6b2a7f52-ad38-4fca-a3fe-4d95437be9f9')

    externalWrite(null)

    await screen.findByText('Full Stack Developer')
    expect(cardTitles()).toEqual(['Graduate Software Engineer', 'Software Developer', 'Full Stack Developer'])
    expect(screen.getByRole('status')).toHaveTextContent('3 of 3 applications')
  })

  // BEHAVIOR-025 — last-write-wins means an in-flight write here can target a row that no longer
  // exists. The store answers `not-found`; the snapshot must then reconcile, because a row the
  // store refuses to act on is a row the UI has no business still offering to act on.
  it('drops the row after a delete the store refused as not-found', async () => {
    mount(createLocalStorageRepository({ storage: localStorage }), `/applications/${XERO}`)
    await screen.findByRole('heading', { name: 'Junior Frontend Developer' })

    // Another tab deletes it first. This tab's snapshot still holds it.
    await otherTab().remove(XERO)

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }))
    fireEvent.click(screen.getByRole('button', { name: 'Delete application' }))

    await screen.findByRole('heading', { name: 'Application not found' })

    fireEvent.click(screen.getByRole('link', { name: /back to applications/i }))
    await screen.findByText('Datacom')
    expect(cardTitles()).not.toContain('Junior Frontend Developer')
    expect(screen.getByRole('status')).toHaveTextContent('4 of 4 applications')
  })

  it('drops the row after an edit the store refused as not-found', async () => {
    mount(createLocalStorageRepository({ storage: localStorage }), `/applications/${XERO}/edit`)
    await screen.findByLabelText(/company name/i)

    await otherTab().remove(XERO)

    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await screen.findByRole('heading', { name: 'Application not found' })
    expect(screen.queryByLabelText(/company name/i)).toBeNull()
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
