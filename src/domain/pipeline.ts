import type { ApplicationStatus, JobApplication } from '../types/application'

export interface PipelineSummary {
  total: number
  byStatus: Record<ApplicationStatus, number>
  interviews: number
  offers: number
}

/**
 * Every count the overview shows, from one pass over the applications it describes.
 *
 * This is the whole point of `domain/` existing: the numbers are business facts, not view
 * arithmetic. Before this module the dashboard filtered the seed constant three times at
 * module scope (DEBT-05), which meant the figures could not change while the page was open.
 * A pure function here is also the cheapest thing in the codebase to test against an API
 * payload in M3, because it takes records and returns numbers.
 */
export function summarise(applications: readonly JobApplication[]): PipelineSummary {
  const byStatus: Record<ApplicationStatus, number> = {
    Saved: 0,
    Applied: 0,
    Interview: 0,
    Offer: 0,
    Rejected: 0,
  }

  for (const application of applications) {
    byStatus[application.status] += 1
  }

  return { total: applications.length, byStatus, interviews: byStatus.Interview, offers: byStatus.Offer }
}
