import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import ApplicationForm, { EMPTY_APPLICATION } from '../components/ApplicationForm'
import EmptyState from '../components/EmptyState'
import type { RepositoryError } from '../domain/applicationRepository'
import { validateApplication, type FieldError } from '../domain/validation'
import { useApplications } from '../state/applicationsProvider'
import type { ApplicationInput } from '../types/application'

/**
 * Turns a storage fault into a sentence. Kept at the edge of the view layer: the domain
 * names the failure, only this file decides what the user reads about it.
 */
function describeError(error: RepositoryError): string {
  switch (error.code) {
    case 'quota-exceeded':
      return 'This device is out of storage for saved data. Delete an application you no longer need, then save again.'
    case 'unavailable':
      return 'This device is blocking saved data, so this change could not be kept. It is still on screen for this tab only.'
    case 'storage-error':
      // Its detail field is a developer string (a DOMException name from the adapter) and
      // stays out of the sentence: the user needs the consequence, not the exception.
      return 'This browser refused to save the change, so nothing was changed. Reloading may help.'
    case 'not-found':
      return 'That application no longer exists. It may have been deleted in another tab.'
    case 'corrupt-data':
      return 'Saved data could not be read, so nothing was overwritten. The unreadable copy was kept for recovery.'
    case 'unsupported-version':
      return `Saved data was written by a newer version of this app (format ${error.found}), so this version refused to change it.`
    // Named rather than left to `default`, because the difference is the whole point of the ninth variant: every other
    // sentence here ends with an invitation to try again, and trying again is precisely what a 401 forbids.
    case 'unauthorized':
      return 'You have been signed out, so nothing was changed. Sign in again to continue.'

    default:
      return 'Something went wrong while saving. Nothing was changed.'
  }
}

export default function ApplicationFormPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { status, getApplication, createApplication, updateApplication } = useApplications()
  const [fieldErrors, setFieldErrors] = useState<FieldError[]>([])
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const isEdit = id !== undefined
  const existing = isEdit ? getApplication(id) : undefined

  if (isEdit && status !== 'ready') {
    return <EmptyState title="Loading…" description="Fetching this application." />
  }

  if (isEdit && existing === undefined) {
    return (
      <EmptyState
        title="Application not found"
        description="It may have been deleted, or the link is out of date."
        action={<Link to="/applications">Back to applications</Link>}
      />
    )
  }

  const initialValues: ApplicationInput = existing
    ? {
        companyName: existing.companyName,
        jobTitle: existing.jobTitle,
        location: existing.location,
        status: existing.status,
        appliedAt: existing.appliedAt ?? '',
        notes: existing.notes ?? '',
      }
    : EMPTY_APPLICATION

  async function handleSubmit(input: ApplicationInput) {
    setPending(true)
    setFieldErrors([])
    setFormError(null)

    const validated = validateApplication(input)

    if (!validated.ok) {
      setFieldErrors(validated.fieldErrors)
      setPending(false)
      return
    }

    const result = existing
      ? await updateApplication(existing.id, validated.value)
      : await createApplication(validated.value)

    setPending(false)

    if (!result.ok) {
      if (result.error.code === 'validation') {
        // The repository already knows which field is wrong; a banner would be a worse
        // version of the information it just handed us.
        setFieldErrors(result.error.fieldErrors)
        return
      }

      setFormError(describeError(result.error))
      return
    }

    navigate('/applications')
  }

  return (
    <section className="page-stack">
      <div>
        <p className="eyebrow">{isEdit ? 'Edit application' : 'New application'}</p>
        <h2>{isEdit ? existing?.companyName : 'Add a job application'}</h2>
      </div>

      <div className="form-card">
        {/* key forces a fresh draft when the record changes; without it, editing A then B
            shows B's data in A's stale controlled state on some update paths. */}
        <ApplicationForm
          key={existing?.id ?? 'new'}
          initialValues={initialValues}
          submitLabel={isEdit ? 'Save application' : 'Save application'}
          fieldErrors={fieldErrors}
          pending={pending}
          onSubmit={(input) => void handleSubmit(input)}
          onCancel={() => navigate('/applications')}
        />
        {formError && (
          <p className="form-error" role="alert">
            {formError}
          </p>
        )}
      </div>
    </section>
  )
}
