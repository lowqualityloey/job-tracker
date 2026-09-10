export type ApplicationStatus = 'Saved' | 'Applied' | 'Interview' | 'Rejected' | 'Offer'

export interface JobApplication {
  /**
   * v4 UUID string, assigned by the repository on create. Changed from `number` in M2a
   * (spec §4.1 / DECISION-m2-persistence-seam-002) while no user data exists to migrate:
   * it is free now and expensive later, because an ASP.NET Core backend hands back string
   * identifiers and numeric ids would then need a migration over stored records.
   */
  id: string
  companyName: string
  jobTitle: string
  location: string
  status: ApplicationStatus
  /** Optional date-only ISO 8601 ('YYYY-MM-DD'), validated by domain/validation. */
  appliedAt?: string
  notes?: string
  /** ISO 8601 timestamp assigned on create; drives newest-first ordering and is never rewritten. */
  createdAt: string
}

/**
 * What the user supplies. Derived from the record itself so a future field cannot be added to
 * JobApplication without forcing this file's authors to notice the new writable surface.
 */
export type ApplicationInput = Omit<JobApplication, 'id' | 'createdAt'>
export type ApplicationPatch = Partial<ApplicationInput>
