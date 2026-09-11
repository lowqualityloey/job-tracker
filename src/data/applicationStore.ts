import type { ApplicationRepository } from '../domain/applicationRepository'
import { createHttpApplicationRepository } from './httpApplicationRepository'
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
/**
 * The API base URL, read at call time rather than captured at module load.
 *
 * Module-load capture is the tempting form and it makes the value untestable without re-importing the module: a
 * test cannot set `VITE_API_BASE_URL` after the first import, so the selection branch either goes untested or gets
 * tested by a mock of this very file, which proves nothing. The value is `string | undefined` by declaration
 * (`src/vite-env.d.ts`), not by a cast here, which is why eslint's no-unnecessary-type-assertion is right to reject
 * one: the type is already the narrowest true statement about the input.
 */
function configuredApiBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL
  return typeof configured === 'string' ? configured.trim() : ''
}

export function createApplicationStore(): ApplicationStore {
  const baseUrl = configuredApiBaseUrl()
  if (baseUrl !== '') {
    // `storageAvailable: true` is not an oversight. The flag exists so the UI can say "your device blocks Web
    // Storage, so nothing will persist" — and with the API adapter, nothing *is* persisted locally, because nothing
    // needs to be. Reporting it as unavailable there would show a warning about a limitation the app is not using.
    return { repository: createHttpApplicationRepository(baseUrl), storageAvailable: true }
  }

  if (!isStorageAvailable(window.localStorage)) {
    return { repository: createInMemoryRepository(seedApplications), storageAvailable: false }
  }

  return { repository: createLocalStorageRepository({ storage: window.localStorage }), storageAvailable: true }
}
