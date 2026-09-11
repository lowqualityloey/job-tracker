import type { ApplicationInput, ApplicationPatch, ApplicationStatus, JobApplication } from '../types/application'
import { APPLICATION_STATUSES, err, ok } from '../domain/applicationRepository'
import type { ApplicationRepository, RepositoryError, Result } from '../domain/applicationRepository'
import { seedApplications } from './seedApplications'

export const APPLICATIONS_STORAGE_KEY = 'job-tracker:applications'

/** Name chosen so a collision with real user data is impossible, and greppable when it isn't. */
const AVAILABILITY_PROBE_KEY = 'job-tracker:availability-probe'

/** The slice of the Web Storage API this module uses. Injectable so faults are testable. */
export interface StorageLike {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
  readonly length: number
}

export interface ApplicationsEnvelopeV1 {
  schemaVersion: 1
  applications: JobApplication[]
}

/**
 * Feature detection by **write probe**, because the obvious check is wrong.
 *
 * MDN documents browsers that disable Web Storage while leaving `window.localStorage`
 * present, and private modes that hand back an object with a zero quota — so
 * `typeof localStorage !== 'undefined'` reports "available" for both, and the app would
 * then tell the user their work was saved when every write was silently discarded.
 *
 * The `QuotaExceededError` + non-empty branch is the distinction the same docs force:
 * a store that already holds data and refuses one more write is FULL, not OFF. Those two
 * states need different sentences in the UI, and only one of them is recoverable by deleting rows.
 */
export function isStorageAvailable(storage: StorageLike): boolean {
  try {
    storage.setItem(AVAILABILITY_PROBE_KEY, 'probe')
    storage.removeItem(AVAILABILITY_PROBE_KEY)
    return true
  } catch (error) {
    const isQuota = error instanceof DOMException && error.name === 'QuotaExceededError'
    return isQuota && storage.length > 0
  }
}

/**
 * A stored payload that cannot be understood. Carries the raw text so the caller can
 * preserve it instead of overwriting it.
 */
class CorruptData extends Error {
  constructor(readonly raw: string) {
    super('stored applications payload is not readable')
  }
}

/**
 * A payload written by a newer build. Deliberately its own failure, not a variety of
 * corruption: quarantining and reseeding a valid newer file is how a downgrade silently
 * deletes records the user still has. Read the version, refuse, and change nothing.
 */
class UnsupportedVersion extends Error {
  constructor(readonly found: number) {
    super(
      'stored envelope schemaVersion ' + found + ' is newer than this build\'s ' + CURRENT_SCHEMA_VERSION,
    )
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isStatus(value: unknown): value is ApplicationStatus {
  return typeof value === 'string' && (APPLICATION_STATUSES as readonly string[]).includes(value)
}

/**
 * Rebuilds one record field by field.
 *
 * This is not ceremony: the payload on disk crosses the same trust boundary as the form,
 * and `{ ...stored } as JobApplication` would hand whatever it contains straight to the UI —
 * including keys no type describes. Rebuilding drops the unknown ones and refuses a record
 * whose required fields are missing or wrongly typed, rather than rendering `undefined`.
 */
function decodeRecord(value: unknown, raw: string): JobApplication {
  if (!isPlainObject(value)) {
    throw new CorruptData(raw)
  }

  const { id, companyName, jobTitle, location, status, appliedAt, notes, createdAt } = value

  if (
    typeof id !== 'string' ||
    typeof companyName !== 'string' ||
    typeof jobTitle !== 'string' ||
    typeof location !== 'string' ||
    typeof createdAt !== 'string' ||
    !isStatus(status)
  ) {
    throw new CorruptData(raw)
  }

  const record: JobApplication = { id, companyName, jobTitle, location, status, createdAt }

  if (typeof appliedAt === 'string') {
    record.appliedAt = appliedAt
  }

  if (typeof notes === 'string') {
    record.notes = notes
  }

  return record
}

function decodeEnvelope(raw: string): JobApplication[] {
  let parsed: unknown

  try {
    parsed = JSON.parse(raw)
  } catch {
    throw new CorruptData(raw)
  }

  if (!isPlainObject(parsed) || typeof parsed.schemaVersion !== 'number') {
    throw new CorruptData(raw)
  }

  if (parsed.schemaVersion > CURRENT_SCHEMA_VERSION) {
    throw new UnsupportedVersion(parsed.schemaVersion)
  }

  if (!Array.isArray(parsed.applications)) {
    throw new CorruptData(raw)
  }

  return parsed.applications.map((entry) => decodeRecord(entry, raw))
}

/**
 * Copy the unreadable value somewhere retrievable, then clear the real key so the next read
 * reseeds. Returns null when even the copy failed — reported, not swallowed, because "we
 * could not save your data" and "we could not even keep the broken bytes" are different
 * sentences for the user.
 */
function quarantine(storage: StorageLike, raw: string): string | null {
  const key = `${CORRUPT_KEY_PREFIX}${new Date().toISOString()}`

  try {
    storage.setItem(key, raw)
    storage.removeItem(APPLICATIONS_STORAGE_KEY)
    return key
  } catch {
    return null
  }
}

function readRecords(storage: StorageLike): JobApplication[] {
  const raw = storage.getItem(APPLICATIONS_STORAGE_KEY)

  if (raw === null) {
    // Copies, not the module array: a caller that sorts or splices the result must not be
    // able to rewrite the shipped seed for everyone downstream.
    return seedApplications.map((record) => ({ ...record }))
  }

  return decodeEnvelope(raw)
}

function writeRecords(storage: StorageLike, applications: JobApplication[]): void {
  const envelope: ApplicationsEnvelopeV1 = { schemaVersion: 1, applications }
  storage.setItem(APPLICATIONS_STORAGE_KEY, JSON.stringify(envelope))
}

/**
 * Turns whatever a storage call threw into a value the UI can switch on.
 *
 * The seam's contract is that it returns Results and never throws: an escaping
 * DOMException shows up as an unhandled rejection and a blank page, which is a worse
 * outcome for the user than the fault itself.
 */
function toRepositoryError(storage: StorageLike, error: unknown): RepositoryError {
  if (error instanceof UnsupportedVersion) {
    return { code: 'unsupported-version', found: error.found }
  }

  if (error instanceof CorruptData) {
    return { code: 'corrupt-data', quarantinedAs: quarantine(storage, error.raw) }
  }

  if (error instanceof NotFound) {
    return { code: 'not-found', id: error.id }
  }

  if (error instanceof DOMException) {
    if (error.name === 'QuotaExceededError') {
      return { code: 'quota-exceeded' }
    }
    if (error.name === 'SecurityError') {
      return { code: 'unavailable' }
    }
  }

  return { code: 'storage-error', detail: error instanceof Error ? error.name : 'unknown' }
}

/** Marker used inside attempt() so the not-found case shares the Result contract with faults. */
class NotFound extends Error {
  constructor(readonly id: string) {
    super(`no application with id ${id}`)
  }
}

function notFound(id: string): NotFound {
  return new NotFound(id)
}

function attempt<T>(storage: StorageLike, run: () => T): Result<T, RepositoryError> {
  try {
    return ok(run())
  } catch (error) {
    return err(toRepositoryError(storage, error))
  }
}

/** The only envelope version this build understands. Bumping it requires a migration in §4.2. */
export const CURRENT_SCHEMA_VERSION = 1

const CORRUPT_KEY_PREFIX = `${APPLICATIONS_STORAGE_KEY}:corrupt-`

function defaultStorage(): StorageLike {
  return window.localStorage
}

/**
 * The only module in the app allowed to touch window.localStorage.
 *
 * Every method of the ApplicationRepository contract is implemented. What is still
 * deliberately absent: quota, corrupt-payload and unknown-version handling. Those are the
 * Red tests of BEHAVIOR-009..011; writing them now would be untested code.
 */
/**
 * Cross-tab signal for this medium (M2b.2).
 *
 * A `StorageEvent` fires only in the *other* tabs, never in the writer, so this is the entire
 * mechanism — and `newValue` is deliberately ignored: the subscriber re-reads through the
 * repository, which is what keeps corrupt payloads quarantined and newer schemas refused.
 *
 * Lives here rather than in the provider so the key name and `window` stay inside `src/data/`
 * (invariant 16). The `subscribe` contract is medium-agnostic; this is the localStorage answer.
 */
function subscribeToStorage(onExternalChange: () => void): () => void {
  const onStorage = (event: StorageEvent) => {
    // A null key is `storage.clear()`: the browser says something changed and refuses to say what.
    // Treating that as "the store is now empty" would render a confident lie — the rows may have
    // been rewritten by the same tick. Re-reading is both safer and the same code path as any
    // other change (spec §4, BEHAVIOR-024).
    if (event.key !== null && event.key !== APPLICATIONS_STORAGE_KEY) {
      return
    }

    onExternalChange()
  }

  window.addEventListener('storage', onStorage)

  return () => window.removeEventListener('storage', onStorage)
}

export function createLocalStorageRepository(
  options: { storage?: StorageLike } = {},
): ApplicationRepository {
  const storage = options.storage ?? defaultStorage()
  const unavailable: Result<never, RepositoryError> = err({ code: 'unavailable' })

  // Refusing outright is the point: a repository that quietly accepted writes into
  // memory while reporting success would produce exactly the lie the probe exists to avoid.
  // The app's composition root is expected to select createInMemoryRepository() instead and
  // show the notice; this is the backstop for anyone who wires it up wrong.
  if (!isStorageAvailable(storage)) {
    return {
      subscribe: () => () => {},
      async list() {
        return unavailable
      },
      async get() {
        return unavailable
      },
      async create() {
        return unavailable
      },
      async update() {
        return unavailable
      },
      async remove() {
        return unavailable
      },
    }
  }

  return {
    subscribe: subscribeToStorage,

    async list() {
      return attempt(storage, () => readRecords(storage))
    },

    async get(id: string) {
      return attempt(storage, () => {
        const found = readRecords(storage).find((record) => record.id === id)
        if (!found) {
          throw notFound(id)
        }
        return found
      })
    },

    async create(input: ApplicationInput) {
      return attempt(storage, () => {
        const record: JobApplication = {
          ...input,
          id: crypto.randomUUID(),
          createdAt: new Date().toISOString(),
        }

        // Read and write inside one attempt(): if the write is refused, nothing has been
        // returned as saved and the stored envelope is untouched.
        writeRecords(storage, [...readRecords(storage), record])

        return record
      })
    },

    async update(id: string, patch: ApplicationPatch) {
      return attempt(storage, () => {
        const records = readRecords(storage)
        const index = records.findIndex((record) => record.id === id)

        if (index === -1) {
          throw notFound(id)
        }

        const existing = records[index]
        // id and createdAt are re-pinned after the spread so a caller cannot relocate a
        // record or reset its age by passing them in a patch.
        const updated: JobApplication = {
          ...existing,
          ...patch,
          id: existing.id,
          createdAt: existing.createdAt,
        }

        writeRecords(storage, [...records.slice(0, index), updated, ...records.slice(index + 1)])

        return updated
      })
    },

    async remove(id: string) {
      return attempt(storage, () => {
        const records = readRecords(storage)

        if (!records.some((record) => record.id === id)) {
          throw notFound(id)
        }

        writeRecords(storage, records.filter((record) => record.id !== id))

        return { id }
      })
    },
  }
}

/**
 * Same contract, no durable store — the fallback for a device where storage is off, and the
 * test double for anything that must not care where records came from.
 */
export function createInMemoryRepository(initial: readonly JobApplication[] = []): ApplicationRepository {
  let records: JobApplication[] = initial.map((record) => ({ ...record }))

  return {
    async list() {
      return ok(records.map((record) => ({ ...record })))
    },

    async get(id: string) {
      const found = records.find((record) => record.id === id)
      return found ? ok({ ...found }) : err({ code: 'not-found', id })
    },

    async create(input: ApplicationInput) {
      const record: JobApplication = {
        ...input,
        id: crypto.randomUUID(),
        createdAt: new Date().toISOString(),
      }
      records = [...records, record]

      return ok({ ...record })
    },

    async update(id: string, patch: ApplicationPatch) {
      const existing = records.find((record) => record.id === id)

      if (!existing) {
        return err({ code: 'not-found', id })
      }

      const updated: JobApplication = { ...existing, ...patch, id, createdAt: existing.createdAt }
      records = records.map((record) => (record.id === id ? updated : record))

      return ok({ ...updated })
    },

    async remove(id: string) {
      if (!records.some((record) => record.id === id)) {
        return err({ code: 'not-found', id })
      }
      records = records.filter((record) => record.id !== id)

      return ok({ id })
    },

    // A store that dies with the tab has no other tab to hear about, so the honest implementation
    // is a no-op that still returns a working unsubscribe.
    subscribe: () => () => {},
  }
}
