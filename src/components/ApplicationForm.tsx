import { useState } from 'react'
import type { FormEvent } from 'react'
import { APPLICATION_STATUSES } from '../domain/applicationRepository'
import type { FieldError } from '../domain/validation'
import type { ApplicationInput, ApplicationStatus } from '../types/application'

export const EMPTY_APPLICATION: ApplicationInput = {
  companyName: '',
  jobTitle: '',
  location: '',
  status: 'Saved',
  appliedAt: '',
  notes: '',
}

interface ApplicationFormProps {
  initialValues: ApplicationInput
  submitLabel: string
  fieldErrors: FieldError[]
  pending: boolean
  onSubmit: (input: ApplicationInput) => void
  onCancel?: () => void
}

/**
 * Collects an application and hands it up. It deliberately does not decide whether the
 * input is acceptable — `domain/validation` does, and the page applies it — so the same
 * rules protect a write no matter which screen starts it.
 */
export default function ApplicationForm({
  initialValues,
  submitLabel,
  fieldErrors,
  pending,
  onSubmit,
  onCancel,
}: ApplicationFormProps) {
  const [draft, setDraft] = useState<ApplicationInput>(initialValues)

  function update<K extends keyof ApplicationInput>(key: K, value: ApplicationInput[K]) {
    setDraft((current) => ({ ...current, [key]: value }))
  }

  function errorFor(key: keyof ApplicationInput): string | undefined {
    return fieldErrors.find((error) => error.field === key)?.message
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    onSubmit({ ...draft, appliedAt: draft.appliedAt || undefined, notes: draft.notes || undefined })
  }

  return (
    <form className="application-form" onSubmit={handleSubmit} noValidate>
      <div className="field">
        <label className="field-label" htmlFor="companyName">
          Company name
        </label>
        <input
          id="companyName"
          value={draft.companyName}
          onChange={(event) => update('companyName', event.target.value)}
          aria-invalid={errorFor('companyName') ? true : undefined}
          aria-describedby={errorFor('companyName') ? 'companyName-error' : undefined}
        />
        {errorFor('companyName') && (
          <p className="field-error" id="companyName-error" role="alert">
            {errorFor('companyName')}
          </p>
        )}
      </div>

      <div className="field">
        <label className="field-label" htmlFor="jobTitle">
          Job title
        </label>
        <input
          id="jobTitle"
          value={draft.jobTitle}
          onChange={(event) => update('jobTitle', event.target.value)}
          aria-invalid={errorFor('jobTitle') ? true : undefined}
          aria-describedby={errorFor('jobTitle') ? 'jobTitle-error' : undefined}
        />
        {errorFor('jobTitle') && (
          <p className="field-error" id="jobTitle-error" role="alert">
            {errorFor('jobTitle')}
          </p>
        )}
      </div>

      <div className="field">
        <label className="field-label" htmlFor="location">
          Location
        </label>
        <input
          id="location"
          value={draft.location}
          onChange={(event) => update('location', event.target.value)}
          aria-invalid={errorFor('location') ? true : undefined}
          aria-describedby={errorFor('location') ? 'location-error' : undefined}
        />
        {errorFor('location') && (
          <p className="field-error" id="location-error" role="alert">
            {errorFor('location')}
          </p>
        )}
      </div>

      <div className="field">
        <label className="field-label" htmlFor="status">
          Status
        </label>
        <select
          id="status"
          value={draft.status}
          onChange={(event) => update('status', event.target.value as ApplicationStatus)}
        >
          {APPLICATION_STATUSES.map((status) => (
            <option key={status} value={status}>
              {status}
            </option>
          ))}
        </select>
      </div>

      <div className="field">
        <label className="field-label" htmlFor="appliedAt">
          Date applied (optional)
        </label>
        <input
          id="appliedAt"
          type="date"
          value={draft.appliedAt ?? ''}
          onChange={(event) => update('appliedAt', event.target.value)}
          aria-invalid={errorFor('appliedAt') ? true : undefined}
          aria-describedby={errorFor('appliedAt') ? 'appliedAt-error' : undefined}
        />
        {errorFor('appliedAt') && (
          <p className="field-error" id="appliedAt-error" role="alert">
            {errorFor('appliedAt')}
          </p>
        )}
      </div>

      <div className="field">
        <label className="field-label" htmlFor="notes">
          Notes (optional)
        </label>
        <textarea
          id="notes"
          rows={4}
          value={draft.notes ?? ''}
          onChange={(event) => update('notes', event.target.value)}
          aria-invalid={errorFor('notes') ? true : undefined}
          aria-describedby={errorFor('notes') ? 'notes-error' : undefined}
        />
        {errorFor('notes') && (
          <p className="field-error" id="notes-error" role="alert">
            {errorFor('notes')}
          </p>
        )}
      </div>

      <div className="form-actions">
        <button className="button button-primary" type="submit" disabled={pending}>
          {pending ? 'Saving…' : submitLabel}
        </button>
        {onCancel && (
          <button className="button button-quiet" type="button" onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}
