import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { ApplicationInput } from '../types/application'
import { createHttpApplicationRepository } from './httpApplicationRepository'

/**
 * BEHAVIOR-m3-backend-api-041 — **does the text a user typed survive the wire unchanged?**
 *
 * The API half of this behaviour is already recorded, and it found a real bug: the shared fixture's
 * `company-name-bom-only` row failed with `Expected: BadRequest, Actual: Created`, because ECMAScript's `trim()`
 * removes U+FEFF and .NET's `char.IsWhiteSpace` does not — two validators disagreeing about whether a company name
 * exists at all. That row, plus `job-title-at-max-emoji` (60 astral emoji = 120 UTF-16 units, the number *both*
 * languages measure), is AC-12's cross-language coverage and lives in the fixture.
 *
 * This file is the **adapter's** half: between a domain record in memory and a request body on the wire, nothing
 * edits the text. Three things in a stack like this edit text while looking like an improvement — normalisation
 * (`normalize()`, which silently merges two visually identical but distinct sequences), truncation (usually added to
 * protect a column, and in UTF-16 it can cut a surrogate pair in half and store U+FFFD where an emoji was), and
 * trimming on send (which ends the byte-for-byte claim quietly, because the value the user typed stops being the
 * value stored).
 *
 * The `fetch` stub is an **echo**: it builds the stored record from the request body it received. That is what makes
 * the assertion end-to-end rather than a test of `JSON.stringify` — if the adapter mangles a string on the way out,
 * the echo carries the damage forward, and if it mangles one on the way in, the returned record differs from the
 * input.
 *
 * The last group is not about unicode at all, and was found *by* building the echo: the API answers
 * `"location": null` for a record whose location was never given, while `JobApplication.location` is a required
 * `string`. The runtime guard checked six of nine fields, so `null` passed straight through into a type that says it
 * cannot be. **The structural-typing seam AC-1 is built on is only as honest as the guard backing it.**
 *
 * Source discipline for a file about invisible characters: every exotic code point below is written as an escape, not
 * as the character itself. A literal U+2028 in a source file is a line terminator to the parser and to anyone reading
 * it on a phone, and "the test that passes but cannot be reviewed" is its own kind of failure.
 */

const BASE = 'http://api.test:8080'
const ID = '3f9b4d0c-6c1e-4a2f-9b7f-4a3d2e1f0a9b'

/** Each case is chosen for the mechanism it stresses, not for looking exotic. */
const tricky: Array<[string, string]> = [
  ['astral plane emoji', '🧃 Engineer'],
  ['ZWJ family sequence', '👩‍👩‍👧‍👦 Family'],
  ['combining diacritic', 'Hoši'],
  ['precomposed next to decomposed', 'caf\u00E9 cafe\u0301'],
  ['right-to-left in mixed direction', 'הוליווד Engineer'],
  ['byte-order mark', '\uFEFFFishermend'],
  ['zero-width space, which both trims keep', 'Fishermend\u200B'],
  ['line and paragraph separators', 'a\u2028b\u2029c'],
  ['quotes and backslashes', 'He said "no"\\ truly'],
  ['control characters', 'a\u0007b\u001Bc'],
  ['emoji at the length boundary', '🧃'.repeat(60)],
]

/**
 * Reads the JSON body the adapter actually put on the wire.
 *
 * `RequestInit.body` is a wide union (`BodyInit`), so `String(body)` would render a `Blob` or `FormData` as
 * `[object Object]` and the lint rule is right to refuse it. Narrowing to the string case first is not ceremony: a
 * future adapter that switched to `FormData` should make these tests fail loudly rather than quietly assert against
 * a meaningless coercion.
 */
function bodyOf(init: RequestInit | undefined): Record<string, unknown> {
  const raw = init?.body
  return typeof raw === 'string' ? (JSON.parse(raw) as Record<string, unknown>) : {}
}

function baseRecord(overrides: Record<string, unknown>): Record<string, unknown> {
  return {
    id: ID,
    companyName: 'Hooli',
    jobTitle: 'Engineer',
    location: 'New York',
    status: 'Applied',
    appliedAt: null,
    notes: null,
    createdAt: '2026-03-01T09:00:00Z',
    revision: 1,
    ...overrides,
  }
}

/** An echo server: the stored record is built from whatever body the adapter actually sent. */
function echoing(body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(baseRecord(body)), {
    status: 201,
    headers: { 'content-type': 'application/json' },
  })
}

function wireWith(body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}

function input(companyName: string, notes?: string): ApplicationInput {
  return {
    companyName,
    jobTitle: 'Engineer',
    location: 'New York',
    status: 'Applied',
    ...(notes === undefined ? {} : { notes }),
  }
}

describe('encoding fidelity — every string survives unicode and length unchanged on the wire', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    vi.stubGlobal('crypto', { randomUUID: () => ID })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each(tricky)('a companyName with %s survives unicode identity', async (_label, value) => {
    const mock = vi.mocked(fetch)
    mock.mockImplementation(async (_url: unknown, init?: RequestInit) => echoing(bodyOf(init)))

    const repository = createHttpApplicationRepository(BASE)
    const result = await repository.create(input(value))

    expect(result.ok).toBe(true)
    if (!result.ok) {
      return
    }

    // Identity, not equality-after-something: `toBe` on a string compares the same sequence of code units, so a
    // normalisation, a mojibake round trip through a wrong charset, or a lost variation selector all fail here.
    expect(result.value.companyName).toBe(value)

    // And the *request body* has to carry it too. The returned object is produced by the adapter's own parser from
    // the echo's JSON, so this pair of assertions covers both directions of the mapping in one test.
    expect(bodyOf(mock.mock.calls[0]?.[1]).companyName).toBe(value)
  })

  it('a 10 kB note survives unicode and keeps its exact length', async () => {
    const mock = vi.mocked(fetch)
    mock.mockImplementation(async (_url: unknown, init?: RequestInit) => echoing(bodyOf(init)))

    // 10,000 characters of astral-plane emoji is 20,000 UTF-16 units: the row the fixture uses for the `notes`
    // ceiling question, and the size at which a truncation that "protects the column" would show up.
    const notes = '🧃'.repeat(5000)
    expect(notes.length).toBe(10_000)

    const repository = createHttpApplicationRepository(BASE)
    const result = await repository.create(input('Hooli', notes))

    expect(result.ok).toBe(true)
    if (!result.ok) {
      return
    }

    expect(result.value.notes).toHaveLength(10_000)
    // A split surrogate pair is the specific corruption length alone would not catch: the halves survive as two
    // U+FFFD replacement characters, so the count stays right while the content quietly changes.
    expect(result.value.notes).not.toContain('\uFFFD')
    expect(result.value.notes).toBe(notes)
  })

  it('a decomposed name and a precomposed one survive unicode as two different records', async () => {
    const mock = vi.mocked(fetch)
    mock.mockImplementation(async (_url: unknown, init?: RequestInit) => echoing(bodyOf(init)))

    const repository = createHttpApplicationRepository(BASE)
    // Written as escapes because the two literals look identical in an editor — which is the entire hazard under
    // test. \u00E9 is one code unit, e\u0301 is two; the first is "café" precomposed, the second decomposed.
    const composed = await repository.create(input('caf\u00E9'))
    const decomposed = await repository.create(input('cafe\u0301'))

    expect(composed.ok && decomposed.ok).toBe(true)
    if (!composed.ok || !decomposed.ok) {
      return
    }

    // Both render identically. If anything in the chain normalises, these two become the same string and a user who
    // means to add a second employer ends up editing… nothing, with no error anywhere to explain it.
    expect(composed.value.companyName).not.toBe(decomposed.value.companyName)
    expect(composed.value.companyName.length).toBe(4)
    expect(decomposed.value.companyName.length).toBe(5)
  })

  it('the wire’s absent location survives unicode as an empty string, not as a null the types forbid', async () => {
    const mock = vi.mocked(fetch)
    mock.mockImplementation(async () => wireWith(baseRecord({ location: null })))

    const result = await createHttpApplicationRepository(BASE).get(ID)

    // The decision, taken here rather than in the domain model (see the register): `JobApplication.location` stays a
    // required `string`, because every form field and every template in the app treats it as one. So the adapter maps
    // the wire's third state into the client's two-state model — at the seam, where the two schemas actually meet.
    // The rejected alternative was making `location` optional across the domain, which pushes a null check into
    // every consumer to represent a state only one backend can produce.
    expect(result.ok).toBe(true)
    if (!result.ok) {
      return
    }

    expect(result.value.location).toBe('')
    // Asserted as a type fact as well as a value: `null` here would satisfy no compile-time check, because the guard
    // claimed to verify a field it never looked at.
    expect(typeof result.value.location).toBe('string')
  })

  it('appliedAt and notes nulls survive unicode into absent optional fields, not null-in-a-string-typed-field', async () => {
    const mock = vi.mocked(fetch)
    mock.mockImplementation(async () => wireWith(baseRecord({ appliedAt: null, notes: null })))

    const result = await createHttpApplicationRepository(BASE).get(ID)
    expect(result.ok).toBe(true)
    if (!result.ok) {
      return
    }

    // `appliedAt?: string` says *absent*. The API says `null`, which is a different value in a different language's
    // type system, and it was flowing straight through: `undefined` for the optional-field checks the app actually
    // performs, and no lie where a `null` would have sat.
    expect(result.value.appliedAt).toBeUndefined()
    expect(result.value.notes).toBeUndefined()
  })

  it('a location of the wrong JSON type is refused rather than laundered into a string', async () => {
    const mock = vi.mocked(fetch)
    mock.mockImplementation(async () => wireWith(baseRecord({ location: 42 })))

    const result = await createHttpApplicationRepository(BASE).get(ID)

    // Now that location is checked, a number is a corrupt record — the same fail-closed rule `-037` applies to an
    // unreadable 200. What must not happen is the previous behaviour: no check, so `42` became a `string` by
    // declaration.
    expect(result.ok).toBe(false)
    if (result.ok) {
      return
    }

    expect(result.error.code).toBe('corrupt-data')
  })
})
