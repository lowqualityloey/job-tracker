import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import type { ApplicationInput, ApplicationPatch, JobApplication } from '../types/application'
import type { ApplicationRepository, RepositoryError, Result } from '../domain/applicationRepository'
import { err } from '../domain/applicationRepository'

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
 */
export function ApplicationsProvider({ repository, storageAvailable = true, children }: ProviderProps) {
  const [status, setStatus] = useState<ApplicationsApi['status']>('loading')
  const [applications, setApplications] = useState<JobApplication[]>([])
  const [error, setError] = useState<RepositoryError | null>(null)

  useEffect(() => {
    let active = true

    void repository.list().then((result) => {
      if (!active) {
        return
      }
      if (result.ok) {
        setApplications(result.value)
        setStatus('ready')
      } else {
        setError(result.error)
        setStatus('error')
      }
    })

    return () => {
      active = false
    }
  }, [repository])

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
      // (the snapshot only drops a record once the store has agreed to the removal)
      // let that Green commit look as though it had passed on its first attempt.
      // let a later Green commit appear to have passed on its first attempt.
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
