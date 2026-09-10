import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ApplicationFormPage from './ApplicationFormPage'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import type { ApplicationRepository, RepositoryError, Result } from '../domain/applicationRepository'
import { ApplicationsProvider } from '../state/applicationsProvider'
import type { ApplicationInput, JobApplication } from '../types/application'

function withFailingCreate(error: RepositoryError) {
  const base = createInMemoryRepository(seedApplications)
  const repository: ApplicationRepository = {
    ...base,
    create: (): Promise<Result<JobApplication>> => Promise.resolve({ ok: false, error }),
  }
  render(
    <MemoryRouter initialEntries={['/applications/new']}>
      <ApplicationsProvider repository={repository}>
        <Routes>
          <Route path="/applications/new" element={<ApplicationFormPage />} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
  return base
}

function fill(field: string, value: string) {
  fireEvent.change(screen.getByLabelText(new RegExp(field, 'i')), { target: { value } })
}

async function submitValidForm() {
  await screen.findByLabelText(/company name/i)
  fill('Company', 'Fishermend')
  fill('Job title', 'Frontend Engineer')
  fill('Location', 'Auckland, NZ')
  fireEvent.click(screen.getByRole('button', { name: /save application/i }))
}

// M2a.5 contract sweep. Every RepositoryError code must be produced somewhere and mean
// something specific on screen; the two below were the measured gaps.
describe('repository errors reach the user with their own message', () => {
  it('puts a repository validation failure on the field it belongs to', async () => {
    withFailingCreate({
      code: 'validation',
      fieldErrors: [{ field: 'companyName', message: 'You already track Fishermend.' }],
    })

    await submitValidForm()

    expect(await screen.findByText('You already track Fishermend.')).toBeInTheDocument()
    const company = screen.getByLabelText(/company name/i)
    expect(company).toHaveAttribute('aria-invalid', 'true')
    expect(company).toHaveAttribute('aria-describedby', 'companyName-error')
    // Not also dumped as an unattributed banner: one rejection, one place to read it.
    expect(screen.queryAllByRole('alert')).toHaveLength(1)
  })

  it('gives a rejected storage write its own sentence instead of the fallback', async () => {
    withFailingCreate({ code: 'storage-error', detail: 'write rejected' })

    await submitValidForm()

    expect(await screen.findByText(/refused to save/i)).toBeInTheDocument()
    expect(screen.queryByText('Something went wrong while saving. Nothing was changed.')).toBeNull()
    // detail is a developer string; the user is not shown a raw storage-layer reason.
    expect(screen.queryByText('write rejected')).toBeNull()
  })

  it('names the newer app version when the stored format is ahead of this build', async () => {
    const base = createInMemoryRepository(seedApplications)
    const repository: ApplicationRepository = {
      ...base,
      create: (): Promise<Result<JobApplication>> =>
        Promise.resolve({ ok: false, error: { code: 'unsupported-version', found: 2 } }),
    }
    render(
      <MemoryRouter initialEntries={['/applications/new']}>
        <ApplicationsProvider repository={repository}>
          <Routes>
            <Route path="/applications/new" element={<ApplicationFormPage />} />
          </Routes>
        </ApplicationsProvider>
      </MemoryRouter>,
    )

    await submitValidForm()

    expect(await screen.findByText(/format 2/)).toBeInTheDocument()
  })

  it.each([
    ['not-found', { code: 'not-found', id: 'x' } as RepositoryError],
    ['unavailable', { code: 'unavailable' } as RepositoryError],
    ['quota-exceeded', { code: 'quota-exceeded' } as RepositoryError],
    [
      'corrupt-data',
      { code: 'corrupt-data', quarantinedAs: 'job-tracker:applications:corrupt-x' } as RepositoryError,
    ],
  ])('%s already renders its own sentence', async (_code, error) => {
    withFailingCreate(error as RepositoryError)
    await submitValidForm()
    expect(screen.queryByText('Something went wrong saving that application. Nothing was changed.')).toBeNull()
    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
