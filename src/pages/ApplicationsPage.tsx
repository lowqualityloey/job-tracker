import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import ApplicationFilters from '../components/ApplicationFilters'
import EmptyState from '../components/EmptyState'
import StatusBadge from '../components/StatusBadge'
import { defaultCriteria, selectApplications } from '../domain/filters'
import type { FilterCriteria } from '../domain/filters'
import { useApplications } from '../state/applicationsProvider'

export default function ApplicationsPage() {
  const { status, applications, error } = useApplications()
  // View state, deliberately not provider state: criteria describe what this user is looking at,
  // not what the store holds. Putting them beside the snapshot would make a filter change look
  // like a data change, and would leak a transient choice into every other consumer (spec §3).
  const [criteria, setCriteria] = useState<FilterCriteria>(defaultCriteria)
  // Above the early returns, unconditionally: a hook below a `return` changes the hook order
  // between the loading render and the ready render, which React rejects at runtime.
  const visible = useMemo(() => selectApplications(applications, criteria), [applications, criteria])

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

      <ApplicationFilters criteria={criteria} onChange={setCriteria} />

      {/* One live region for every view, including the unfiltered one, so the count is announced
          by a single code path instead of appearing only after the user does something. */}
      <p className="filter-count" role="status">
        Showing {visible.length} of {applications.length} applications
      </p>

      <div className="card-list">
        {visible.map((application) => (
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
