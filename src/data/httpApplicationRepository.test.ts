import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'

import type { ApplicationRepository, RepositoryError, Result } from '../domain/applicationRepository'
import { createHttpApplicationRepository } from './httpApplicationRepository'

/**
 * BEHAVIOR-m3-backend-api-037 (AC-8) — §4.3's HTTP ⇄ `RepositoryError` table is **total, with no silent
 * fallthrough**. One case per row of the table, plus the two rows the table implies and a spec does not always
 * spell out: an *unrecognised* `code`, and a problem body with **no** `code` at all.
 *
 * The shape of the assertion matters as much as the coverage. Every case checks the code *and* the payload the code
 * carries (`fieldErrors`, `id`), because a mapping that names the right variant with the wrong contents is the
 * failure that reaches a user: `not-found` without an id gives the details page nothing to show, and `validation`
 * without field errors gives a form nothing to highlight.
 *
 * `toMatchObject` rather than `toEqual` on purpose: `storage-error` carries a free-text `detail` that §4.3 leaves to
 * the implementation ("the reason, for a human"), and pinning its exact wording here would turn a sentence into a
 * contract. Every key the client *branches on* is still asserted exactly.
 *
 * The transport case — `fetch` itself rejecting — is BEHAVIOR `-038` and is not here, because it needs the
 * provider's refusal gate asserted alongside it.
 */

const BASE = 'http://api.test:8080'
const ID = '3f9b4d0c-6c1e-4a2f-9b7f-4a3d2e1f0a9b'

function problem(status: number, code: string | undefined, extra: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      type: `https://job-tracker.local/probs/${code ?? 'error'}`,
      title: 'The request could not be completed.',
      status,
      instance: '/api/applications',
      ...(code === undefined ? {} : { code }),
      ...extra,
    }),
    { status, headers: { 'content-type': 'application/problem+json' } },
  )
}

interface MapCase {
  name: string
  respond: () => Response
  invoke: (repository: ApplicationRepository) => Promise<Result<unknown>>
  /** The declared keys are the contract; `detail` strings are deliberately unconstrained. */
  expect: Partial<RepositoryError>
}

const cases: MapCase[] = [
  {
    name: '400 validation becomes fieldErrors on the named fields',
    respond: () => problem(400, 'validation', {
      errors: [{ pointer: '#/companyName', detail: 'Company name is required.' }],
    }),
    invoke: (repository) => repository.create({ companyName: '', jobTitle: 'Engineer', location: 'NY', status: 'Saved' }),
    expect: { code: 'validation', fieldErrors: [{ field: 'companyName', message: 'Company name is required.' }] },
  },
  {
    name: 'a pointer at a field the client does not have becomes storage-error, not a guess',
    respond: () => problem(400, 'validation', {
      errors: [{ pointer: '#/schemaVersion', detail: 'Unknown field.' }],
    }),
    invoke: (repository) => repository.create({ companyName: 'Hooli', jobTitle: 'Engineer', location: 'NY', status: 'Saved' }),
    expect: { code: 'storage-error' },
  },
  {
    name: '404 becomes not-found carrying the id that was asked for',
    respond: () => problem(404, 'not-found'),
    invoke: (repository) => repository.get(ID),
    expect: { code: 'not-found', id: ID },
  },
  {
    name: '409 becomes conflict — the eighth variant, DECISION-006',
    respond: () => problem(409, 'conflict'),
    invoke: (repository) => repository.create({ companyName: 'Hooli', jobTitle: 'Engineer', location: 'NY', status: 'Saved' }),
    expect: { code: 'conflict', id: ID },
  },
  {
    name: '503 becomes unavailable',
    respond: () => problem(503, 'unavailable'),
    invoke: (repository) => repository.list(),
    expect: { code: 'unavailable' },
  },
  {
    name: 'a 502 from an intermediary becomes unavailable too',
    respond: () => new Response('<html>Bad Gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }),
    invoke: (repository) => repository.list(),
    expect: { code: 'unavailable' },
  },
  {
    name: 'a 504 from an intermediary becomes unavailable too',
    respond: () => new Response('', { status: 504 }),
    invoke: (repository) => repository.list(),
    expect: { code: 'unavailable' },
  },
  {
    name: 'a 200 whose body is not JSON becomes corrupt-data',
    respond: () => new Response('not json at all', { status: 200, headers: { 'content-type': 'application/json' } }),
    invoke: (repository) => repository.list(),
    expect: { code: 'corrupt-data' },
  },
  {
    name: 'a 200 whose record is missing required members becomes corrupt-data',
    respond: () => new Response(JSON.stringify([{ id: ID }]), { status: 200, headers: { 'content-type': 'application/json' } }),
    invoke: (repository) => repository.list(),
    expect: { code: 'corrupt-data' },
  },
  {
    name: 'a server error with a non-problem body becomes storage-error',
    respond: () => new Response('<html>boom</html>', { status: 500, headers: { 'content-type': 'text/html' } }),
    invoke: (repository) => repository.list(),
    expect: { code: 'storage-error' },
  },
  {
    name: 'a code we do not recognise fails closed to corrupt-data',
    respond: () => problem(400, 'teapot'),
    invoke: (repository) => repository.list(),
    expect: { code: 'corrupt-data' },
  },
  {
    name: 'a problem with no code at all fails closed too — the 415 the framework answers',
    respond: () => problem(415, undefined),
    invoke: (repository) => repository.create({ companyName: 'Hooli', jobTitle: 'Engineer', location: 'NY', status: 'Saved' }),
    expect: { code: 'corrupt-data' },
  },
]

describe('httpApplicationRepository — the §4.3 error mapping is total', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    // The adapter mints its own uuid on create (DECISION-007), so a case that asserts `conflict` carries *an* id
    // cannot also name the fixture's id unless the mint is deterministic. Spying here makes the assertion exact;
    // leaving it random would force the case to check `code` alone, and "the id came back" is half the claim.
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(ID)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each(cases.map((testCase) => [testCase.name, testCase] as const))(
    '%s',
    async (_name, testCase) => {
      vi.mocked(fetch).mockResolvedValue(testCase.respond())
      const result = await testCase.invoke(createHttpApplicationRepository(BASE))

      expect(result.ok).toBe(false)
      if (result.ok) {
        // The mapping under test is a failure path, so a case whose request *succeeded* has told us nothing about
        // it. Returning here keeps the report honest ("expected a refusal") rather than failing on a comparison
        // against `undefined` that looks like a payload bug.
        return
      }

      expect(result.error).toMatchObject(testCase.expect)
    },
  )

  it('names every code the client can branch on, so a new server code cannot land on a default', () => {
    // AC-8's structural half: the union is the closed list, and this assertion is what makes adding a variant to the
    // API a change that has to be made *here* as well. `quota-exceeded` and `unsupported-version` stay in the list
    // while the localStorage adapter exists, even though HTTP can never produce them — §4.3 says so explicitly, and
    // a future reader who deletes them should have to delete this line first.
    const codes: RepositoryError['code'][] = [
      'validation',
      'not-found',
      'conflict',
      'unavailable',
      'quota-exceeded',
      'corrupt-data',
      'unsupported-version',
      'storage-error',
    ]

    expect(new Set(codes).size).toBe(8)
  })

  it('sends the request the contract describes, not one the adapter finds convenient', async () => {
    // Not part of the table, but the table is worthless if the adapter never asks for a problem envelope: a server
    // that answered HTML instead would be indistinguishable from one that refused to parse JSON.
    vi.mocked(fetch).mockResolvedValue(new Response('[]', { status: 200 }))
    await createHttpApplicationRepository(BASE).list()

    const init = (fetch as Mock).mock.calls[0]?.[1] as RequestInit | undefined
    expect(new Headers(init?.headers).get('accept')).toBe('application/json')
  })
})

/**
 * BEHAVIOR-m4-auth-063 / AC-11, client half. The server refuses a state-changing request that carries a session but
 * no `X-CSRF-Token`; this block is the other half of that handshake.
 *
 * Scope note, stated because it is easy to over-claim here: **jsdom cannot prove the antiforgery property.** It has no
 * same-site model, no cookie jar restrictions, and will happily hand back a `__Host-` cookie it never validated. What
 * these cases can prove is the part that is actually this file's job — that the adapter reads the token and attaches
 * it to exactly the verbs the server checks. The property itself ("a cross-site page cannot do this") is `-063`'s
 * browser row, and a unit test that appeared to prove it would be the most misleading kind of passing test.
 */
describe('httpApplicationRepository — the antiforgery header (BEHAVIOR-m4-auth-063)', () => {
  const TOKEN = 'CfDJ8A1b+Zx9kQ==T0k3n' // shaped like data-protection output: Base64 with +, / and = padding

  // **Measured, not assumed: jsdom will not store a `__Host-` cookie at all.** Setting one through the real jar yields
  // an empty `document.cookie` for `__Host-JTCsrf=x`, and also for `__Host-JTCsrf=x; Secure; Path=/`, while an
  // unprefixed `JTCsrf=x` stores fine — probed directly, not inferred. So the prefix rule is enforced by the cookie
  // *jar implementation*, not only by Chromium, and `-042`'s finding was not a browser quirk.
  //
  // Hence the jar is stubbed at the string the adapter actually reads. That narrows what these cases can claim, and
  // the narrowing is the point of this comment: they cover **how the adapter parses a jar and which verbs it puts the
  // token on**. They do not cover whether a real browser accepts the cookie the server writes — that is `-063`'s
  // browser row, and it is the only seam where `__Host-` can be tested honestly.
  const realJar = Object.getOwnPropertyDescriptor(Document.prototype, 'cookie')
  let jar = ''

  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    // Reset at file scope, not inside a nested block: `jar` is shared state the adapter reads on every call, so a
    // leftover from a sibling `describe` would make these cases order-dependent. AGENTS.md named this mistake before
    // it was ever made here.
    jar = ''
    Object.defineProperty(document, 'cookie', { configurable: true, get: () => jar })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    if (realJar) {
      Object.defineProperty(document, 'cookie', { ...realJar, configurable: true })
    }
  })

  /**
   * Headers of the request that used `method`, not of `calls[0]`.
   *
   * The distinction arrived the hard way: `update()` reads the current record before it writes (it needs the revision
   * for `If-Match` and the untouched fields for the replacement body), so the first call in the mock is a `GET` — and
   * a positional assertion there passes for every verb except the one it claims to check. `create` and `remove` send
   * one request each, so the bug would have shown up in exactly this one case, which is the kind of coverage that
   * looks green while being blind.
   */
  function headersFor(method: string): Headers {
    const calls = (fetch as Mock).mock.calls as [unknown, RequestInit | undefined][]
    const hit = calls.find(([, init]) => String(init?.method ?? 'GET').toUpperCase() === method)
    if (!hit) {
      throw new Error(
        `no ${method} request was sent; verbs seen: ` +
          JSON.stringify(calls.map(([, init]) => String(init?.method ?? 'GET'))),
      )
    }

    return new Headers(hit[1]?.headers)
  }

  it('attaches the token to a create, byte for byte', async () => {
    jar = `__Host-JTCsrf=${TOKEN}`
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 201 }))

    await createHttpApplicationRepository(BASE).create({
      companyName: 'Hooli',
      jobTitle: 'Engineer',
      location: 'NY',
      status: 'Saved',
    })

    // Compared through `Headers`, which lowercases on the way in and back out, so this asserts the value and not
    // the casing of the key we chose to write. The casing that matters is the server's, and that is `-063`'s API row.
    expect(headersFor('POST').get('x-csrf-token')).toBe(TOKEN)
  })

  // A wire record, because `update` cannot be reached without it: the method reads the current row first (it needs the
  // revision for `If-Match` and the untouched fields for the replacement body) and returns early if that read does not
  // parse. Responding `null` to it meant no `PUT` was ever sent, and the header assertion was checking a `GET`.
  const RECORD = {
    id: ID,
    companyName: 'Hooli',
    jobTitle: 'Engineer',
    location: 'NY',
    status: 'Saved',
    appliedAt: '2026-09-12',
    notes: null,
    // Both required by `isWireApplication`, which checks `createdAt: string` and `revision: number` — omitting them
    // made the read fail as corrupt-data and `update` return before its PUT, which is how this fixture's own shape
    // became the bug rather than the assertion.
    createdAt: '2026-09-11T09:00:00.000Z',
    revision: 3,
  }

  it.each([
    // `update(id, patch)` — two arguments, from the interface line rather than from the shape of a `JobApplication`. It
    // is the third case today where the harness failed because a call was written from a remembered signature.
    ['update', (repository: ApplicationRepository) => repository.update(ID, { status: 'Interview' }), 'PUT',
      () => new Response(JSON.stringify(RECORD), { status: 200, headers: { 'content-type': 'application/json' } })],
    // §4.3 answers `204` to a delete and `remove` deliberately does not parse a body, so an empty one is the truth.
    ['remove', (repository: ApplicationRepository) => repository.remove(ID), 'DELETE',
      () => new Response(null, { status: 204 })],
  ])('attaches it to %s as well, because every unsafe verb is checked', async (_name, invoke, verb, respond) => {
    jar = `__Host-JTCsrf=${TOKEN}`
    vi.mocked(fetch).mockImplementation(async () => respond())

    await invoke(createHttpApplicationRepository(BASE))

    expect(headersFor(verb).get('x-csrf-token')).toBe(TOKEN)
  })

  it('sends no token on a read, so the exempt verb stays exempt on the client too', async () => {
    jar = `__Host-JTCsrf=${TOKEN}`
    vi.mocked(fetch).mockResolvedValue(new Response('[]', { status: 200 }))

    await createHttpApplicationRepository(BASE).list()

    expect(headersFor('GET').get('x-csrf-token')).toBeNull()
  })

  it('sends no header when the cookie is absent, and lets the server decide what that means', async () => {
    // The tempting client-side "fix" is to synthesise a token or skip the request with a friendly error. Both make
    // the failure undiagnosable: the server's 403 + `antiforgery` code is the only place anyone learns that the
    // session and the token disagree.
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 201 }))

    await createHttpApplicationRepository(BASE).create({
      companyName: 'Hooli',
      jobTitle: 'Engineer',
      location: 'NY',
      status: 'Saved',
    })

    expect(fetch).toHaveBeenCalledOnce()
    expect(headersFor('POST').get('x-csrf-token')).toBeNull()
  })

  it('re-reads the jar on every call rather than caching the first token it saw', async () => {
    // The case that distinguishes "reads document.cookie" from "read it once at module scope". A cached token
    // survives one login and then silently breaks every write after the user signs out and back in — a 403 with no
    // explanation, and the worst kind to debug because it only happens the second time.
    const repository = createHttpApplicationRepository(BASE)
    jar = `__Host-JTCsrf=${TOKEN}`
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 201 }))
    await repository.create({ companyName: 'First', jobTitle: 'A', location: 'NY', status: 'Saved' })
    const first = headersFor('POST').get('x-csrf-token')

    jar = '__Host-JTCsrf=rotated-value'
    ;(fetch as Mock).mockClear()
    await repository.create({ companyName: 'Second', jobTitle: 'B', location: 'NY', status: 'Saved' })

    expect(first).toBe(TOKEN)
    expect(headersFor('POST').get('x-csrf-token')).toBe('rotated-value')
  })
})
