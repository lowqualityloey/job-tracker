import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationFormPage from './ApplicationFormPage'
import ApplicationsPage from './ApplicationsPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import type { ApplicationRepository } from '../domain/applicationRepository'
import { ApplicationsProvider } from '../state/applicationsProvider'

const repository = () => createInMemoryRepository(seedApplications)

function renderAt(route: string, repo: ApplicationRepository) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <ApplicationsProvider repository={repo}>
        <Routes>
          <Route path="/applications" element={<ApplicationsPage />} />
          <Route path="/applications/new" element={<ApplicationFormPage />} />
          <Route path="/applications/:id/edit" element={<ApplicationFormPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
}

function fill(field: string, value: string) {
  fireEvent.change(screen.getByLabelText(new RegExp(field, 'i')), { target: { value } })
}

// BEHAVIOR-m2-persistence-seam-013 — and the edit path alongside it: the task objective names
// create/edit/delete, but spec §6 gave the UI edit flow no behaviour id of its own, so it is
// covered here rather than left untested. That gap is recorded, not papered over.
describe('application form — create', () => {
  it('saves a valid application and the list shows it', async () => {
    const repo = repository()
    renderAt('/applications/new', repo)

    fill('Company', 'Fishermend')
    fill('Job title', 'Frontend Engineer')
    fill('Location', 'Auckland, NZ')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await waitFor(() => expect(screen.getByText('Fishermend')).toBeInTheDocument())
    const listed = await repo.list()
    if (listed.ok) expect(listed.value).toHaveLength(6)
  })

  it('leaves an optional field out and still saves', async () => {
    const repo = repository()
    renderAt('/applications/new', repo)

    fill('Company', 'Solo Devices')
    fill('Job title', 'UI Engineer')
    fill('Location', 'Wellington, NZ')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await screen.findByText('Solo Devices')
    const listed = await repo.list()
    if (listed.ok) expect(listed.value[5].appliedAt).toBeUndefined()
  })
})

describe('application form — edit', () => {
  it('opens with the stored values already filled in', async () => {
    const target = seedApplications[1]
    renderAt(`/applications/${target.id}/edit`, repository())

    await waitFor(() =>
      expect(screen.getByLabelText(/company name/i)).toHaveValue(target.companyName),
    )
    expect(screen.getByLabelText(/job title/i)).toHaveValue(target.jobTitle)
  })

  it('saves a change to the same record without adding another one', async () => {
    const repo = repository()
    const target = seedApplications[1]
    renderAt(`/applications/${target.id}/edit`, repo)

    await screen.findByLabelText(/company name/i)
    fill('Company', 'Datacom Group')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await screen.findByText('Datacom Group')
    const listed = await repo.list()
    if (listed.ok) {
      expect(listed.value).toHaveLength(5)
      expect(listed.value.find((r) => r.id === target.id)?.companyName).toBe('Datacom Group')
    }
  })
})
