import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationsPage from '../pages/ApplicationsPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import type { ApplicationRepository } from '../domain/applicationRepository'
import { ApplicationsProvider } from '../state/applicationsProvider'

function setup(repo: ApplicationRepository) {
  render(
    <MemoryRouter initialEntries={['/applications']}>
      <ApplicationsProvider repository={repo}>
        <Routes>
          <Route path="/applications" element={<ApplicationsPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
}

/** The rendered cards, in the order the page shows them, keyed by job title. */
const cardTitles = () => screen.getAllByRole('article').map((card) => card.querySelector('h3')?.textContent)

// BEHAVIOR-m2b-filters-cross-tab-018 — a status chip narrows the list to matching records
// and announces the count. Assertions are on rendered cards, never on criteria state.
describe('filtering the applications list by status', () => {
  it('shows only the Interview records once the Interview chip is pressed', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    expect(cardTitles()).toHaveLength(seedApplications.length)

    fireEvent.click(screen.getByRole('button', { name: 'Interview' }))

    expect(cardTitles()).toEqual(['Graduate Software Engineer'])
    expect(screen.getByText('Datacom')).toBeInTheDocument()
    expect(screen.queryByText('Xero')).toBeNull()
  })

  it('announces how many of the total are shown', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    // The count is present before any filter too, so the live region is one code path
    // rather than a branch that only appears once the user has narrowed things.
    expect(screen.getByRole('status')).toHaveTextContent('5 of 5 applications')

    fireEvent.click(screen.getByRole('button', { name: 'Rejected' }))

    expect(screen.getByRole('status')).toHaveTextContent('1 of 5 applications')
  })
})

function typeQuery(value: string) {
  fireEvent.change(screen.getByRole('searchbox', { name: /search applications/i }), {
    target: { value },
  })
}

// BEHAVIOR-m2b-filters-cross-tab-019 — a query matches company, title, location and notes,
// insensitive to case and to surrounding whitespace.
describe('searching the applications list', () => {
  it.each([
    ['trade', ['Frontend Developer']], // companyName: "Trade Me"
    ['GRADUATE', ['Graduate Software Engineer']], // jobTitle, wrong case
    ['auckland', ['Software Developer', 'Full Stack Developer']], // location, two records
    ['typescript', ['Junior Frontend Developer']], // notes only — no other field says it
  ])('narrows to the records containing "%s" in any searched field', async (query, expected) => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    typeQuery(query)

    expect(cardTitles()).toEqual(expected)
    expect(screen.getByRole('status')).toHaveTextContent(`${expected.length} of 5 applications`)
  })

  it('ignores surrounding whitespace', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    typeQuery('   remote  ')

    expect(cardTitles()).toEqual(['Frontend Developer'])
  })

  it('combines with a status chip rather than replacing it', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    fireEvent.click(screen.getByRole('button', { name: 'Rejected' }))
    typeQuery('auckland')

    expect(cardTitles()).toEqual(['Software Developer'])
    expect(screen.getByRole('status')).toHaveTextContent('1 of 5 applications')

    // The status chip stays pressed while the query narrows inside it: two controls, one result.
    expect(screen.getByRole('button', { name: 'Rejected' })).toHaveAttribute('aria-pressed', 'true')
  })
})
