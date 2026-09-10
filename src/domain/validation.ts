import type { ApplicationInput } from '../types/application'

export type ValidationResult =
  | { ok: true; value: ApplicationInput }
  | { ok: false; fieldErrors: unknown[] }

/**
 * Validates a new or edited application.
 *
 * Deliberately minimal: this implementation exists only to turn
 * BEHAVIOR-m2-persistence-seam-001 Green (accept a valid application, trim whitespace).
 * Required-field, length, status, and calendar-date rules arrive as their own
 * Red -> Green cycles, not pre-emptively.
 */
export function validateApplication(input: ApplicationInput): ValidationResult {
  return {
    ok: true,
    value: {
      ...input,
      companyName: input.companyName.trim(),
      jobTitle: input.jobTitle.trim(),
      location: input.location.trim(),
      notes: input.notes?.trim(),
    },
  }
}
