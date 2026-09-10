import type { ApplicationInput, ApplicationPatch, JobApplication } from '../types/application'
import { err, ok } from '../domain/applicationRepository'
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

function readRecords(storage: StorageLike): JobApplication[] {
  const raw = storage.getItem(APPLICATIONS_STORAGE_KEY)

  if (raw === null) {
    // Copies, not the module array: a caller that sorts or splices the result must not be
    // able to rewrite the shipped seed for everyone downstream.
    return seedApplications.map((record) => ({ ...record }))
  }

  const parsed = JSON.parse(raw) as ApplicationsEnvelopeV1
  return parsed.applications
}

function writeRecords(storage: StorageLike, applications: JobApplication[]): void {
  const envelope: ApplicationsEnvelopeV1 = { schemaVersion: 1, applications }
  storage.setItem(APPLICATIONS_STORAGE_KEY, JSON.stringify(envelope))
}

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
    async list() {
      return ok(readRecords(storage))
    },

    async get(id: string) {
      const found = readRecords(storage).find((record) => record.id === id)
      return found ? ok(found) : err({ code: 'not-found', id })
    },

    async create(input: ApplicationInput) {
      const record: JobApplication = {
        ...input,
        id: crypto.randomUUID(),
        createdAt: new Date().toISOString(),
      }

      writeRecords(storage, [...readRecords(storage), record])

      return ok(record)
    },

    async update(id: string, patch: ApplicationPatch) {
      const records = readRecords(storage)
      const index = records.findIndex((record) => record.id === id)

      if (index === -1) {
        return err({ code: 'not-found', id })
      }

      const existing = records[index]
      // id and createdAt are re-pinned after the spread so a caller cannot relocate a record
      // or reset its age by passing them in a patch.
      const updated: JobApplication = { ...existing, ...patch, id: existing.id, createdAt: existing.createdAt }

      writeRecords(storage, [...records.slice(0, index), updated, ...records.slice(index + 1)])

      return ok(updated)
    },

    async remove(id: string) {
      const records = readRecords(storage)

      if (!records.some((record) => record.id === id)) {
        return err({ code: 'not-found', id })
      }

      writeRecords(storage, records.filter((record) => record.id !== id))

      return ok({ id })
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
  }
}
