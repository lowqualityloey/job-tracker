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
  /** Free text over company, title, location and notes. Empty string means "no query". */
  query: string
}

export const ALL_STATUSES: readonly StatusFilter[] = ['All', 'Saved', 'Applied', 'Interview', 'Rejected', 'Offer']

export const defaultCriteria: FilterCriteria = { status: 'All', query: '' }

/**
 * `'  AUCKLAND '` and `'auckland'` must behave identically, or the first thing a user types after
 * a paste ("it's not finding it", spec B-5) is a bug report about a feature that works.
 *
 * Trimmed at the ends only: collapsing inner whitespace would match a query for "rocket  lab"
 * against text that does not contain it, which is a different promise than the UI makes.
 */
export function normaliseQuery(value: string): string {
  return value.trim().toLowerCase()
}

/** The fields a query searches. Deliberately not every field: `status` has chips, and `id`/`createdAt` are not user-meaningful text. */
const SEARCHED_FIELDS = ['companyName', 'jobTitle', 'location', 'notes'] as const

export function matchesQuery(record: JobApplication, query: string): boolean {
  const needle = normaliseQuery(query)

  if (needle === '') {
    return true
  }

  return SEARCHED_FIELDS.some((field) => {
    const value = record[field]
    return typeof value === 'string' && value.toLowerCase().includes(needle)
  })
}

/**
 * The subset of records the criteria select, in the order the store returned them.
 *
 * Status and query combine with AND, not OR: pressing `Rejected` and typing "auckland" asks for
 * rejected applications in Auckland. Two independent filters that replaced each other would make
 * the count region lie about what the user selected.
 */
export function selectApplications(
  records: readonly JobApplication[],
  criteria: FilterCriteria,
): JobApplication[] {
  return records.filter(
    (record) => (criteria.status === 'All' || record.status === criteria.status) && matchesQuery(record, criteria.query),
  )
}
