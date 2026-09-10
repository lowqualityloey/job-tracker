import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationFormPage from './ApplicationFormPage'
import ApplicationsPage from './ApplicationsPage'
import DashboardPage from './DashboardPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import { ApplicationsProvider } from '../state/applicationsProvider'
import type { ApplicationRepository } from '../domain/applicationRepository'

const seedInterviews = seedApplications.filter((item) => item.status === 'Interview').length

function stat(label: string): HTMLElement {
  const card = screen.getByText(label).closest('.stat-card')
  if (!card) {
    throw new Error(`no stat card for ${label}`)
  }
  return card.querySelector('strong') as HTMLElement
}

function setup(repo: ApplicationRepository, route = '/applications/new') {
  render(
    <MemoryRouter initialEntries={[route]}>
      <ApplicationsProvider repository={repo}>
        <Link to="/">Go to dashboard</Link>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/applications" element={<ApplicationsPage />} />
          <Route path="/applications/new" element={<ApplicationFormPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
}

function fill(field: string, value: string) {
  fireEvent.change(screen.getByLabelText(new RegExp(field, 'i')), { target: { value } })
}

// BEHAVIOR-m2-persistence-seam-015 — closes DEBT-05: the dashboard's counts were computed
// once at module scope from the seed constant, so they could never move.
describe('dashboard counts derive from live data', () => {
  it('shows the seeded pipeline before anything changes', async () => {
    setup(createInMemoryRepository(seedApplications), '/')

    await waitFor(() => expect(screen.getByText('Total applications')).toBeInTheDocument())
    expect(stat('Total applications').textContent).toBe(String(seedApplications.length))
    expect(stat('Interviews').textContent).toBe(String(seedInterviews))
  })

  it('counts an application created from the form, without a reload', async () => {
    const repo = createInMemoryRepository(seedApplications)
    setup(repo)

    await screen.findByLabelText(/company name/i)
    fill('Company', 'Fishermend')
    fill('Job title', 'Frontend Engineer')
    fill('Location', 'Auckland, NZ')
    fill('Status', 'Interview')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await screen.findByText('Fishermend')
    fireEvent.click(screen.getByText('Go to dashboard'))

    await waitFor(() => expect(stat('Total applications').textContent).toBe('6'))
    expect(stat('Interviews').textContent).toBe(String(seedInterviews + 1))
  })

  it('reports a failed load instead of showing zeros as if they were counts', async () => {
    const failing: ApplicationRepository = {
      ...createInMemoryRepository(),
      async list() {
        return { ok: false, error: { code: 'unavailable' } }
      },
    }
    setup(failing, '/')

    // Role-scoped: a bare text regex matched the heading and the description, so this
    // assertion was failing on ambiguity rather than on the missing error branch.
    await screen.findByRole('heading', { name: 'Your applications could not be read' })
    expect(screen.queryByText('Total applications')).toBeNull()
  })
})
