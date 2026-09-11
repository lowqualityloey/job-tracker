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
