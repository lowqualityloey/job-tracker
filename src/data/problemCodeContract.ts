import type { FieldError } from '../domain/validation'
import type { RepositoryError } from '../domain/applicationRepository'

/**
 * The API's problem-document `code` vocabulary and the variant each one becomes — **one row per code, typed so a row is
 * mandatory**. BEHAVIOR-060; the `-037` pattern applied to the error contract instead of to validation fixtures.
 *
 * ## The defect this is for
 *
 * `unauthorized` has been emitted by the API since `-052`, and until now the client reached it through the mapping's
 * `default` branch. That is not a crash; it is a silent equivalence — a 401 and a malformed body both became
 * `corrupt-data`, the variant that means *"the server sent something my contract does not describe"*. Nothing in the build
 * compared the server's list with the client's, and the two lists live in different languages.
 *
 * ## How the table makes that impossible to repeat
 *
 * The type is `Record<ServerProblemCode, …>`, so a code added to the union without a row is a **compile error**, not a
 * runtime surprise. The union is asserted equal to `contracts/problem-codes.json` in `problemCodeContract.test.ts`, and the
 * same file is asserted against the API's own factories (enumerated by reflection in `ProblemCodeContractTests`). A new code
 * therefore fails a test on the side that added it — which is the only moment the fix is cheap.
 *
 * ## `unauthorized` maps to `corrupt-data`, provisionally
 *
 * DECISION-m4-auth-005 asks the owner to approve a **ninth** `RepositoryError` variant: `forbidden` would claim
 * "authenticated but not allowed", and `unavailable` would claim "server unreachable, retry" — a retry loop against a 401 is
 * a lockout generator. Widening M2a's frozen union needs an explicit yes, which `DECISION-006` got and this has not
 * demanded yet. The row exists so that the yes becomes one line here plus the provider behaviour in `-061`.
 */
export const SERVER_PROBLEM_CODES = ['validation', 'not-found', 'conflict', 'unauthorized'] as const

export type ServerProblemCode = (typeof SERVER_PROBLEM_CODES)[number]

/** What a row may consult. Only two things ever mattered: the id the caller addressed, and whether the `errors` array was
 *  shaped like ours. Kept this small on purpose — a row that could reach for the status code, the verb, and the request body
 *  would be a switch statement with extra syntax. */
export interface ProblemContext {
  readonly id: string | null
  readonly fieldErrors: FieldError[] | null
}

export type ProblemCodeRow = (context: ProblemContext) => RepositoryError

const corruptData: RepositoryError = { code: 'corrupt-data', quarantinedAs: null }

/**
 * Row-for-row. Order follows the wire, not importance: 400, 404, 409, 401 — the order a request can fail in.
 */
export const PROBLEM_CODE_TABLE: Record<ServerProblemCode, ProblemCodeRow> = {
  validation: (context) =>
    context.fieldErrors === null
      ? { code: 'storage-error', detail: 'validation named a field this client does not have' }
      : { code: 'validation', fieldErrors: context.fieldErrors },

  // `id === null` means the request addressed no record (a list), so a document naming a record cannot describe this call.
  'not-found': (context) => (context.id === null ? corruptData : { code: 'not-found', id: context.id }),

  conflict: (context) => (context.id === null ? corruptData : { code: 'conflict', id: context.id }),

  /** PROVISIONAL — see DECISION-m4-auth-005 above. Same value as the fallback, but *named*, so the change is one line and
   *  `problemCodeContract.test.ts` is the test that fails when the owner's approval arrives. */
  unauthorized: () => corruptData,
}

/**
 * Looks a row up by the wire value. Returns `undefined` for anything unrecognised — including an absent `code`, which is
 * what ASP.NET's own 415 produces — so the adapter's fail-closed branch stays visible at the call site rather than being
 * smuggled into a row. DECISION-m3-backend-api-004 is unchanged by this file: unknown still means `corrupt-data`, never
 * success.
 */
export function rowFor(code: string | null | undefined): ProblemCodeRow | undefined {
  if (typeof code !== 'string' || code.length === 0) {
    return undefined
  }

  return Object.prototype.hasOwnProperty.call(PROBLEM_CODE_TABLE, code)
    ? PROBLEM_CODE_TABLE[code as ServerProblemCode]
    : undefined
}
