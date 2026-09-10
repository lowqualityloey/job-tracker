export type ApplicationStatus = 'Saved' | 'Applied' | 'Interview' | 'Rejected' | 'Offer'

export interface JobApplication {
  id: number
  companyName: string
  jobTitle: string
  location: string
  status: ApplicationStatus
  appliedAt?: string
  notes?: string
}

/**
 * The writable shape of an application: everything the user supplies, with the
 * repository-assigned fields (id, createdAt) removed. Defined as an explicit interface
 * rather than Omit<JobApplication, ...> for now, because JobApplication.id is still
 * `number`; the spec §4.1 contract swap happens with the repository in M2a.2.
 */
export interface ApplicationInput {
  companyName: string
  jobTitle: string
  location: string
  status: ApplicationStatus
  appliedAt?: string
  notes?: string
}
