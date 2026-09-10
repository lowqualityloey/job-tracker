import {
  APPLICATIONS_STORAGE_KEY,
  createInMemoryRepository,
  createLocalStorageRepository,
  isStorageAvailable,
  type StorageLike,
} from './localStorageApplicationRepository'
import type { JobApplication } from '../types/application'
import { seedApplications } from './seedApplications'

const validInput = {
  companyName: 'Northwind Robotics',
  jobTitle: 'Senior Frontend Engineer',
  location: 'Remote (EU)',
  status: 'Applied' as const,
  appliedAt: '2026-09-03',
}

function writeThrows(name: string): Storage {
  return {
    getItem: () => null,
    removeItem: () => {},
    key: () => null,
    clear: () => {},
    setItem: () => {
      throw new DOMException('blocked', name)
    },
    length: 0,
  } as unknown as Storage
}

function fullButUsableStore(): Storage {
  return {
    getItem: () => null,
    removeItem: () => {},
    key: () => null,
    clear: () => {},
    setItem: () => {
      throw new DOMException('quota', 'QuotaExceededError')
    },
    length: 3,
  } as unknown as Storage
}

// BEHAVIOR-m2-persistence-seam-008
describe('storage availability probe', () => {
  it('reports unavailable when setItem throws even though the object exists', () => {
    // This is the case MDN warns about: `typeof localStorage !== 'undefined'` returns true
    // here, so an existence check would tell the user their work was saved when it was not.
    expect(isStorageAvailable(writeThrows('SecurityError'))).toBe(false)
  })

  it('reports unavailable for a private-mode store that exists with a zero quota', () => {
    const store = writeThrows('QuotaExceededError')
    expect(typeof store).toBe('object')
    expect(isStorageAvailable(store)).toBe(false)
  })

  it('reports available for a store that exists and works', () => {
    localStorage.clear()
    expect(isStorageAvailable(localStorage)).toBe(true)
  })

  it('distinguishes "full" from "off": a quota error over a non-empty store is available', () => {
    // MDN's own discrimination — a failing write on a store that already holds data means
    // full, not disabled, and the two need different messages to the user.
    expect(isStorageAvailable(fullButUsableStore())).toBe(true)
  })

  it('leaves no sentinel behind after probing', () => {
    localStorage.clear()
    isStorageAvailable(localStorage)
    expect(localStorage.length).toBe(0)
  })
})

describe('repository selection when storage is unavailable', () => {
  it('falls back to an in-memory repository that still accepts writes this session', async () => {
    const repository = createInMemoryRepository(seedApplications)

    const created = await repository.create(validInput)
    expect(created.ok).toBe(true)

    const listed = await repository.list()
    if (listed.ok) expect(listed.value).toHaveLength(6)
  })

  it('reports the fault instead of silently pretending to persist', async () => {
    const repository = createLocalStorageRepository({ storage: writeThrows('SecurityError') })

    const result = await repository.create(validInput)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toEqual({ code: 'unavailable' })
  })
})

// BEHAVIOR-m2-persistence-seam-009
// A store can pass the availability probe and still refuse one particular write. The
// requirement is that a refused write leaves nothing half-applied and is reported as
// itself — not as a generic failure, and not as success.
/** A store that works until told otherwise, so "was fine, then filled up" is testable. */
function fakeWorkingStore(): StorageLike & { forceQuotaError: boolean } {
  const backing = new Map<string, string>()
  const fake = {
    forceQuotaError: false,
    getItem: (key: string) => backing.get(key) ?? null,
    removeItem: (key: string) => void backing.delete(key),
    get length() {
      return backing.size
    },
    setItem: (key: string, value: string) => {
      if (fake.forceQuotaError) {
        throw new DOMException('quota', 'QuotaExceededError')
      }
      backing.set(key, value)
    },
  }
  return fake as StorageLike & { forceQuotaError: boolean }
}

function storeThatFailsOnlyOn(failKey: string): StorageLike {
  const backing = new Map<string, string>()

  return {
    getItem: (key) => backing.get(key) ?? null,
    removeItem: (key) => void backing.delete(key),
    get length() {
      return backing.size
    },
    setItem: (key, value) => {
      if (key === failKey) {
        throw new DOMException('quota', 'QuotaExceededError')
      }
      backing.set(key, value)
    },
  }
}

describe('quota exceeded during a write', () => {
  it('reports quota-exceeded rather than success or a generic error', async () => {
    const repository = createLocalStorageRepository({
      storage: storeThatFailsOnlyOn(APPLICATIONS_STORAGE_KEY),
    })

    const result = await repository.create(validInput)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toEqual({ code: 'quota-exceeded' })
  })

  it('leaves the stored envelope untouched when a write is refused', async () => {
    const storage = fakeWorkingStore()
    const repository = createLocalStorageRepository({ storage })

    const created = await repository.create(validInput)
    expect(created.ok).toBe(true)
    if (!created.ok) return

    const written = storage.getItem(APPLICATIONS_STORAGE_KEY)
    expect(written).not.toBeNull()

    storage.forceQuotaError = true
    const failed = await repository.update(created.value.id, { status: 'Offer' })
    expect(failed.ok).toBe(false)
    if (!failed.ok) expect(failed.error).toEqual({ code: 'quota-exceeded' })

    // Asserted on the raw durable artefact rather than through a second repository: a new
    // instance re-runs the availability probe, which correctly fails while the store is in
    // its forced-error state. That coupling was my first attempt's bug, not the code's.
    expect(storage.getItem(APPLICATIONS_STORAGE_KEY)).toBe(written)
    const envelope = JSON.parse(written ?? '{}') as { applications: JobApplication[] }
    expect(envelope.applications).toHaveLength(6)
    expect(envelope.applications[5].status).not.toBe('Offer')
  })
})

// File scope, not inside a describe: a beforeEach in one block does not apply to its
// siblings, and every case here asserts on localStorage.key(i) enumeration, so leftovers
// from an earlier block read as new evidence. This file had that bug the moment the
// quarantine-count assertion was added.
beforeEach(() => {
  localStorage.clear()
})

// BEHAVIOR-m2-persistence-seam-010
// Unreadable stored data is the user's, not noise to be overwritten. The store must keep a
// retrievable copy before it reseeds, so a bad parse never destroys anything.
const CORRUPT_PREFIX = `${APPLICATIONS_STORAGE_KEY}:corrupt-`

function storedKeys(): string[] {
  return Array.from({ length: localStorage.length }, (_, i) => localStorage.key(i) ?? '')
}

describe('corrupt stored payload', () => {
  it('quarantines an unparseable value and reports where it went', async () => {
    const garbage = '{"schemaVersion": 1, "applications": [oops'
    localStorage.setItem(APPLICATIONS_STORAGE_KEY, garbage)

    const listed = await createLocalStorageRepository().list()

    expect(listed.ok).toBe(false)
    if (listed.ok) return
    expect(listed.error.code).toBe('corrupt-data')
    if (listed.error.code !== 'corrupt-data') return

    const quarantined = storedKeys().filter((key) => key.startsWith(CORRUPT_PREFIX))
    expect(quarantined).toHaveLength(1)
    expect(localStorage.getItem(quarantined[0])).toBe(garbage)
    expect(listed.error.quarantinedAs).toBe(quarantined[0])
  })

  it('recovers on the next read: a fresh store seeds and new writes succeed', async () => {
    localStorage.setItem(APPLICATIONS_STORAGE_KEY, 'not json at all')
    const repository = createLocalStorageRepository()
    expect((await repository.list()).ok).toBe(false)

    const created = await repository.create(validInput)
    expect(created.ok).toBe(true)

    const listed = await repository.list()
    if (listed.ok) expect(listed.value).toHaveLength(6)
  })

  it('rejects an envelope whose applications field is not an array', async () => {
    localStorage.setItem(APPLICATIONS_STORAGE_KEY, '{"schemaVersion":1,"applications":"everything"}')

    const result = await createLocalStorageRepository().list()

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error.code).toBe('corrupt-data')
  })

  it('rejects a record that is missing required fields instead of trusting the cast', async () => {
    // Shape validation, not a bare `as JobApplication[]`: a stored payload crosses the same
    // trust boundary as form input, and a partial record renders as undefined text everywhere.
    localStorage.setItem(
      APPLICATIONS_STORAGE_KEY,
      JSON.stringify({ schemaVersion: 1, applications: [{ id: 'only-an-id' }] }),
    )

    const result = await createLocalStorageRepository().list()

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error.code).toBe('corrupt-data')
  })

  it('drops an unknown optional field rather than passing it through', async () => {
    localStorage.setItem(
      APPLICATIONS_STORAGE_KEY,
      JSON.stringify({
        schemaVersion: 1,
        applications: [{ ...seedApplications[0], salaryExpectation: 'unlimited' }],
      }),
    )

    const result = await createLocalStorageRepository().list()

    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value[0]).not.toHaveProperty('salaryExpectation')
  })
})

// BEHAVIOR-m2-persistence-seam-011
// A payload written by a newer build is not corruption. Treating it as unreadable-and-then-
// reseeded is how a downgrade silently deletes a user's real records, so this path must
// refuse, preserve, and NOT quarantine.
describe('payload from a newer schema version', () => {
  const futureEnvelope = JSON.stringify({
    schemaVersion: 99,
    applications: [{ ...seedApplications[0], id: 'written-by-a-newer-build' }],
  })

  it('reports unsupported-version instead of reading fields it cannot trust', async () => {
    localStorage.setItem(APPLICATIONS_STORAGE_KEY, futureEnvelope)

    const result = await createLocalStorageRepository().list()

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toEqual({ code: 'unsupported-version', found: 99 })
  })

  it('writes nothing and leaves the stored bytes byte-identical', async () => {
    localStorage.setItem(APPLICATIONS_STORAGE_KEY, futureEnvelope)

    const created = await createLocalStorageRepository().create(validInput)

    expect(created.ok).toBe(false)
    if (!created.ok) expect(created.error).toEqual({ code: 'unsupported-version', found: 99 })
    expect(localStorage.getItem(APPLICATIONS_STORAGE_KEY)).toBe(futureEnvelope)
  })

  it('does not quarantine a value that is not actually broken', async () => {
    localStorage.setItem(APPLICATIONS_STORAGE_KEY, futureEnvelope)
    await createLocalStorageRepository().list()

    expect(storedKeys().filter((key) => key.startsWith(CORRUPT_PREFIX))).toHaveLength(0)
  })
})
