import { fireEvent, render, screen, waitFor } from '@testing-library/react'

import StorageNotice from '../components/StorageNotice'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import type { ApplicationRepository, Result } from '../domain/applicationRepository'
import type { JobApplication } from '../types/application'
import { ApplicationsProvider, useApplications } from './applicationsProvider'

const draft = {
  companyName: 'Fishermend',
  jobTitle: 'Frontend Engineer',
  location: 'Auckland, NZ',
  status: 'Saved' as const,
}

/** A consumer, so assertions are made on what the UI can actually render. */
function Consumer() {
  const { status, applications, createApplication } = useApplications()

  if (status !== 'ready') {
    return <p>{status === 'loading' ? 'Loading applications' : 'Could not load applications'}</p>
  }

  return (
    <div>
      <p>Ready · {applications.length} applications</p>
      <button type="button" onClick={() => void createApplication(draft)}>
        Add
      </button>
    </div>
  )
}

function setup(repository: ApplicationRepository, storageAvailable = true) {
  return render(
    <ApplicationsProvider repository={repository} storageAvailable={storageAvailable}>
      <StorageNotice />
      <Consumer />
    </ApplicationsProvider>,
  )
}

// BEHAVIOR-m2-persistence-seam-012, plus the loading / error states and the AC-5 notice that
// AGENTS.md makes part of Definition of Done for anything touching data flow.
describe('ApplicationsProvider', () => {
  it('exposes the seeded list once the repository resolves', async () => {
    setup(createInMemoryRepository(seedApplications))

    expect(screen.getByText('Loading applications')).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByText('Ready · 5 applications')).toBeInTheDocument()
    })
  })

  it('reflects a created application in the exposed list', async () => {
    const repository = createInMemoryRepository(seedApplications)
    setup(repository)

    await screen.findByText('Ready · 5 applications')
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    await screen.findByText('Ready · 6 applications')
    const listed = await repository.list()
    if (listed.ok) expect(listed.value).toHaveLength(6)
  })

  it('surfaces a failed load as an error state instead of an empty list', async () => {
    const failing: ApplicationRepository = {
      ...createInMemoryRepository(),
      async list(): Promise<Result<JobApplication[]>> {
        return { ok: false, error: { code: 'corrupt-data', quarantinedAs: null } }
      },
    }

    setup(failing)

    await screen.findByText('Could not load applications')
    expect(screen.queryByText(/Ready ·/)).not.toBeInTheDocument()
  })

  it('tells the user work will not be saved when storage is unavailable (AC-5)', async () => {
    setup(createInMemoryRepository(seedApplications), false)

    await screen.findByText(/not be saved/i)
  })

  it('renders nothing for the notice when storage works', async () => {
    setup(createInMemoryRepository(seedApplications), true)
    await screen.findByText('Ready · 5 applications')

    expect(screen.queryByText(/not be saved/i)).not.toBeInTheDocument()
  })

  it('refuses to answer useApplications() outside a provider', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    function Bare() {
      useApplications()
      return null
    }

    expect(() => render(<Bare />)).toThrow(/ApplicationsProvider/)
    consoleError.mockRestore()
  })
})
