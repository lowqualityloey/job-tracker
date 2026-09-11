import type {
  ApplicationInput,
  ApplicationPatch,
  JobApplication,
} from '../types/application'
import {
  err,
  ok,
  type ApplicationRepository,
  type RepositoryError,
  type Result,
} from '../domain/applicationRepository'
// FieldError is imported from the module that declares it (`domain/validation`), not re-exported through the
// repository contract. TS2459 said so plainly: `applicationRepository` imports the type to build its union and
// never exports it, so reaching for it there was a guess about a module's surface rather than a reading of it.
import type { FieldError } from '../domain/validation'

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
 *  2. **`update(id, patch)` is partial; `PUT` is a full replacement.** So an update is a read-modify-write: fetch the
 *     current row, merge the patch over it, send the whole thing with the revision just read. That is also what makes
 *     a stale write impossible to hide — the revision used is the one the server handed back seconds earlier, so a
 *     conflict means someone wrote *between* those two requests, which is a true statement about the world rather
 *     than a client-side guess about it.
 *
 * Error mapping is §4.3's table and lives in `mapFailure`, in one place, because AC-8's claim is *totality*: a table
 * restated in six methods is six chances to forget a row.
 */

/** What the API sends: the domain record plus the concurrency token I-6 keeps out of `src/domain`. */
type WireApplication = JobApplication & { revision: number }

/** §4.3's `errors[].pointer` values are wire member names, and only these are legal to highlight in a form. */
const KNOWN_FIELDS = ['companyName', 'jobTitle', 'location', 'status', 'appliedAt', 'notes'] as const

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

interface ProblemEnvelope {
  code?: unknown
  errors?: unknown
}

async function readProblem(response: Response): Promise<ProblemEnvelope | null> {
  // Content-type before body: a gateway's HTML error page is not a problem document whatever it contains, and
  // parsing it to find out would turn "the proxy ate my request" into a corrupt-data report about the app's data.
  if (!response.headers.get('content-type')?.includes('application/problem+json')) {
    return null
  }

  try {
    return (await response.json()) as ProblemEnvelope
  } catch {
    return null
  }
}

/**
 * §4.3's HTTP ⇄ `RepositoryError` table — total, with no silent fallthrough.
 *
 * Order is the load-bearing part. Status is tested before the body because `502`/`504` arrive from an intermediary
 * whose body has nothing to do with the API's vocabulary. The `code` is read after, and *missing* codes join
 * unrecognised ones on the fail-closed branch: that is what turns "the server grew a new error" into a visible
 * client fault instead of a guess about what it meant.
 *
 * `id` is what the caller asked for, never what the body claims — `not-found` and `conflict` exist so the UI can act
 * on *that* record, and trusting a server-authored id would let a response name a different row than the request.
 * Null means the request carried no id to name (a list), in which case the code cannot be honoured and is corrupt.
 */
async function mapFailure(response: Response, id: string | null): Promise<RepositoryError> {
  if (response.status === 502 || response.status === 503 || response.status === 504) {
    return { code: 'unavailable' }
  }

  const problem = await readProblem(response)
  if (problem === null) {
    return { code: 'storage-error', detail: `HTTP ${response.status} with no readable problem document` }
  }

  switch (problem.code) {
    case 'validation': {
      const fieldErrors = toFieldErrors(problem.errors)
      return fieldErrors === null
        ? { code: 'storage-error', detail: 'validation named a field this client does not have' }
        : { code: 'validation', fieldErrors }
    }

    case 'not-found':
      return id === null ? { code: 'corrupt-data', quarantinedAs: null } : { code: 'not-found', id }

    case 'conflict':
      return id === null ? { code: 'corrupt-data', quarantinedAs: null } : { code: 'conflict', id }

    default:
      // Includes the absent-code case. ASP.NET's own `ProblemDetails` for a 415 (verified live in `-045`) carries no
      // `code` extension member, so "we do not recognise it" and "it never said" must land in the same place: both
      // mean the client is looking at a response its contract does not describe.
      return { code: 'corrupt-data', quarantinedAs: null }
  }
}

function toFieldErrors(errors: unknown): FieldError[] | null {
  if (!Array.isArray(errors)) {
    return null
  }

  const mapped: FieldError[] = []
  for (const entry of errors) {
    const { pointer, detail } = (entry ?? {}) as { pointer?: unknown; detail?: unknown }
    if (typeof pointer !== 'string' || typeof detail !== 'string') {
      return null
    }

    const field = pointer.startsWith('#/') ? pointer.slice(2) : ''
    if (!(KNOWN_FIELDS as readonly string[]).includes(field)) {
      // One unrecognised pointer condemns the whole response, per §4.3. The reason to condemn rather than skip: a
      // server naming a field this client has never heard of is speaking a schema it is not, so the remaining
      // pointers are not trustworthy either — highlighting three real fields while a fourth silently vanished would
      // look like a working form with a bug in it.
      return null
    }

    mapped.push({ field: field as FieldError['field'], message: detail })
  }

  return mapped
}

export function createHttpApplicationRepository(baseUrl: string): ApplicationRepository {
  const root = baseUrl.replace(/\/+$/, '')
  const revisions = new Map<string, number>()

  function transportError(cause: unknown): RepositoryError {
    // Transport, not response: `fetch` rejecting means no HTTP exchange happened at all — nothing listening, DNS
    // failure, CORS refusal. §4.3 maps that here rather than to `unavailable`, because "we could not learn anything"
    // is not "the server said it is busy". The spec's own behaviour ladder says `unavailable` for the same event;
    // §13 of the test plan records which sentence wins, and why the table with the rationale outranks the row
    // without one.
    return { code: 'storage-error', detail: String(cause) }
  }

  async function send(path: string, init: RequestInit, id: string | null): Promise<Result<Response>> {
    let response: Response
    try {
      response = await fetch(`${root}${path}`, {
        ...init,
        headers: { accept: 'application/json', ...init.headers },
      })
    } catch (cause) {
      return err(transportError(cause))
    }

    if (!response.ok) {
      return err(await mapFailure(response, id))
    }

    return ok(response)
  }

  function remember(wire: WireApplication): JobApplication {
    revisions.set(wire.id, wire.revision)
    return toDomain(wire)
  }

  function ifMatch(id: string): Record<string, string> {
    const revision = revisions.get(id)
    // Omitted rather than sent empty: §4.3 treats a *missing* If-Match on PUT as a conflict, and a header with no
    // value would be a third state the contract does not describe. `remove` may legitimately go without one.
    return revision === undefined ? {} : { 'if-match': `"${revision}"` }
  }

  async function one(response: Response): Promise<Result<JobApplication>> {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      // A 200 whose body is not JSON is a corrupt response, not a transport failure: the server said "here it is"
      // and handed over something unreadable. §4.3's corrupt-data row, and the reason the parse is checked rather
      // than cast.
      return err({ code: 'corrupt-data', quarantinedAs: null })
    }

    return isWireApplication(body) ? ok(remember(body)) : err({ code: 'corrupt-data', quarantinedAs: null })
  }

  async function readRecord(path: string, id: string | null): Promise<Result<JobApplication>> {
    const sent = await send(path, { method: 'GET' }, id)
    return sent.ok ? one(sent.value) : sent
  }

  return {
    subscribe() {
      // A real no-op, not a placeholder pretending to work: BEHAVIOR `-040` drives the EventSource implementation,
      // including the "at most one re-list per `open`" bound the grill added as F-8. Until then the app behaves as it
      // did in M2 — cross-tab changes appear on the next explicit read — and a silent `return` says so.
      return () => undefined
    },

    async list() {
      const sent = await send('/api/applications', { method: 'GET' }, null)
      if (!sent.ok) {
        return sent
      }

      let body: unknown
      try {
        body = await sent.value.json()
      } catch {
        return err({ code: 'corrupt-data', quarantinedAs: null })
      }

      if (!Array.isArray(body) || !body.every(isWireApplication)) {
        // One bad record fails the whole read rather than being filtered out. A list that quietly loses rows is how a
        // user ends up with a record they never asked to delete, by which point they stop trusting the list.
        return err({ code: 'corrupt-data', quarantinedAs: null })
      }

      // No sort here: §4.3 says "unordered is fine; client sorts", and the client's sort is M2's tested behaviour.
      return ok(body.map(remember))
    },

    async get(id) {
      return readRecord(`/api/applications/${encodeURIComponent(id)}`, id)
    },

    async create(input) {
      // The id is minted here, not read back from the server (DECISION-m3-backend-api-007): a client-generated uuid
      // is what makes a retried POST recognisable as the same record rather than a second copy of it.
      const id = crypto.randomUUID()
      const sent = await send('/api/applications', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ id, ...input }),
      }, id)

      return sent.ok ? one(sent.value) : sent
    },

    async update(id, patch: ApplicationPatch) {
      const current = await readRecord(`/api/applications/${encodeURIComponent(id)}`, id)
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

      // `=== undefined` rather than `??` for appliedAt/notes: absence from the patch means "leave it", an explicit
      // empty value means "clear it", and `??` cannot tell those apart — so a form that cleared a date would silently
      // keep the old one, and the user would be told they had not cleared it.
      const sent = await send(`/api/applications/${encodeURIComponent(id)}`, {
        method: 'PUT',
        headers: { 'content-type': 'application/json', ...ifMatch(id) },
        body: JSON.stringify({ id, ...replacement }),
      }, id)

      return sent.ok ? one(sent.value) : sent
    },

    async remove(id) {
      const sent = await send(`/api/applications/${encodeURIComponent(id)}`, {
        method: 'DELETE',
        headers: ifMatch(id),
      }, id)
      if (!sent.ok) {
        return sent
      }

      // Nothing to parse: §4.3 answers 204, and calling `.json()` on an empty body is how an adapter invents a
      // corrupt-data error for a request that succeeded.
      revisions.delete(id)
      return ok({ id })
    },
  }
}
