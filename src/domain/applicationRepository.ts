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
/** BEHAVIOR-062: the two things a push channel can report. `error` is not a data event — it is the transport
 * giving up, which after M4 means "your session may have ended", a question that must be asked of the server exactly once. */
export type ExternalChangeReason = 'change' | 'error'

export type RepositoryError =
  | { code: 'validation'; fieldErrors: FieldError[] }
  | { code: 'not-found'; id: string }
  /**
   * The eighth variant, added by `DECISION-m3-backend-api-006` for the HTTP medium: the row changed underneath this
   * client between reading it and writing it back. No existing code carried that meaning — `validation` is about the
   * payload, `storage-error` about not being able to reach the store — and reusing either would tell the user to
   * edit their input when the right instruction is "reload and look at what someone else wrote".
   *
   * `quota-exceeded` and `unsupported-version` stay in the union even though HTTP can never produce them: they are
   * `localStorage` concepts, and §4.3 keeps them until the local adapter goes away.
   */
  | { code: 'conflict'; id: string }
  | { code: 'unavailable' }
  | { code: 'quota-exceeded' }
  | {
      code: 'corrupt-data'
      /** Where the unreadable payload was copied, or null when even that copy failed. */
      quarantinedAs: string | null
    }
  | { code: 'unsupported-version'; found: number }
  /**
   * The ninth variant. DECISION-m4-auth-005, approved by the owner on 2026-09-12 — the second widening of the union M2a
   * froze at seven, made on an explicit yes exactly as DECISION-006's first widening was. It exists because a 401 is none of
   * the other eight: `corrupt-data` says the response was unparseable, `unavailable` says the server could not be reached,
   * and `validation` says the user's input was wrong. Only this one means "what you are looking at belongs to a session that
   * has ended", and the behaviour that depends on the distinction — stop, do not retry, go and sign in — is exactly the
   * behaviour a retrying variant would break. `-060` is the row in the table; `-061` is the screen it redirects to.
   */
  | { code: 'unauthorized' }
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
  /**
   * Register a callback for data that changed **outside** this tab, and get back the function that
   * unregisters it. The repository owns the mechanism because the mechanism is a property of the
   * medium: `localStorage` answers with a `StorageEvent` filtered on its own key, an in-memory
   * store has nothing to report, and M3's HTTP client will answer with polling or a pushed event.
   *
   * The callback carries no payload on purpose. Whoever hears about a change is expected to
   * re-read through `list()`, so the same envelope validation, field-by-field rebuild, quarantine
   * path, and version gate run on every path into the UI — a `StorageEvent.newValue` shortcut
   * would bypass all four (M2b spec §4).
   */
  /**
   * Why this changed shape: BEHAVIOR-062. The callback now carries one bit of *how* — `change` (the data may differ)
   * or `error` (the transport failed) — because a 401 on a stream is a permanently failing connection the browser
   * retries forever, and "re-read" is not a question that can be asked of a dead session. The parameter is OPTIONAL
   * on purpose: M2b/M3 implementations that only ever report changes (the localStorage adapter, the in-memory test
   * repository) stay valid without editing a line, since a function taking fewer parameters is assignable to one
   * taking more. That keeps the widening additive instead of turning every adapter into a participant.
   */
  subscribe(onExternalChange: (reason?: ExternalChangeReason) => void): () => void
  list(): Promise<Result<JobApplication[]>>
  get(id: string): Promise<Result<JobApplication>>
  create(input: ApplicationInput): Promise<Result<JobApplication>>
  update(id: string, patch: ApplicationPatch): Promise<Result<JobApplication>>
  remove(id: string): Promise<Result<{ id: string }>>
}
