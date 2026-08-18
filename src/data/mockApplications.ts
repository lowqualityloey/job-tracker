import type { JobApplication } from '../types/application'

export const mockApplications: JobApplication[] = [
  {
    id: 1,
    companyName: 'Xero',
    jobTitle: 'Junior Frontend Developer',
    location: 'Wellington, NZ',
    status: 'Applied',
    appliedAt: '2026-08-10',
    notes: 'Strong React and TypeScript fit.',
  },
  {
    id: 2,
    companyName: 'Datacom',
    jobTitle: 'Graduate Software Engineer',
    location: 'Hamilton, NZ',
    status: 'Interview',
    appliedAt: '2026-08-05',
    notes: 'Prepare examples of teamwork and API work.',
  },
  {
    id: 3,
    companyName: 'Trade Me',
    jobTitle: 'Frontend Developer',
    location: 'Remote, NZ',
    status: 'Saved',
    notes: 'Review product design and user experience examples.',
  },
  {
    id: 4,
    companyName: 'Rocket Lab',
    jobTitle: 'Software Developer',
    location: 'Auckland, NZ',
    status: 'Rejected',
    appliedAt: '2026-07-29',
    notes: 'Good practice for interview follow-up notes.',
  },
  {
    id: 5,
    companyName: 'BNZ',
    jobTitle: 'Full Stack Developer',
    location: 'Auckland, NZ',
    status: 'Offer',
    appliedAt: '2026-08-01',
    notes: 'Track salary, benefits, and response deadline.',
  },
]
