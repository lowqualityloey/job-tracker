import { Link } from 'react-router-dom'
import EmptyState from '../components/EmptyState'
import StatusBadge from '../components/StatusBadge'
import { useApplications } from '../state/applicationsProvider'

export default function ApplicationsPage() {
  const { status, applications, error } = useApplications()

  if (status === 'loading') {
    return <EmptyState title="Loading applications" description="Reading your saved pipeline." />
  }

  if (status === 'error') {
    return (
      <EmptyState
        title="Your applications could not be loaded"
        description={
          error?.code === 'corrupt-data'
            ? 'The saved copy could not be read, so it was kept aside instead of being overwritten.'
            : 'Something went wrong reading your saved data.'
        }
      />
    )
  }

  if (applications.length === 0) {
    return (
      <EmptyState
        title="No job applications yet"
        description="Start by adding your first saved role or application."
        action={
          <Link className="details-link" to="/applications/new">
            Add an application
          </Link>
        }
      />
    )
  }

  return (
    <section className="page-stack">
      <div className="page-actions">
        <div>
          <p className="eyebrow">Applications</p>
          <h2>Your current pipeline</h2>
        </div>
        <Link className="button button-primary" to="/applications/new">
          New application
        </Link>
      </div>

      <div className="card-list">
        {applications.map((application) => (
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
