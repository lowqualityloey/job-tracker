import { Link, useParams } from 'react-router-dom'
import EmptyState from '../components/EmptyState'
import StatusBadge from '../components/StatusBadge'
import { useApplications } from '../state/applicationsProvider'

export default function ApplicationDetailsPage() {
  const { id } = useParams()
  const { status, getApplication, error } = useApplications()

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
    </section>
  )
}
