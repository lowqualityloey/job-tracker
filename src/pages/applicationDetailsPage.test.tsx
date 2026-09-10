import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationDetailsPage from './ApplicationDetailsPage'
import ApplicationsPage from './ApplicationsPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import type { ApplicationRepository } from '../domain/applicationRepository'
import { ApplicationsProvider } from '../state/applicationsProvider'

function setup(repo: ApplicationRepository, route: string) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <ApplicationsProvider repository={repo}>
        <Routes>
          <Route path="/applications" element={<ApplicationsPage />} />
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

// BEHAVIOR-m2-persistence-seam-017 — deletion is destructive and unrecoverable in this app:
// there is no trash, no undo, and the only copy is the one in storage.
describe('deleting an application', () => {
  const open = async (repo: ApplicationRepository, index = 0) => {
    setup(repo, `/applications/${seedApplications[index].id}`)
    await screen.findByRole('heading', { level: 2 })
  }

  it('asks before removing anything, and removing needs the second click', async () => {
    const repo = createInMemoryRepository(seedApplications)
    await open(repo)

    fireEvent.click(screen.getByRole('button', { name: /^delete$/i }))

    expect(
      await screen.findByText(/delete this application\? this cannot be undone\./i),
    ).toBeInTheDocument()
    const listed = await repo.list()
    if (listed.ok) expect(listed.value).toHaveLength(5)
    expect(screen.queryByRole('heading', { name: 'Application not found' })).toBeNull()
  })

  it('keeps the record when the confirmation is cancelled', async () => {
    const repo = createInMemoryRepository(seedApplications)
    await open(repo)

    fireEvent.click(screen.getByRole('button', { name: /^delete$/i }))
    fireEvent.click(await screen.findByRole('button', { name: /cancel/i }))

    expect(screen.getByRole('heading', { level: 2 })).toBeInTheDocument()
    const listed = await repo.list()
    if (listed.ok) expect(listed.value).toHaveLength(5)
  })

  it('removes the record from storage and returns to the list', async () => {
    const repo = createInMemoryRepository(seedApplications)
    const target = seedApplications[1]
    setup(repo, `/applications/${target.id}`)
    await screen.findByRole('heading', { level: 2 })
    const title = (screen.getByRole('heading', { level: 2 }) as HTMLElement).textContent

    fireEvent.click(screen.getByRole('button', { name: /^delete$/i }))
    fireEvent.click(await screen.findByRole('button', { name: /delete application/i }))

    await waitFor(async () => {
      const listed = await repo.list()
      if (listed.ok) expect(listed.value.find((r) => r.id === target.id)).toBeUndefined()
    })
    expect(await screen.findByRole('heading', { name: 'Your current pipeline' })).toBeInTheDocument()
    expect(screen.queryByText(title)).toBeNull()
  })

  it('says so when the store refuses the deletion, instead of dropping the row anyway', async () => {
    const refusing: ApplicationRepository = {
      ...createInMemoryRepository(seedApplications),
      async remove() {
        return { ok: false, error: { code: 'storage-error', detail: 'write rejected' } }
      },
    }
    setup(refusing, `/applications/${seedApplications[0].id}`)
    await screen.findByRole('heading', { level: 2 })

    fireEvent.click(screen.getByRole('button', { name: /^delete$/i }))
    fireEvent.click(await screen.findByRole('button', { name: /delete application/i }))

    const refusal = await screen.findByText(/could not be deleted/i)
    expect(refusal.closest('[role="alert"]')).not.toBeNull()
    expect(screen.getByRole('heading', { level: 2 })).toBeInTheDocument()
  })

  it('puts the confirmation in a group a screen reader can find', async () => {
    setup(createInMemoryRepository(seedApplications), `/applications/${seedApplications[0].id}`)
    await screen.findByRole('heading', { level: 2 })

    fireEvent.click(screen.getByRole('button', { name: /^delete$/i }))

    expect(await screen.findByRole('group', { name: /confirm deletion/i })).toBeInTheDocument()
  })
})
