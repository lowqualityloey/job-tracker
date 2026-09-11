import type { FilterCriteria, StatusFilter } from '../domain/filters'
import { ALL_STATUSES } from '../domain/filters'

interface ApplicationFiltersProps {
  criteria: FilterCriteria
  onChange: (criteria: FilterCriteria) => void
}

/**
 * The controls that narrow the list.
 *
 * Controlled: criteria state belongs to the page that renders the results, so the predicate and the
 * rows can never disagree by living in two places. This component is presentational, which is also
 * why it takes `onChange` for a whole criteria object instead of one setter per control — the two
 * filters combine, so they are changed as a unit.
 *
 * Chips are `<button>`s with `aria-pressed`, not `<input type="radio">`: the state being conveyed is
 * "this control is currently selecting", which is exactly what `aria-pressed` means, and it keeps
 * `All` — the single-press clear — the same shape as every other press.
 */
export default function ApplicationFilters({ criteria, onChange }: ApplicationFiltersProps) {
  function selectStatus(status: StatusFilter) {
    onChange({ ...criteria, status })
  }

  function setQuery(query: string) {
    onChange({ ...criteria, query })
  }

  return (
    <div className="filter-bar" role="search" aria-label="Filter applications">
      <div className="field">
        <label className="field-label" htmlFor="applications-search">
          Search applications
        </label>
        {/* type="search" gives the searchbox role and the UA's own clear affordance. The live count
            below the list is the feedback, so there is no onSubmit: filtering is immediate, and an
            Enter key that did nothing but reload would be the worse affordance. */}
        <input
          id="applications-search"
          className="filter-input"
          type="search"
          value={criteria.query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Company, title, location, or notes"
        />
      </div>

      <div className="chip-row" role="group" aria-label="Filter by status">
        {ALL_STATUSES.map((option) => (
          <button
            key={option}
            type="button"
            className="chip"
            aria-pressed={criteria.status === option}
            onClick={() => selectStatus(option)}
          >
            {option}
          </button>
        ))}
      </div>
    </div>
  )
}
