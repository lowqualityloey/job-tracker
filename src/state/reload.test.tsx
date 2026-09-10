import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationsPage from '../pages/ApplicationsPage'
import {
  APPLICATIONS_STORAGE_KEY,
  createLocalStorageRepository,
} from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import { ApplicationsProvider } from './applicationsProvider'

beforeEach(() => {
  localStorage.clear()
})

function mount() {
  // A fresh repository instance every time: that is the reload. Nothing is shared between
  // the two mounts except the bytes sitting in jsdom's Storage.
  return render(
    <MemoryRouter initialEntries={['/applications']}>
      <ApplicationsProvider repository={createLocalStorageRepository({ storage: localStorage })}>
        <Routes>
          <Route path="/applications" element={<ApplicationsPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
}

// AC-11's automatable half: proves persistence through the real app wiring rather than
// through a repository call in isolation. The manual browser check stays a named residual.
describe('the application survives a reload', () => {
  it('seeds on first run, then reads back a record the next mount never had in memory', async () => {
    mount()
    await screen.findByText('Datacom')
    expect(screen.queryByText('Fishermend Limited')).toBeNull()

    const writer = createLocalStorageRepository({ storage: localStorage })
    await writer.create({
      companyName: 'Fishermend Limited',
      jobTitle: 'Staff Frontend Engineer',
      location: 'Auckland, NZ',
      status: 'Interview',
    })

    unmountAll()
    mount()

    expect(await screen.findByText('Fishermend Limited')).toBeInTheDocument()
    expect(screen.getByText('Datacom')).toBeInTheDocument()
  })

  it('writes nothing to memory that the store does not hold', async () => {
    await createLocalStorageRepository({ storage: localStorage }).create({
      companyName: 'Solo Devices',
      jobTitle: 'UI Engineer',
      location: 'Wellington, NZ',
      status: 'Saved',
    })

    const stored = JSON.parse(String(localStorage.getItem(APPLICATIONS_STORAGE_KEY))) as {
      schemaVersion: number
      applications: JobApplicationRef[]
    }
    expect(stored.schemaVersion).toBe(1)
    expect(stored.applications.some((record) => record.companyName === 'Solo Devices')).toBe(true)
  })
})

type JobApplicationRef = { companyName: string }

function unmountAll() {
  // React 18 roots unmount via the container; here the simple stand-in is that a fresh
  // render with a new repository cannot see the previous tree's state.
  document.body.innerHTML = ''
}
