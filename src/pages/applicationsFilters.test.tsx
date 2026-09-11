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

// BEHAVIOR-m2b-filters-cross-tab-020 — zero matches says "nothing matches this filter" with a
// clear action, never "no applications yet". An empty result and an empty account are different
// facts, and the second sentence tells the user to do something they have already done (spec B-6).
describe('when nothing matches', () => {
  it('blames the filter instead of claiming the account is empty', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    typeQuery('nothing-matches-this')

    expect(screen.getByRole('heading', { name: /no applications match/i })).toBeInTheDocument()
    expect(screen.queryByText('No job applications yet')).toBeNull()
    expect(screen.getByRole('status')).toHaveTextContent('0 of 5 applications')
  })

  it('keeps the controls on screen so the user is not trapped by their own query', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    typeQuery('nothing-matches-this')

    expect(screen.getByRole('searchbox', { name: /search applications/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Interview' })).toBeInTheDocument()
  })
})

// AC-10's live-region half. A polite region announces whatever it contains, so a button inside
// the count sentence is read to the user as part of the result — and screen-reader users get a
// control they cannot reliably reach from a live region.
describe('the result count region', () => {
  it('announces text only, with the clear control outside the region', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    fireEvent.click(screen.getByRole('button', { name: 'Rejected' }))

    const region = screen.getByRole('status')
    expect(region).toHaveTextContent('1 of 5 applications')
    expect(region.querySelector('button')).toBeNull()
  })
})

// BEHAVIOR-m2b-filters-cross-tab-021 — clearing restores the full list and leaves exactly one
// active control. A "clear" that resets the chips but leaves the query in the box is the worst
// outcome: the list stays short and the reason for it is invisible.
describe('clearing filters', () => {
  it('restores the whole pipeline from one press and leaves only All active', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    fireEvent.click(screen.getByRole('button', { name: 'Rejected' }))
    typeQuery('auckland')
    expect(cardTitles()).toEqual(['Software Developer'])

    fireEvent.click(screen.getByRole('button', { name: /^clear filters$/i }))

    expect(cardTitles()).toHaveLength(seedApplications.length)
    expect(screen.getByRole('searchbox', { name: /search applications/i })).toHaveValue('')
    expect(screen.getByRole('status')).toHaveTextContent('5 of 5 applications')

    const pressed = screen.getAllByRole('button', { pressed: true })
    expect(pressed.map((chip) => chip.textContent)).toEqual(['All'])
  })

  it('clears from the zero-match state too, without a second visible clear control', async () => {
    setup(createInMemoryRepository(seedApplications))
    await screen.findByText('Datacom')

    typeQuery('nothing-matches-this')

    // Exactly one control named "Clear filters" — two would make the announcement ambiguous.
    const clears = screen.getAllByRole('button', { name: /clear filters/i })
    expect(clears).toHaveLength(1)

    fireEvent.click(clears[0])

    expect(cardTitles()).toHaveLength(seedApplications.length)
    expect(screen.queryByRole('heading', { name: /no applications match/i })).toBeNull()
  })
})
