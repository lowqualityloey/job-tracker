import { Link, useParams } from 'react-router-dom'
import StatusBadge from '../components/StatusBadge'
import { mockApplications } from '../data/mockApplications'

export default function ApplicationDetailsPage() {
  const { id } = useParams()
  const application = mockApplications.find((item) => item.id === Number(id))

  if (!application) {
    return (
      <section className="page-stack">
        <h2>Application not found</h2>
        <Link className="details-link" to="/applications">
          Back to applications
        </Link>
      </section>
    )
  }

  return (
    <section className="page-stack">
      <Link className="details-link" to="/applications">
        ← Back to applications
      </Link>
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
