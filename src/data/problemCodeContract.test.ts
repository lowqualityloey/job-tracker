import contractDocument from '../../contracts/problem-codes.json'
import { describe, expect, it } from 'vitest'
import { PROBLEM_CODE_TABLE, SERVER_PROBLEM_CODES, rowFor } from './problemCodeContract'
import type { ProblemContext } from './problemCodeContract'

/**
 * BEHAVIOR-060 — the problem-code → `RepositoryError` mapping is **table-driven row-for-row**, the `-037` pattern, so a
 * code cannot be added on either side without a row and no code can pass unobserved. Seam Unit (FE), p0.
 *
 * ## What is actually being defended
 *
 * The API's `code` vocabulary and the client's variant union are two lists in two languages that must agree, and nothing
 * in the build said so. Before this file, `unauthorized` — which the API has emitted since `-052`, on every request the
 * harness makes while unauthenticated — was handled by the mapping's `default` branch. That is not "unobserved" in the
 * sense of crashing: it is worse, because it *silently renders as `corrupt-data`*, which is the variant that says "the
 * server sent me something my contract does not describe". A 401 and a malformed body became the same user-facing state,
 * and the one signal that could have told us — `Object.keys` of the table versus the server's list — existed nowhere.
 *
 * Three assertions, each catching a different drift:
 *
 * 1. `SERVER_PROBLEM_CODES` (TS) equals `contracts/problem-codes.json` — the shared artifact both suites read.
 * 2. Every code in the contract has an **explicit row**; the `default` branch may only serve codes nobody has declared.
 * 3. Each row's mapping is what the row says, asserted per row from the table rather than from the prose.
 *
 * The API side has its own guard (`ProblemCodeContractTests`), which enumerates the codes by invoking `Problems`'s own
 * factory methods by reflection. That direction matters as much: adding `Problems.SessionExpired(...)` fails a test in
 * the suite that *ships the code*, rather than surfacing months later as a client rendering nonsense.
 *
 * ## `unauthorized` maps to `corrupt-data` here, and that is deliberate
 *
 * DECISION-m4-auth-005 asks the owner to approve a **ninth** `RepositoryError` variant, because `forbidden` ("authenticated
 * but not allowed") and `unavailable` ("server unreachable, retry") are both wrong, and a retry loop against a 401 is a
 * lockout generator. Widening M2a's frozen union needs their explicit yes — `DECISION-006` got one — so this table does not
 * assume it. What it does instead is make that yes a **one-line change plus provider behaviour in `-061`**: the row exists,
 * is named, and is the only place to edit. Until then a 401 renders as corrupt-data, which is the same thing that happens
 * today; the difference is that a test now records the fact as provisional rather than as an accident.
 */

interface Contract {
  codes: string[]
}

// Imported rather than read from disk, for two reasons. The mechanical one: `tsconfig.app.json` lists
// `types: ["vitest/globals", "@testing-library/jest-dom"]` with no `@types/node`, so `node:fs` and `process` do not
// typecheck in the browser project at all (TS2307/TS2591) -- the run told me, my reading of the config did not. The
// substantive one: `resolveJsonModule` is already on, so an import puts the contract's SHAPE in the type system, where a
// readFileSync + JSON.parse pair puts only a string.
const contract = contractDocument as Contract

const withId: ProblemContext = { id: 'a1b2c3d4-0000-4000-8000-000000000001', fieldErrors: [] }
const withoutId: ProblemContext = { id: null, fieldErrors: [] }

describe('the problem-code table is row-for-row with the API contract (BEHAVIOR-060)', () => {
  it('the TypeScript union and the shared contract list the same codes', () => {
    // The shared file is the only place both suites can look, and it is read rather than duplicated: a second copy of a
    // contract is a second thing to forget.
    expect([...SERVER_PROBLEM_CODES].sort()).toEqual([...contract.codes].sort())
  })

  it('every code the API can emit has an explicit row, so no code passes through default', () => {
    const declared = Object.keys(PROBLEM_CODE_TABLE).sort()
    const missing = contract.codes.filter((code) => !declared.includes(code))
    expect(missing, `codes with no row (they are handled by the unknown-code fallback): ${missing.join(', ')}`).toEqual([])
  })

  it('validation maps to the validation variant when the fields are known, and fails closed when they are not', () => {
    const known = PROBLEM_CODE_TABLE.validation({ ...withId, fieldErrors: [{ field: 'status', message: 'Escalated is not a status this client models' }] })
    expect(known).toEqual({ code: 'validation', fieldErrors: [{ field: 'status', message: 'Escalated is not a status this client models' }] })

    // `fieldErrors: null` is the adapter's "the errors array was not shaped like ours" signal, and it must not be dressed
    // up as a validation error the form cannot display.
    expect(PROBLEM_CODE_TABLE.validation({ ...withId, fieldErrors: null })).toMatchObject({ code: 'storage-error' })
  })

  it('not-found and conflict answer by id when the request named one, and fail closed when it did not', () => {
    expect(PROBLEM_CODE_TABLE['not-found'](withId)).toEqual({ code: 'not-found', id: withId.id })
    expect(PROBLEM_CODE_TABLE.conflict(withId)).toEqual({ code: 'conflict', id: withId.id })

    // A list request has no id to name; -056's scoping makes that case reachable in anger, because a 404 on a route that
    // never carried an id means the response is not describing a record we asked for.
    expect(PROBLEM_CODE_TABLE['not-found'](withoutId)).toEqual({ code: 'corrupt-data', quarantinedAs: null })
    expect(PROBLEM_CODE_TABLE.conflict(withoutId)).toEqual({ code: 'corrupt-data', quarantinedAs: null })
  })

  it('unauthorized maps to its OWN variant -- DECISION-m4-auth-005 approved by the owner on 2026-09-12', () => {
    const row = rowFor('unauthorized')
    expect(row, 'the 401 code must be declared, not caught by the fallback').toBeTypeOf('function')
    // This assertion is the one -060 left failing on purpose: it pinned the PROVISIONAL mapping (corrupt-data) so that the
    // owner's approval would break it rather than slide past it unnoticed. It broke, which is the mechanism working.
    expect(row?.(withId)).toEqual({ code: 'unauthorized' })
    // And the point of the distinct variant, stated here because this is where a reader arrives looking for it: a 401
    // must not be retried. corrupt-data and unavailable both invite the user to try again, and retrying a 401 is how you
    // turn a sign-out into a lockout. -061 builds the screen that this value redirects to.
    expect(row?.(withId)).not.toEqual({ code: 'corrupt-data', quarantinedAs: null })
  })

  it('a code nobody has declared still fails closed', () => {
    // DECISION-m3-backend-api-004 survives this refactor on purpose. The table must not become a whitelist that quietly
    // returns success, and it must not throw: a 415 with no `code` at all has to land in the same place it does today.
    expect(rowFor('session-expired')).toBeUndefined()
    expect(rowFor('')).toBeUndefined()
    expect(rowFor(undefined)).toBeUndefined()
  })

  it('every row is total: a declared code never yields undefined', () => {
    for (const code of contract.codes) {
      const row = rowFor(code)
      expect(row, `${code} is in the contract but has no row`).toBeTypeOf('function')
      expect(row?.(withId)).toBeTruthy()
      expect(row?.(withoutId)).toBeTruthy()
    }
  })
})
