import { validateApplication } from './validation'
import type { ApplicationInput } from '../types/application'

// BEHAVIOR-m2-persistence-seam-001
// A well-formed application must be accepted, and surrounding whitespace must not
// survive into stored data (it would break comparisons and look wrong in the UI).
describe('validateApplication', () => {
  it('accepts a valid application and normalises surrounding whitespace', () => {
    const input: ApplicationInput = {
      companyName: '  Northwind Robotics  ',
      jobTitle: ' Senior Frontend Engineer ',
      location: 'Remote (EU)',
      status: 'Applied',
      appliedAt: '2026-09-03',
      notes: ' Referral from Ana. ',
    }

    const result = validateApplication(input)

    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.value.companyName).toBe('Northwind Robotics')
      expect(result.value.jobTitle).toBe('Senior Frontend Engineer')
      expect(result.value.notes).toBe('Referral from Ana.')
    }
  })
})
