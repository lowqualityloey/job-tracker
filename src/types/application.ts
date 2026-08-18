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
