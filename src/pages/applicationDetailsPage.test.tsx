import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationDetailsPage from './ApplicationDetailsPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import type { ApplicationRepository } from '../domain/applicationRepository'
import { ApplicationsProvider } from '../state/applicationsProvider'

function setup(repo: ApplicationRepository, route: string) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <ApplicationsProvider repository={repo}>
        <Routes>
          <Route path="/applications/:id" element={<ApplicationDetailsPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
}

// BEHAVIOR-m2-persistence-seam-016 — closes DEBT-07.
describe('application details', () => {
  it('opens a record that exists only in storage, never in the seed constant', async () => {
    const repo = createInMemoryRepository(seedApplications)
    const created = await repo.create({
      companyName: 'Fishermend',
      jobTitle: 'Frontend Engineer',
      location: 'Auckland, NZ',
      status: 'Applied',
    })
    if (!created.ok) {
      throw new Error('seed repository refused the record under test')
    }

    setup(repo, `/applications/${created.value.id}`)

    expect(await screen.findByRole('heading', { level: 2, name: 'Frontend Engineer' })).toBeInTheDocument()
    expect(screen.getByText('Fishermend')).toBeInTheDocument()
  })

  it('says the record is missing when the id matches nothing', async () => {
    setup(createInMemoryRepository(seedApplications), '/applications/11111111-1111-4111-8111-111111111111')

    expect(await screen.findByRole('heading', { name: 'Application not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /back to applications/i })).toBeInTheDocument()
  })

  it('does not call a record "not found" while it is still being read', async () => {
    // The bug DEBT-07 actually describes: the lookup runs before the data exists, finds
    // nothing, and reports that as a missing record. A slow device turns every details page
    // into a false negative, and "not found" is a claim about the user's data, not a
    // placeholder for "I have not looked yet".
    const neverResolves: ApplicationRepository = {
      ...createInMemoryRepository(seedApplications),
      list: () => new Promise(() => {}),
    }

    setup(neverResolves, `/applications/${seedApplications[0].id}`)

    expect(await screen.findByText('Loading this application')).toBeInTheDocument()
    expect(screen.queryByText('Application not found')).toBeNull()
  })

  it('links to the editor for the record being viewed', async () => {
    const target = seedApplications[2]
    setup(createInMemoryRepository(seedApplications), `/applications/${target.id}`)

    await screen.findByRole('heading', { level: 2 })
    expect(screen.getByRole('link', { name: /^edit$/i })).toHaveAttribute(
      'href',
      `/applications/${target.id}/edit`,
    )
  })
})
