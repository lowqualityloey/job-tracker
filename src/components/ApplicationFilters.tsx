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
 * why it takes `onChange` for a whole criteria object instead of one setter per control — a filter
 * that combines (status **and** query) has to be changed as a unit.
 *
 * Chips are `<button>`s with `aria-pressed`, not `<input type="radio">`: the state being conveyed is
 * "this control is currently selecting", which is exactly what `aria-pressed` means, and it keeps the
 * single-action clear (`press All`) the same shape as every other press.
 */
export default function ApplicationFilters({ criteria, onChange }: ApplicationFiltersProps) {
  function select(status: StatusFilter) {
    onChange({ ...criteria, status })
  }

  return (
    <div className="filter-bar">
      <div className="chip-row" role="group" aria-label="Filter by status">
        {ALL_STATUSES.map((option) => (
          <button
            key={option}
            type="button"
            className="chip"
            aria-pressed={criteria.status === option}
            onClick={() => select(option)}
          >
            {option}
          </button>
        ))}
      </div>
    </div>
  )
}
