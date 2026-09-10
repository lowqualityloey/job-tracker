import type { ApplicationInput, JobApplication } from '../types/application'
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

    async update() {
      throw new Error('not implemented: BEHAVIOR-m2-persistence-seam-006')
    },

    async remove() {
      throw new Error('not implemented: BEHAVIOR-m2-persistence-seam-007')
    },
  }
}
