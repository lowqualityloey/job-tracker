import { describe, expect, it } from 'vitest'

import type { ApplicationInput } from '../types/application'
import { validateApplication as runClientValidator } from './validation'
import validationFixture from '../../api/tests/fixtures/validation-cases.json'

/**
 * The client half of AC-12 / grill F-1 — and the reason the shared file exists at all.
 *
 * `api/tests/fixtures/validation-cases.json` states every record-validation rule once, and this suite and
 * `ValidationContractTests.cs` each feed the same rows to their own validator. Without this file the C# validator
 * could pass a suite that encodes its *own* opinion of the rules and the client could keep a rule the server
 * dropped, and both would be green. The rows here were derived from `validation.ts`, so a mismatch between this
 * run and its expectations is not a broken test — it is a misreading of the client by whoever wrote a row.
 *
 * Two details about the mechanism, because both were checked rather than assumed:
 *  - the fixture is imported through `resolveJsonModule`, which `tsconfig.app.json` already enables. `node:fs`
 *    was the obvious route and is not: this project has no `@types/node` and pins `compilerOptions.types` to
 *    vitest and jest-dom, so a filesystem read would mean a new dependency to satisfy a contract test. An
 *    `import` also keeps the file inside the type system, so a malformed fixture is a build error here rather
 *    than a runtime surprise;
 *  - each row's `id` is stripped before validation. `ApplicationInput` is `Omit<JobApplication, 'id' | 'createdAt'>`
 *    and the API needs the id; the client validator must never see one, so a row that leaked its id into the
 *    input would be validating a type the client cannot construct.
 */

interface Violation {
  field: string
  message: string
}

interface FixtureCase {
  id: string
  why: string
  input: ApplicationInput & { id?: string }
  expect: { client: 'accept' | 'reject'; api: 'accept' | 'reject' }
  violations?: Violation[]
  clientViolations?: Violation[]
  apiViolations?: Violation[]
  stored?: Record<string, string>
}

const cases = (validationFixture as { cases: unknown }).cases

// Fail closed on the shape, in both directions: an empty or malformed fixture would otherwise make this file a
// suite of zero tests that reports success. A contract test whose data source broke silently is worse than none.
if (!Array.isArray(cases) || cases.length === 0) {
  throw new Error('validation-cases.json carries no cases; the AC-12 contract is not being tested')
}

const declared = cases as FixtureCase[]
const seenIds = new Set<string>()
for (const testCase of declared) {
  if (seenIds.has(testCase.id)) {
    throw new Error(`duplicate case id in validation-cases.json: ${testCase.id}`)
  }
  seenIds.add(testCase.id)
  if (!testCase.why) {
    throw new Error(`case ${testCase.id} states no reason, so nobody will know what it protects`)
  }
}

function expectedViolationsForClient(testCase: FixtureCase): Violation[] {
  return testCase.clientViolations ?? testCase.violations ?? []
}

describe('validation-cases.json — the client validator against the shared rules', () => {
  it.each(declared.map((testCase) => [testCase.id, testCase] as const))(
    '%s',
    (_id, testCase) => {
      // Explicit delete, not a `const { id: _omitted, ...input }` discard. The config here does permit `_`-prefixed
      // unused bindings (eslint.config.js: varsIgnorePattern '^_'), so this is a readability call and not a lint
      // workaround: the line should read "the client validator must never see an id", which a discard pattern
      // states as an afterthought.
      const input: Record<string, unknown> = { ...testCase.input }
      delete input.id
      const result = runClientValidator(input as unknown as ApplicationInput)

      if (testCase.expect.client === 'accept') {
        expect(result.ok).toBe(true)
        if (!result.ok) {
          return
        }

        // `stored` names the fields normalisation can move (a trim, or '' where trimming yields nothing); the rest
        // must survive untouched, which is what "the validator does not quietly edit my data" means in a check.
        for (const [field, value] of Object.entries(testCase.stored ?? {})) {
          expect((result.value as Record<string, unknown>)[field]).toBe(value)
        }

        return
      }

      expect(result.ok).toBe(false)
      if (result.ok) {
        return
      }

      expect(result.fieldErrors).toEqual(expectedViolationsForClient(testCase))
    },
  )

  it('carries at least one case per field the client validates', () => {
    // The count is the point, not the specific numbers: this assertion exists because a fixture can be technically
    // green while quietly ceasing to cover anything. If AC-12's coverage narrows, this fails and says so.
    const fieldsUnderTest = new Set<string>()
    for (const testCase of declared) {
      for (const violation of expectedViolationsForClient(testCase)) {
        fieldsUnderTest.add(violation.field)
      }
    }

    for (const field of ['companyName', 'jobTitle', 'location', 'appliedAt']) {
      // Set.has rather than expect(set).toContain: toContain is defined for arrays and strings, and a matcher that
      // silently does the wrong thing on a Set would let this guard rot while staying green.
      expect(fieldsUnderTest.has(field), `no fixture case rejects the client-side field '${field}'`).toBe(true)
    }
  })
})
