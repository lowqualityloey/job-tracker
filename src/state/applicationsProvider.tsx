import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
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
   * DECISION-m4-auth-005, client half: a 401 from ANY seam -- first read, re-read, or a write this tab just
   * attempted -- drops the snapshot along with the session it belonged to. Not a cache to keep: rows loaded under
   * someone's credentials, still on screen after their session ended, are a leak dressed up as politeness. Only the
   * session state goes; the in-progress form draft deliberately does not, which is what "draft-free" means in the
   * decision's own wording.
   */
  const sessionEnded = useCallback((error: RepositoryError): boolean => {
    if (error.code !== 'unauthorized') {
      return false
    }

    // Order matters only for the reader: close the channel first, because everything below this line is a re-render, and a
    // re-render that happens while the stream is still open can queue another probe from the browser's next retry tick.
    unsubscribeRef.current?.()
    unsubscribeRef.current = null

    setApplications([])
    setError(error)
    setStatus('error')
    return true
  }, [])

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
    if (!sessionEnded(result.error)) {
      setError(result.error)
      setStatus('error')
    }
  }, [sessionEnded])

  // A ref, not the effect's closure flag, because `reload` is now called from three places: the
  // first read, the external-change subscription, and a write the store refused. Each of them
  // resolves after an await, and every one of them has to be able to discover that this tab's
  // tree is gone. The effect body sets it back to true so React 18's double-invoke stays correct.
  const mounted = useRef(true)

  // BEHAVIOR-062: the stream's teardown handle, kept where a *verdict* can reach it. The provider used to close the stream
  // only on unmount (spec B-1); now a session known dead closes it too, because an EventSource the browser keeps retrying
  // against a 401 is the loop this row exists to end -- and after a redirect to /login nothing unmounts, since the provider
  // sits above the router on purpose.
  const unsubscribeRef = useRef<(() => void) | null>(null)

  const reload = useCallback(async () => {
    const result = await repository.list()

    if (mounted.current) {
      applyListResult(result)
    }
  }, [repository, applyListResult])

  /**
   * BEHAVIOR-062: ask the server, once, whether this session is still alive — and act on exactly one answer.
   *
   * A probe reuses `list()` rather than minting a session endpoint: it is the same authenticated read the app already makes,
   * its 401 already maps to `unauthorized` through `-060`'s table, and `GET /api/auth/session` (spec §4.3) still has no
   * ladder row. Deliberately NOT routed through `applyListResult`: a probe whose failure means "the server is busy" must not
   * blank a board that is showing readable rows. Only `unauthorized` is a verdict; every other outcome is "the outage was the
   * network, carry on".
   */
  const probeSession = useCallback(async () => {
    const result = await repository.list()

    if (mounted.current && !result.ok && result.error.code === 'unauthorized') {
      sessionEnded(result.error)
    }
  }, [repository, sessionEnded])

  useEffect(() => {
    mounted.current = true

    void reload()

    // The provider asks *whether* its data changed, and since BEHAVIOR-062 the single bit of *how* that a 401 needs. `repository.subscribe` is the
    // seam: the localStorage adapter answers with a key-filtered StorageEvent, an in-memory store
    // has nothing to report, and M3's HTTP client can answer with polling or a pushed event
    // without a line of this file changing. Unsubscribing here is spec B-1's mitigation.
    const unsubscribe = repository.subscribe((reason) => {
      // The sentence below came from M3 and was true then; -062 spent one bit of it. The provider still asks *whether* its
      // data changed, and now distinguishes the one case where re-reading is the wrong question to ask.
      if (reason === 'error') {
        void probeSession()
        return
      }

      void reload()
    })
    unsubscribeRef.current = unsubscribe

    return () => {
      mounted.current = false
      unsubscribeRef.current = null
      unsubscribe()
    }
  }, [repository, reload, probeSession])

  // BEHAVIOR-026: a read that failed means this tab cannot say what the store holds, and writing
  // now would overwrite bytes it has not read. The refusal carries the *existing* error rather than
  // inventing an eighth code — the UI already has an honest sentence for it.
  //
  // `loading` is deliberately not refused: no page reaches a write control before `ready` (the form
  // shows "Loading…" and the details page gates on status), so refusing there would be a claim this
  // milestone never made and a message nothing can justify.
  const refusal = status === 'error' && error !== null ? err(error) : null

  const createApplication = useCallback(
    async (input: ApplicationInput) => {
      if (refusal) {
        return refusal
      }

      const result = await repository.create(input)
      // A write is usually how a tab learns the session ended: the read that filled this screen happened while it
      // was alive. Without this the 401 stops at the page's toast and the app keeps showing data whose session is
      // gone -- the half of the decision -060 could not act on, because it had no variant to act on.
      if (!result.ok) {
        sessionEnded(result.error)
      }


      if (result.ok) {
        setApplications((current) => [...current, result.value])
      }

      return result
    },
    [repository, refusal, sessionEnded],
  )

  const updateApplication = useCallback(
    async (id: string, patch: ApplicationPatch) => {
      if (refusal) {
        return refusal
      }

      const result = await repository.update(id, patch)
      // A write is usually how a tab learns the session ended: the read that filled this screen happened while it
      // was alive. Without this the 401 stops at the page's toast and the app keeps showing data whose session is
      // gone -- the half of the decision -060 could not act on, because it had no variant to act on.
      if (!result.ok) {
        sessionEnded(result.error)
      }


      if (result.ok) {
        setApplications((current) => current.map((record) => (record.id === id ? result.value : record)))
        return result
      }

      // `not-found` is not a mystery here: it means the row this tab was showing is gone. Waiting
      // for a StorageEvent that will never arrive (this tab is the one that spoke) would leave a
      // ghost the user can click into, so the refused write is itself the invalidation signal.
      if (result.error.code === 'not-found') {
        await reload()
      }

      return result
    },
    [repository, reload, refusal, sessionEnded],
  )

  const deleteApplication = useCallback(
    async (id: string) => {
      if (refusal) {
        return refusal
      }

      const result = await repository.remove(id)
      // A write is usually how a tab learns the session ended: the read that filled this screen happened while it
      // was alive. Without this the 401 stops at the page's toast and the app keeps showing data whose session is
      // gone -- the half of the decision -060 could not act on, because it had no variant to act on.
      if (!result.ok) {
        sessionEnded(result.error)
      }


      if (result.ok) {
        // The snapshot only drops a record once the store has agreed to the removal.
        setApplications((current) => current.filter((record) => record.id !== id))
        return result
      }

      if (result.error.code === 'not-found') {
        await reload()
      }

      return result
    },
    [repository, reload, refusal, sessionEnded],
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
