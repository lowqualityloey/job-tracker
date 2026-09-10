import { createInMemoryRepository, createLocalStorageRepository, isStorageAvailable } from './localStorageApplicationRepository'
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
