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
  // Passed as a function, so React calls it once for the initial value and never again.
  const [criteria, setCriteria] = useState<FilterCriteria>(defaultCriteria)
  // Above the early returns, unconditionally: a hook below a `return` changes the hook order
  // between the loading render and the ready render, which React rejects at runtime.
  const visible = useMemo(() => selectApplications(applications, criteria), [applications, criteria])
  // Derived rather than a third piece of state: a stored `filtersActive` flag beside `criteria`
  // is one more thing that can disagree with what the controls actually say.
  const filtersActive = criteria.status !== 'All' || criteria.query !== ''

  function clearFilters() {
    setCriteria(defaultCriteria())
  }

  if (status === 'loading') {
    return <EmptyState title="Loading applications" description="Reading your saved pipeline." />
  }

  if (status === 'error') {
    return (
      <EmptyState
        title="Your applications could not be loaded"
        description={
          error?.code === 'unsupported-version'
            ? 'The saved data was written by a newer version of this app, so this version left it alone.'
            : error?.code === 'corrupt-data'
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
          by a single code path instead of appearing only after the user does something. The region
          holds text only: a button inside it would be announced as part of the result, and a live
          region is an unreliable place to park a control someone has to operate. */}
      <div className="filter-summary">
        <p className="filter-count" role="status">
          Showing {visible.length} of {applications.length} applications
        </p>
        {filtersActive && visible.length > 0 && (
          <button type="button" className="clear-filters" onClick={clearFilters}>
            Clear filters
          </button>
        )}
      </div>

      {/* The zero case keeps the filter controls mounted. Replacing the whole page with an empty
          state would hide the box that caused it, leaving the user to guess at their own query.
          Only one "Clear filters" control renders at a time: this branch and the count-row one
          above are mutually exclusive, so the accessible name stays unambiguous. */}
      {visible.length === 0 ? (
        <EmptyState
          title="No applications match this filter"
          description="You still have applications saved. Change the search or the status to see them."
          action={
            <button type="button" className="button button-quiet" onClick={clearFilters}>
              Clear filters
            </button>
          }
        />
      ) : (
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
      )}
    </section>
  )
}
