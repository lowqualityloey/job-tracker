import { Link } from 'react-router-dom'
import EmptyState from '../components/EmptyState'
import StatusBadge from '../components/StatusBadge'
import { mockApplications } from '../data/mockApplications'

export default function ApplicationsPage() {
  if (mockApplications.length === 0) {
    return (
      <EmptyState
        title="No job applications yet"
        description="Start by adding your first saved role or application."
      />
    )
  }

  return (
    <section className="page-stack">
      <div>
        <p className="eyebrow">Applications</p>
        <h2>Your current pipeline</h2>
      </div>

      <div className="card-list">
        {mockApplications.map((application) => (
          <article key={application.id} className="application-card">
            <div className="card-header">
              <div>
                <h3>{application.jobTitle}</h3>
                <p className="muted-text">{application.companyName}</p>
              </div>
              <StatusBadge status={application.status} />
            </div>
            <p>{application.location}</p>
            <p className="muted-text">Applied: {application.appliedAt ?? 'Not yet applied'}</p>
            <Link className="details-link" to={`/applications/${application.id}`}>
              View details
            </Link>
          </article>
        ))}
      </div>
    </section>
  )
}
