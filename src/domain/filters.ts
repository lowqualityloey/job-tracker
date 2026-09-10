import type { ApplicationStatus, JobApplication } from '../types/application'

/**
 * What the user is currently looking at, as opposed to what the store holds.
 *
 * `'All'` is a member of the criteria type rather than `undefined` because "no status chosen"
 * and "the user chose to see everything" are the same state here, and an optional field invites
 * a caller to distinguish them.
 */
export type StatusFilter = ApplicationStatus | 'All'

export interface FilterCriteria {
  status: StatusFilter
}

export const ALL_STATUSES: readonly StatusFilter[] = ['All', 'Saved', 'Applied', 'Interview', 'Rejected', 'Offer']

export const defaultCriteria: FilterCriteria = { status: 'All' }

/**
 * The subset of records the criteria select, in the order the store returned them.
 *
 * Pure and total on purpose: it takes records and returns records, so the same predicate is what
 * M3 can mirror server-side (`WHERE status = $1`) instead of a second, drifting copy in C#. It is
 * also the only place a matching rule can live — inlining it into the page would scatter the rules
 * across every component that lists applications.
 *
 * No sorting happens here. Order is store order, and adding an implicit sort would change what
 * `BEHAVIOR-018` promises without anyone asking for it (spec §2 non-goals, B-10).
 */
export function selectApplications(
  records: readonly JobApplication[],
  criteria: FilterCriteria,
): JobApplication[] {
  if (criteria.status === 'All') {
    return [...records]
  }

  return records.filter((record) => record.status === criteria.status)
}
