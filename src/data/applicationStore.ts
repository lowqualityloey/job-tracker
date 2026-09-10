import type { ApplicationRepository } from '../domain/applicationRepository'
import { createInMemoryRepository, createLocalStorageRepository, isStorageAvailable } from './localStorageApplicationRepository'
import { seedApplications } from './seedApplications'

export interface ApplicationStore {
  repository: ApplicationRepository
  /** False on a device that blocks Web Storage, so the UI can say so instead of lying. */
  storageAvailable: boolean
}

/**
 * The composition root for data. Lives in `src/data/` on purpose: this is the only file
 * besides the adapter itself that may name `window.localStorage`, which keeps the
 * "pages never touch storage" invariant enforceable by a grep.
 *
 * Selecting the implementation here rather than inside the repository means exactly one
 * place decides availability, and the notice and the fallback can never disagree.
 */
export function createApplicationStore(): ApplicationStore {
  if (!isStorageAvailable(window.localStorage)) {
    return { repository: createInMemoryRepository(seedApplications), storageAvailable: false }
  }

  return { repository: createLocalStorageRepository({ storage: window.localStorage }), storageAvailable: true }
}
