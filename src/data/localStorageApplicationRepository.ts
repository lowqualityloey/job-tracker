import type { ApplicationInput, ApplicationPatch, JobApplication } from '../types/application'
import { err, ok } from '../domain/applicationRepository'
import type { ApplicationRepository } from '../domain/applicationRepository'
import { seedApplications } from './seedApplications'

export const APPLICATIONS_STORAGE_KEY = 'job-tracker:applications'

export interface ApplicationsEnvelopeV1 {
  schemaVersion: 1
  applications: JobApplication[]
}

function readStored(): JobApplication[] {
  const raw = localStorage.getItem(APPLICATIONS_STORAGE_KEY)
  if (raw === null) {
    return seedApplications
  }
  const parsed = JSON.parse(raw) as ApplicationsEnvelopeV1
  return parsed.applications
}

function writeStored(applications: JobApplication[]): void {
  const envelope: ApplicationsEnvelopeV1 = { schemaVersion: 1, applications }
  localStorage.setItem(APPLICATIONS_STORAGE_KEY, JSON.stringify(envelope))
}

/**
 * The only module in the app allowed to touch window.localStorage.
 *
 * Currently implements create() alone. list/get/update/remove throw rather than returning
 * something plausible, because their Red tests have not run yet — an unimplemented stub that
 * silently returns [] would let a later Green commit look like it passed on first try.
 */
export function createLocalStorageRepository(): ApplicationRepository {
  return {
    async create(input: ApplicationInput) {
      const record: JobApplication = {
        ...input,
        id: crypto.randomUUID(),
        createdAt: new Date().toISOString(),
      }

      writeStored([...readStored(), record])

      return ok(record)
    },

    async list() {
      return ok(readStored())
    },

    async get(id: string) {
      const found = readStored().find((record) => record.id === id)

      return found ? ok(found) : err({ code: 'not-found', id })
    },

    async update(id: string, patch: ApplicationPatch) {
      const records = readStored()
      const index = records.findIndex((record) => record.id === id)

      if (index === -1) {
        return err({ code: 'not-found', id })
      }

      const existing = records[index]
      // id and createdAt are re-pinned after the spread so a caller cannot relocate a record
      // or reset its age by passing them in a patch.
      const updated: JobApplication = { ...existing, ...patch, id: existing.id, createdAt: existing.createdAt }
      const next = [...records.slice(0, index), updated, ...records.slice(index + 1)]

      writeStored(next)

      return ok(updated)
    },

    async remove(id: string) {
      const records = readStored()

      if (!records.some((record) => record.id === id)) {
        return err({ code: 'not-found', id })
      }

      writeStored(records.filter((record) => record.id !== id))

      return ok({ id })
    },
  }
}
