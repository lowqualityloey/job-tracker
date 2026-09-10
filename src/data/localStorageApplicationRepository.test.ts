import { createLocalStorageRepository } from './localStorageApplicationRepository'

// The v4 shape is asserted, not just "is a string": a hand-rolled Math.random() uuid
// would satisfy a typeof check and still be wrong for a future server contract.
const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

const validInput = {
  companyName: 'Northwind Robotics',
  jobTitle: 'Senior Frontend Engineer',
  location: 'Remote (EU)',
  status: 'Applied' as const,
  appliedAt: '2026-09-03',
}

// Every test here asserts on record counts and ids, so the store must start empty in each
// one. This reset sits at FILE scope, not inside a describe: a beforeEach written inside one
// block does not apply to its siblings, and the leak turns the suite order-dependent — which
// is exactly what happened on the first Green attempt.
beforeEach(() => {
  localStorage.clear()
})

// BEHAVIOR-m2-persistence-seam-004
describe('localStorage application repository', () => {
  it('create() assigns a v4 string id and a createdAt stamp', async () => {
    const repository = createLocalStorageRepository()

    const result = await repository.create(validInput)

    expect(result.ok).toBe(true)
    if (!result.ok) return

    expect(typeof result.value.id).toBe('string')
    expect(result.value.id).toMatch(UUID_V4)
    expect(Number.isNaN(Date.parse(result.value.createdAt))).toBe(false)
  })
})

// BEHAVIOR-m2-persistence-seam-005
// "It is still there after I press F5" is the whole point of this milestone. A page reload
// gives the app a brand-new repository object over the same origin storage, so that is the
// exact condition the test reproduces — not a mocked internals call.
describe('localStorage application repository — persistence', () => {
  it('seeds the demo records on first run', async () => {
    const result = await createLocalStorageRepository().list()

    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value).toHaveLength(5)
  })

  it('shows a created record to a fresh repository instance', async () => {
    const created = await createLocalStorageRepository().create(validInput)
    expect(created.ok).toBe(true)
    if (!created.ok) return

    const listed = await createLocalStorageRepository().list()

    expect(listed.ok).toBe(true)
    if (listed.ok) {
      expect(listed.value).toHaveLength(6)
      expect(listed.value.map((record) => record.id)).toContain(created.value.id)
    }

    const fetched = await createLocalStorageRepository().get(created.value.id)
    expect(fetched.ok).toBe(true)
    if (fetched.ok) expect(fetched.value).toEqual(created.value)
  })

  it('returns not-found for an id that was never stored', async () => {
    const result = await createLocalStorageRepository().get('missing-id')

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toEqual({ code: 'not-found', id: 'missing-id' })
  })

  it('stores records inside a versioned envelope under the namespaced key', async () => {
    await createLocalStorageRepository().create(validInput)

    const raw = localStorage.getItem('job-tracker:applications')
    expect(raw).not.toBeNull()
    if (raw === null) return

    const envelope = JSON.parse(raw)
    expect(envelope.schemaVersion).toBe(1)
    expect(Array.isArray(envelope.applications)).toBe(true)
  })
})

// BEHAVIOR-m2-persistence-seam-006
// An edit must not mint a new identity or reset the creation timestamp: links, keys and
// ordering all hang off those two fields.
describe('localStorage application repository — update', () => {
  it('applies the patch and preserves id and createdAt', async () => {
    const created = await createLocalStorageRepository().create(validInput)
    expect(created.ok).toBe(true)
    if (!created.ok) return

    const updated = await createLocalStorageRepository().update(created.value.id, {
      status: 'Interview',
      notes: 'Loop booked for Thursday.',
    })

    expect(updated.ok).toBe(true)
    if (updated.ok) {
      expect(updated.value.status).toBe('Interview')
      expect(updated.value.notes).toBe('Loop booked for Thursday.')
      expect(updated.value.id).toBe(created.value.id)
      expect(updated.value.createdAt).toBe(created.value.createdAt)
    }

    const reread = await createLocalStorageRepository().get(created.value.id)
    expect(reread.ok).toBe(true)
    if (reread.ok) expect(reread.value.status).toBe('Interview')
  })

  it('leaves untouched fields alone', async () => {
    const created = await createLocalStorageRepository().create(validInput)
    if (!created.ok) return

    const updated = await createLocalStorageRepository().update(created.value.id, { status: 'Offer' })

    if (updated.ok) expect(updated.value.companyName).toBe('Northwind Robotics')
  })

  it('returns not-found for an unknown id instead of creating a record', async () => {
    const result = await createLocalStorageRepository().update('nope', { status: 'Offer' })

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toEqual({ code: 'not-found', id: 'nope' })

    const listed = await createLocalStorageRepository().list()
    if (listed.ok) expect(listed.value).toHaveLength(5)
  })
})

// BEHAVIOR-m2-persistence-seam-007
describe('localStorage application repository — remove', () => {
  it('deletes the record and reports which id went away', async () => {
    const created = await createLocalStorageRepository().create(validInput)
    if (!created.ok) return

    const removed = await createLocalStorageRepository().remove(created.value.id)

    expect(removed.ok).toBe(true)
    if (removed.ok) expect(removed.value).toEqual({ id: created.value.id })

    const after = await createLocalStorageRepository().get(created.value.id)
    expect(after.ok).toBe(false)
    if (!after.ok) expect(after.error).toEqual({ code: 'not-found', id: created.value.id })

    const listed = await createLocalStorageRepository().list()
    if (listed.ok) expect(listed.value).toHaveLength(5)
  })

  it('returns not-found when deleting an unknown id and removes nothing', async () => {
    const result = await createLocalStorageRepository().remove('nope')

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toEqual({ code: 'not-found', id: 'nope' })

    const listed = await createLocalStorageRepository().list()
    if (listed.ok) expect(listed.value).toHaveLength(5)
  })
})

// Hardening of AC-3 / BEHAVIOR-005, not new scope: the seed records are a module constant,
// so handing the array itself to a caller means one `.sort()` in a component would rewrite
// the shipped demo data for every later test and every later render.
describe('localStorage application repository — returned data is not aliased state', () => {
  it('does not expose the seed module array to caller mutation', async () => {
    const first = await createLocalStorageRepository().list()
    if (!first.ok) return

    first.value.pop()
    first.value.sort((a, b) => a.companyName.localeCompare(b.companyName))

    const second = await createLocalStorageRepository().list()
    expect(second.ok).toBe(true)
    if (second.ok) expect(second.value).toHaveLength(5)
  })
})
