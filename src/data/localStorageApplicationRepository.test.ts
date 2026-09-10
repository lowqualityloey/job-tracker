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

// BEHAVIOR-m2-persistence-seam-004
describe('localStorage application repository', () => {
  beforeEach(() => {
    localStorage.clear()
  })

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
