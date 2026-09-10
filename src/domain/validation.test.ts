import { validateApplication, type ValidationResult } from './validation'
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

// BEHAVIOR-m2-persistence-seam-002
// Required text fields must be rejected when empty, when they contain only whitespace
// (which trimming would silently turn into ''), and when they exceed the stored length
// bound. Each failure must name the offending field so the form can announce it inline.
describe('validateApplication — required and bounded text', () => {
  it('rejects an empty company name', () => {
    const result = validateApplication(base({ companyName: '' }))
    expect(result.ok).toBe(false)
    if (!result.ok) expect(errorFields(result)).toContain('companyName')
  })

  it('rejects a company name that is only whitespace', () => {
    const result = validateApplication(base({ companyName: '    ' }))
    expect(result.ok).toBe(false)
    if (!result.ok) expect(errorFields(result)).toContain('companyName')
  })

  it('rejects a company name longer than the agreed maximum', () => {
    const result = validateApplication(base({ companyName: 'x'.repeat(121) }))
    expect(result.ok).toBe(false)
    if (!result.ok) expect(errorFields(result)).toContain('companyName')
  })

  it('rejects an empty job title without reporting the company name', () => {
    const result = validateApplication(base({ jobTitle: '' }))
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(errorFields(result)).toContain('jobTitle')
      expect(errorFields(result)).not.toContain('companyName')
    }
  })

  it('accepts a company name at exactly the maximum length', () => {
    expect(validateApplication(base({ companyName: 'x'.repeat(120) })).ok).toBe(true)
  })
})

function base(overrides: Partial<ApplicationInput> = {}): ApplicationInput {
  return {
    companyName: 'Northwind Robotics',
    jobTitle: 'Senior Frontend Engineer',
    location: 'Remote (EU)',
    status: 'Applied',
    ...overrides,
  }
}

function errorFields(result: ValidationResult): string[] {
  if (result.ok) throw new Error('expected a failed validation result')
  return result.fieldErrors.map((error) => error.field)
}

// BEHAVIOR-m2-persistence-seam-003
// appliedAt is optional, but when present it must be a real calendar date. String
// shape checks alone let '2026-02-30' through, and Date would silently roll it into
// March — the stored record would then disagree with what the user typed.
describe('validateApplication — appliedAt date', () => {
  it('accepts a real date', () => {
    expect(validateApplication(base({ appliedAt: '2026-02-28' })).ok).toBe(true)
  })

  it('accepts the field being absent', () => {
    const result = validateApplication(base({ appliedAt: undefined }))
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value.appliedAt).toBeUndefined()
  })

  it('rejects a date that does not exist on the calendar', () => {
    const result = validateApplication(base({ appliedAt: '2026-02-30' }))
    expect(result.ok).toBe(false)
    if (!result.ok) expect(errorFields(result)).toContain('appliedAt')
  })

  it('rejects a wrongly formatted date', () => {
    for (const bad of ['2026/02/28', '28-02-2026', '2026-02', 'not-a-date', '2026-2-8']) {
      const result = validateApplication(base({ appliedAt: bad }))
      expect(result.ok).toBe(false)
      if (!result.ok) expect(errorFields(result)).toContain('appliedAt')
    }
  })

  it('rejects a future-free-form value that Date would coerce', () => {
    const result = validateApplication(base({ appliedAt: '2026-02-28T10:00:00Z' }))
    expect(result.ok).toBe(false)
    if (!result.ok) expect(errorFields(result)).toContain('appliedAt')
  })
})
