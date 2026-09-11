import type {
  ApplicationInput,
  ApplicationPatch,
  JobApplication,
} from '../types/application'
import { err, ok, type ApplicationRepository, type RepositoryError, type Result } from '../domain/applicationRepository'

/**
 * The HTTP adapter — the reason M3 exists.
 *
 * Two constraints shape almost every method here, and neither is visible from the interface:
 *
 *  1. **`revision` must not enter `JobApplication`.** Invariant I-6 keeps that field unknown to every type above
 *     `src/data/`, so the adapter stores it in a side table keyed by id and re-reads it when a write needs an
 *     `If-Match`. The alternative — an optional 9th field on the domain record — would make the UI's data model
 *     carry a concurrency token it must never touch, and "never touch" is not a rule a type can enforce once the
 *     field exists.
 *  2. **`update(id, patch)` is partial; `PUT` is a full replacement.** So an update is a read-modify-write: fetch
 *     the current row, merge the patch over it, send the whole thing with the revision just read. That is also what
 *     makes a stale write *impossible to hide* — the revision used is the one the server just handed back, so a
 *     conflict means someone wrote between those two requests, which is the truth rather than a client-side guess.
 *
 * Error mapping is deliberately thin: every non-OK response currently becomes `storage-error`. That is safe (the
 * provider refuses further writes on any error, so nothing is silently lost) and it is not yet specific. BEHAVIOR
 * `-037` drives §4.3's total table out of failing tests, and `-038` the transport case. Writing the table here would
 * be code with no test behind it — the exact shape this project's own rules refuse.
 */

/** What the API sends: the domain record plus the concurrency token I-6 keeps out of `src/domain`. */
type WireApplication = JobApplication & { revision: number }

function isWireApplication(candidate: unknown): candidate is WireApplication {
  if (typeof candidate !== 'object' || candidate === null) {
    return false
  }

  const record = candidate as Record<string, unknown>
  return (
    typeof record.id === 'string' &&
    typeof record.companyName === 'string' &&
    typeof record.jobTitle === 'string' &&
    typeof record.status === 'string' &&
    typeof record.createdAt === 'string' &&
    typeof record.revision === 'number'
  )
}

/** The domain projection: every wire field except `revision`, which stays in this file. */
function toDomain(wire: WireApplication): JobApplication {
  const { revision: _revision, ...domain } = wire
  return domain
}

export function createHttpApplicationRepository(baseUrl: string): ApplicationRepository {
  const root = baseUrl.replace(/\/+$/, '')
  const revisions = new Map<string, number>()

  async function request(path: string, init?: RequestInit): Promise<Response> {
    // No try/catch here on purpose: a `fetch` rejection (server down, DNS failure, CORS refusal) must reach the
    // caller as a distinct event from a response with a bad status, because §4.3 maps them to different codes.
    // `-038` owns that distinction; this function just refuses to flatten it early.
    return fetch(`${root}${path}`, {
      ...init,
      headers: { accept: 'application/json', ...(init?.headers ?? {}) },
    })
  }

  function remember(wire: WireApplication): JobApplication {
    revisions.set(wire.id, wire.revision)
    return toDomain(wire)
  }

  function storageError(detail: string): Result<never, RepositoryError> {
    return err({ code: 'storage-error', detail })
  }

  async function readOne(path: string): Promise<Result<JobApplication>> {
    let response: Response
    try {
      response = await request(path)
    } catch (cause) {
      return storageError(String(cause))
    }

    if (!response.ok) {
      return storageError(`GET ${path} answered ${response.status}`)
    }

    return parseRecord(response)
  }

  async function parseRecord(response: Response): Promise<Result<JobApplication>> {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      // A 200 whose body is not JSON is a corrupt response, not a transport failure: the server said "here it is"
      // and handed over something unreadable. §4.3's `corrupt-data` row, and the reason the parse is checked rather
      // than cast.
      return err({ code: 'corrupt-data', quarantinedAs: null })
    }

    if (!isWireApplication(body)) {
      return err({ code: 'corrupt-data', quarantinedAs: null })
    }

    return ok(remember(body))
  }

  return {
    subscribe() {
      // A real no-op, not a placeholder pretending to work: BEHAVIOR `-040` drives the EventSource implementation,
      // including the "at most one re-list per `open`" bound the grill added as F-8. Until then the app behaves as
      // it did in M2 — cross-tab changes appear on the next explicit read — and a silent `return` states that.
      return () => undefined
    },

    async list() {
      let response: Response
      try {
        response = await request('/api/applications')
      } catch (cause) {
        return storageError(String(cause))
      }

      if (!response.ok) {
        return storageError(`GET /api/applications answered ${response.status}`)
      }

      let body: unknown
      try {
        body = await response.json()
      } catch {
        return err({ code: 'corrupt-data', quarantinedAs: null })
      }

      if (!Array.isArray(body) || !body.every(isWireApplication)) {
        // One bad record fails the whole read rather than being filtered out. A list that quietly loses rows is how
        // a user deletes a record they never asked to delete, by which point they will not trust the list at all.
        return err({ code: 'corrupt-data', quarantinedAs: null })
      }

      // No sort here: §4.3 says "unordered is fine; client sorts", and the client's sort is M2's tested behaviour.
      return ok(body.map(remember))
    },

    async get(id) {
      return readOne(`/api/applications/${encodeURIComponent(id)}`)
    },

    async create(input) {
      // The id is minted here, not read back from the server (DECISION-m3-backend-api-007): a client-generated uuid
      // is what makes a retried POST detectable as the same record instead of a second one.
      const id = crypto.randomUUID()
      let response: Response
      try {
        response = await request('/api/applications', {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ id, ...input }),
        })
      } catch (cause) {
        return storageError(String(cause))
      }

      if (!response.ok) {
        return storageError(`POST /api/applications answered ${response.status}`)
      }

      return parseRecord(response)
    },

    async update(id, patch: ApplicationPatch) {
      const current = await readOne(`/api/applications/${encodeURIComponent(id)}`)
      if (!current.ok) {
        return current
      }

      const replacement: ApplicationInput = {
        companyName: patch.companyName ?? current.value.companyName,
        jobTitle: patch.jobTitle ?? current.value.jobTitle,
        location: patch.location ?? current.value.location,
        status: patch.status ?? current.value.status,
        appliedAt: patch.appliedAt === undefined ? current.value.appliedAt : patch.appliedAt,
        notes: patch.notes === undefined ? current.value.notes : patch.notes,
      }

      // `=== undefined` rather than `??` for appliedAt/notes: both are optional fields whose *absence from the patch*
      // means "leave it" and whose explicit empty value means "clear it". `??` cannot tell those apart, so a form
      // that clears a date would silently keep the old one.
      const revision = revisions.get(id)
      let response: Response
      try {
        response = await request(`/api/applications/${encodeURIComponent(id)}`, {
          method: 'PUT',
          headers: {
            'content-type': 'application/json',
            ...(revision === undefined ? {} : { 'if-match': `"${revision}"` }),
          },
          body: JSON.stringify({ id, ...replacement }),
        })
      } catch (cause) {
        return storageError(String(cause))
      }

      if (!response.ok) {
        return storageError(`PUT /api/applications/${id} answered ${response.status}`)
      }

      return parseRecord(response)
    },

    async remove(id) {
      const revision = revisions.get(id)
      let response: Response
      try {
        response = await request(`/api/applications/${encodeURIComponent(id)}`, {
          method: 'DELETE',
          headers: revision === undefined ? {} : { 'if-match': `"${revision}"` },
        })
      } catch (cause) {
        return storageError(String(cause))
      }

      if (!response.ok) {
        return storageError(`DELETE /api/applications/${id} answered ${response.status}`)
      }

      revisions.delete(id)
      return ok({ id })
    },
  }
}
