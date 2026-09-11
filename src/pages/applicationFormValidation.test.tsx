import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationFormPage from './ApplicationFormPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import { ApplicationsProvider } from '../state/applicationsProvider'

function renderForm(repo = createInMemoryRepository(seedApplications)) {
  render(
    <MemoryRouter initialEntries={['/applications/new']}>
      <ApplicationsProvider repository={repo}>
        <Routes>
          <Route path="/applications/new" element={<ApplicationFormPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
  return repo
}

function fill(field: string, value: string) {
  fireEvent.change(screen.getByLabelText(new RegExp(field, 'i')), { target: { value } })
}

// BEHAVIOR-m2-persistence-seam-014: an invalid submission explains itself per field and
// changes nothing. AC-9's "nothing is saved" half.
describe('application form — rejection', () => {
  it('names every missing required field and keeps the user on the form', async () => {
    renderForm()

    await screen.findByLabelText(/company name/i)
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    expect(await screen.findByText('Company name is required.')).toBeInTheDocument()
    expect(screen.getByText('Job title is required.')).toBeInTheDocument()
    expect(screen.getByText('Location is required.')).toBeInTheDocument()
    // Each message sits inside a live region, so a screen reader announces it without the
    // user having to move focus. Asserting the wrapper, not just the words.
    for (const message of ['Company name is required.', 'Job title is required.', 'Location is required.']) {
      expect(screen.getByText(message).closest('[role="alert"]')).not.toBeNull()
    }
    // Stayed on the form rather than navigating: the list page's heading is absent.
    expect(screen.queryByText('Your current pipeline')).toBeNull()
    expect(screen.getByRole('button', { name: /save application/i })).toBeEnabled()
  })

  it('shows the error next to the field it belongs to, and only that one', async () => {
    renderForm()

    await screen.findByLabelText(/company name/i)
    fill('Company', '   ')
    fill('Job title', 'Frontend Engineer')
    fill('Location', 'Auckland, NZ')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    expect(await screen.findByText('Company name is required.')).toBeInTheDocument()
    expect(screen.queryAllByRole('alert')).toHaveLength(1)
    const company = screen.getByLabelText(/company name/i)
    expect(company).toHaveAttribute('aria-invalid', 'true')
    expect(company).toHaveAttribute('aria-describedby', 'companyName-error')
  })

  // The date case as first written was not a Red, it was a wrong test. A native
  // <input type="date"> sanitises an impossible value to the empty string, so the browser
  // never lets '2026-02-30' reach the form. What is worth pinning at this seam is the
  // observable outcome: nothing bogus is stored, and the guard still exists one layer down
  // (validation.test.ts asserts the domain rejects that exact value, because M3 will receive
  // dates from a wire where no native control is watching).
  it('cannot be talked into storing an impossible date by the native control', async () => {
    const repo = renderForm()

    await screen.findByLabelText(/company name/i)
    fill('Company', 'Fishermend')
    fill('Job title', 'Frontend Engineer')
    fill('Location', 'Auckland, NZ')
    fill('Date applied', '2026-02-30')
    expect(screen.getByLabelText(/date applied/i)).toHaveValue('')

    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await waitFor(async () => {
      const listed = await repo.list()
      if (listed.ok) {
        expect(listed.value).toHaveLength(6)
        expect(listed.value[5].appliedAt).toBeUndefined()
      }
    })
    expect(screen.queryByText(/real date in YYYY-MM-DD/)).toBeNull()
  })

  it('refuses a submission the moment it is over the length cap', async () => {
    renderForm()

    await screen.findByLabelText(/company name/i)
    fill('Company', 'a'.repeat(121))
    fill('Job title', 'Frontend Engineer')
    fill('Location', 'Auckland, NZ')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    expect(await screen.findByText('Company name must be 120 characters or fewer.')).toBeInTheDocument()
  })

  it('clears the previous rejection once the field is fixed', async () => {
    const repo = renderForm()

    await screen.findByLabelText(/company name/i)
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))
    await screen.findByText('Company name is required.')

    fill('Company', 'Fishermend')
    fill('Job title', 'UI Engineer')
    fill('Location', 'Christchurch, NZ')
    fireEvent.click(screen.getByRole('button', { name: /save application/i }))

    await waitFor(async () => {
      const listed = await repo.list()
      if (listed.ok) expect(listed.value).toHaveLength(6)
    })
    expect(screen.queryAllByRole('alert')).toHaveLength(0)
  })
})
