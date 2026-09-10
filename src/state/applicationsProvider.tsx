import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import type { ApplicationInput, ApplicationPatch, JobApplication } from '../types/application'
import type { ApplicationRepository, RepositoryError, Result } from '../domain/applicationRepository'

interface ApplicationsApi {
  status: 'loading' | 'ready' | 'error'
  error: RepositoryError | null
  applications: JobApplication[]
  storageAvailable: boolean
  getApplication(id: string): JobApplication | undefined
  createApplication(input: ApplicationInput): Promise<Result<JobApplication>>
  updateApplication(id: string, patch: ApplicationPatch): Promise<Result<JobApplication>>
  deleteApplication(id: string): Promise<Result<{ id: string }>>
}

const ApplicationsContext = createContext<ApplicationsApi | null>(null)

interface ProviderProps {
  repository: ApplicationRepository
  storageAvailable?: boolean
  children: ReactNode
}

/**
 * Owns the single in-memory snapshot of applications for this tab.
 *
 * Reads are served from state so rendering never awaits; writes go to the repository first
 * and only then replace the snapshot, so the UI cannot show a record the store refused.
 * The repository is a prop, not an import: that is what lets a test drive the real
 * in-memory implementation, and what lets M3 swap in an HTTP one without touching a page.
 *
 * It is also where cross-tab reconciliation lands (M2b.2). A `StorageEvent` fires **only in the
 * other tabs**, never in the one that wrote, so this subscription is the whole mechanism — and it
 * re-reads instead of trusting the event payload, because the payload is raw bytes that have not
 * been through envelope validation, the field-by-field rebuild, quarantine, or the version gate.
 */
export function ApplicationsProvider({ repository, storageAvailable = true, children }: ProviderProps) {
  const [status, setStatus] = useState<ApplicationsApi['status']>('loading')
  const [applications, setApplications] = useState<JobApplication[]>([])
  const [error, setError] = useState<RepositoryError | null>(null)

  /**
   * One place turns a `list()` result into state, so the first read of the tab and every read that
   * follows an external change cannot drift apart.
   */
  const applyListResult = useCallback((result: Result<JobApplication[]>) => {
    if (result.ok) {
      setApplications(result.value)
      setError(null)
      setStatus('ready')
      return
    }

    // A failed re-read flips `status`, and every page branches on `status` before it touches the
    // array, so the rows already in state are never offered as if they were still trustworthy.
    setError(result.error)
    setStatus('error')
  }, [])

  useEffect(() => {
    let active = true

    const refresh = async () => {
      const result = await repository.list()

      if (active) {
        applyListResult(result)
      }
    }

    void refresh()

    // The provider asks *whether* its data changed, never *how*. `repository.subscribe` is the
    // seam: the localStorage adapter answers with a key-filtered StorageEvent, an in-memory store
    // has nothing to report, and M3's HTTP client can answer with polling or a pushed event
    // without a line of this file changing. Unsubscribing here is spec B-1's mitigation.
    const unsubscribe = repository.subscribe(() => {
      void refresh()
    })

    return () => {
      active = false
      unsubscribe()
    }
  }, [repository, applyListResult])

  const createApplication = useCallback(
    async (input: ApplicationInput) => {
      const result = await repository.create(input)

      if (result.ok) {
        setApplications((current) => [...current, result.value])
      }

      return result
    },
    [repository],
  )

  const updateApplication = useCallback(
    async (id: string, patch: ApplicationPatch) => {
      const result = await repository.update(id, patch)

      if (result.ok) {
        setApplications((current) => current.map((record) => (record.id === id ? result.value : record)))
      }

      return result
    },
    [repository],
  )

  // The snapshot only drops a record once the store has agreed to the removal.
  const deleteApplication = useCallback(
    async (id: string) => {
      const result = await repository.remove(id)

      if (result.ok) {
        setApplications((current) => current.filter((record) => record.id !== id))
      }

      return result
    },
    [repository],
  )

  const api = useMemo<ApplicationsApi>(
    () => ({
      status,
      error,
      applications,
      storageAvailable,
      getApplication: (id: string) => applications.find((record) => record.id === id),
      createApplication,
      updateApplication,
      deleteApplication,
    }),
    [status, error, applications, storageAvailable, createApplication, updateApplication, deleteApplication],
  )

  return <ApplicationsContext.Provider value={api}>{children}</ApplicationsContext.Provider>
}

export function useApplications(): ApplicationsApi {
  const context = useContext(ApplicationsContext)

  if (context === null) {
    throw new Error('useApplications must be used inside <ApplicationsProvider>')
  }

  return context
}
