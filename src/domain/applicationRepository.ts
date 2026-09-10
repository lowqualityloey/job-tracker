import type { ApplicationInput, ApplicationPatch, ApplicationStatus, JobApplication } from '../types/application'
import type { FieldError } from './validation'

export const APPLICATION_STATUSES: readonly ApplicationStatus[] = [
  'Saved',
  'Applied',
  'Interview',
  'Rejected',
  'Offer',
]

/**
 * Every way a repository call can fail, as data rather than as an exception. The UI switches
 * on `code` to decide what to tell the user, which is the point: 'quota-exceeded' and
 * 'not-found' need completely different messages, and a bare Error message string cannot be
 * switched on safely.
 */
export type RepositoryError =
  | { code: 'validation'; fieldErrors: FieldError[] }
  | { code: 'not-found'; id: string }
  | { code: 'unavailable' }
  | { code: 'quota-exceeded' }
  | {
      code: 'corrupt-data'
      /** Where the unreadable payload was copied, or null when even that copy failed. */
      quarantinedAs: string | null
    }
  | { code: 'unsupported-version'; found: number }
  | { code: 'storage-error'; detail: string }

export type Result<T, E = RepositoryError> = { ok: true; value: T } | { ok: false; error: E }

export const ok = <T,>(value: T): Result<T, never> => ({ ok: true, value })
export const err = <E>(error: E): Result<never, E> => ({ ok: false, error })

/**
 * The seam. Deliberately async even though localStorage is synchronous: the shape is chosen
 * for what replaces it (M3's HTTP client), not for what it wraps today, so the swap changes
 * one implementation file and no call site.
 */
export interface ApplicationRepository {
  list(): Promise<Result<JobApplication[]>>
  get(id: string): Promise<Result<JobApplication>>
  create(input: ApplicationInput): Promise<Result<JobApplication>>
  update(id: string, patch: ApplicationPatch): Promise<Result<JobApplication>>
  remove(id: string): Promise<Result<{ id: string }>>
}
