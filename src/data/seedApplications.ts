import type { JobApplication } from '../types/application'

/**
 * Demo records used to seed an empty store on first run. Ids are FIXED literal UUIDs, not
 * crypto.randomUUID() calls: a generated id would change every load and make tests that assert
 * on ordering or on a specific record impossible. createdAt values are literals for the same reason.
 *
 * Replaces src/data/mockApplications.ts, which was a module-scope fixture the UI rendered directly.
 */
export const seedApplications: JobApplication[] = [
  {
    id: 'd36b4d5f-291b-40c0-b96c-c1a61a23bf93',
    companyName: 'Xero',
    jobTitle: 'Junior Frontend Developer',
    location: 'Wellington, NZ',
    status: 'Applied',
    appliedAt: '2026-08-10',
    notes: 'Strong React and TypeScript fit.',
    createdAt: '2026-08-10T09:00:00.000Z',
  },
  {
    id: '037d4bd5-ffd4-48f3-ad73-0c3a0ca8c2b3',
    companyName: 'Datacom',
    jobTitle: 'Graduate Software Engineer',
    location: 'Hamilton, NZ',
    status: 'Interview',
    appliedAt: '2026-08-05',
    notes: 'Prepare examples of teamwork and API work.',
    createdAt: '2026-08-08T09:00:00.000Z',
  },
  {
    id: '6b2a7f52-ad38-4fca-a3fe-4d95437be9f9',
    companyName: 'Trade Me',
    jobTitle: 'Frontend Developer',
    location: 'Remote, NZ',
    status: 'Saved',
    notes: 'Review product design and user experience examples.',
    createdAt: '2026-08-07T09:00:00.000Z',
  },
  {
    id: '811017d1-0038-4eca-aecd-0990b1590a9f',
    companyName: 'Rocket Lab',
    jobTitle: 'Software Developer',
    location: 'Auckland, NZ',
    status: 'Rejected',
    appliedAt: '2026-07-29',
    notes: 'Good practice for interview follow-up notes.',
    createdAt: '2026-07-29T09:00:00.000Z',
  },
  {
    id: '34247b24-960c-4601-a1c8-e64e0e255660',
    companyName: 'BNZ',
    jobTitle: 'Full Stack Developer',
    location: 'Auckland, NZ',
    status: 'Offer',
    appliedAt: '2026-08-01',
    notes: 'Track salary, benefits, and response deadline.',
    createdAt: '2026-08-01T09:00:00.000Z',
  },
]
