import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import EmptyState from '../components/EmptyState'
import StatusBadge from '../components/StatusBadge'
import { useApplications } from '../state/applicationsProvider'

export default function ApplicationDetailsPage() {
  const { id } = useParams()
  const { status, getApplication, error, deleteApplication } = useApplications()
  const navigate = useNavigate()
  const [confirming, setConfirming] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  if (status === 'loading') {
    return <EmptyState title="Loading this application" description="Reading your saved record." />
  }

  if (status === 'error') {
    return (
      <EmptyState
        title="Your applications could not be read"
        description={
          error?.code === 'unsupported-version'
            ? 'The saved data was written by a newer version of this app, so this version left it alone.'
            : 'The saved copy could not be read. Nothing was overwritten while it stayed unreadable.'
        }
      />
    )
  }

  const application = id === undefined ? undefined : getApplication(id)

  if (!application) {
    return (
      <EmptyState
        title="Application not found"
        description="It may have been deleted, or the link is out of date."
        action={
          <Link className="details-link" to="/applications">
            ← Back to applications
          </Link>
        }
      />
    )
  }

  async function handleDelete(applicationId: string) {
    setDeleting(true)
    setDeleteError(null)
    const result = await deleteApplication(applicationId)
    setDeleting(false)

    if (result.ok) {
      navigate('/applications')
      return
    }

    setDeleteError(
      result.error.code === 'not-found'
        ? 'That application was already deleted, probably in another tab.'
        : 'The application could not be deleted. It is still here.',
    )
  }

  return (
    <section className="page-stack">
      <div className="page-actions">
        <Link className="details-link" to="/applications">
          ← Back to applications
        </Link>
        <Link className="button button-quiet" to={`/applications/${application.id}/edit`}>
          Edit
        </Link>
      </div>

      <div className="card-header">
        <div>
          <p className="eyebrow">Application details</p>
          <h2>{application.jobTitle}</h2>
          <p className="muted-text">{application.companyName}</p>
        </div>
        <StatusBadge status={application.status} />
      </div>

      <article className="application-card">
        <p>
          <strong>Location:</strong> {application.location}
        </p>
        <p>
          <strong>Applied at:</strong> {application.appliedAt ?? 'Not yet applied'}
        </p>
        <p>
          <strong>Notes:</strong> {application.notes ?? 'No notes yet.'}
        </p>
      </article>

      {/* Destructive, no undo, and storage holds the only copy — so the button that destroys
          is not the same control that asks for permission. The snapshot drops the record only
          after the store agrees, which is why a refused delete leaves it on screen. */}
      <div className="delete-zone">
        {confirming ? (
          <div className="delete-confirm" role="group" aria-label="Confirm deletion">
            <p>Delete this application? This cannot be undone.</p>
            <div className="form-actions">
              <button
                className="button button-danger"
                type="button"
                disabled={deleting}
                onClick={() => void handleDelete(application.id)}
              >
                {deleting ? 'Deleting…' : 'Delete application'}
              </button>
              <button className="button button-quiet" type="button" onClick={() => setConfirming(false)}>
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <button className="button button-danger" type="button" onClick={() => setConfirming(true)}>
            Delete
          </button>
        )}

        {deleteError && (
          <p className="form-error" role="alert">
            {deleteError}
          </p>
        )}
      </div>
    </section>
  )
}
