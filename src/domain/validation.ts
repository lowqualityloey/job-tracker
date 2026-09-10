import type { ApplicationInput } from '../types/application'

/** Maximum stored length for the short free-text fields. */
export const MAX_TEXT_LENGTH = 120

export type ValidationField = 'companyName' | 'jobTitle' | 'location' | 'status' | 'appliedAt' | 'notes'

export interface FieldError {
  field: ValidationField
  message: string
}

export type ValidationResult =
  | { ok: true; value: ApplicationInput }
  | { ok: false; fieldErrors: FieldError[] }

/**
 * Fields that must contain something after trimming, and never more than MAX_TEXT_LENGTH.
 * Data-driven on purpose: adding a sixth short-text field later means adding one row here,
 * not another if-block plus another test for the same shape of mistake.
 */
const ISO_DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/

/**
 * True only for 'YYYY-MM-DD' strings that name a real day on the proleptic Gregorian
 * calendar. The shape test alone is not enough: '2026-02-30' has the right shape, and
 * Date would roll it to 2 March — storing a date the user never typed. The round-trip
 * back through Date.UTC is what rejects it, and the same trick catches month 13,
 * day 32, and a two-digit-looking year that Date.UTC would reinterpret as 19xx.
 */
export function isIsoDateOnly(value: string): boolean {
  if (!ISO_DATE_ONLY.test(value)) {
    return false
  }

  const [year, month, day] = value.split('-').map(Number)
  const parsed = new Date(Date.UTC(year, month - 1, day))

  return (
    parsed.getUTCFullYear() === year &&
    parsed.getUTCMonth() === month - 1 &&
    parsed.getUTCDate() === day
  )
}

const REQUIRED_TEXT_FIELDS: ReadonlyArray<{ field: ValidationField; label: string }> = [
  { field: 'companyName', label: 'Company name' },
  { field: 'jobTitle', label: 'Job title' },
  { field: 'location', label: 'Location' },
]

/**
 * Validates a new or edited application before anything is written to the repository.
 *
 * Returns a discriminated Result rather than throwing: a failed validation is an expected
 * outcome of a user pressing Save, and the caller must narrow on `ok` before reading a
 * value, so it cannot be used by accident (PROMPTKIT.md: no silent error handling).
 *
 * Only the rules that have a failing test behind them are implemented. Status membership
 * and calendar-date validity arrive with their own Red -> Green cycles.
 */
export function validateApplication(input: ApplicationInput): ValidationResult {
  const fieldErrors: FieldError[] = []
  const value: ApplicationInput = {
    ...input,
    companyName: input.companyName.trim(),
    jobTitle: input.jobTitle.trim(),
    location: input.location.trim(),
    notes: input.notes?.trim(),
  }

  for (const { field, label } of REQUIRED_TEXT_FIELDS) {
    const text = value[field as 'companyName' | 'jobTitle' | 'location']

    if (text.length === 0) {
      fieldErrors.push({ field, message: `${label} is required.` })
    } else if (text.length > MAX_TEXT_LENGTH) {
      fieldErrors.push({
        field,
        message: `${label} must be ${MAX_TEXT_LENGTH} characters or fewer.`,
      })
    }
  }

  if (value.appliedAt !== undefined && !isIsoDateOnly(value.appliedAt)) {
    fieldErrors.push({
      field: 'appliedAt',
      message: 'Date applied must be a real date in YYYY-MM-DD form, or left empty.',
    })
  }

  if (fieldErrors.length > 0) {
    return { ok: false, fieldErrors }
  }

  return { ok: true, value }
}
