import EmptyState from '../components/EmptyState'
import { summarise } from '../domain/pipeline'
import { useApplications } from '../state/applicationsProvider'

export default function DashboardPage() {
  const { status, applications, error } = useApplications()

  if (status === 'loading') {
    return <EmptyState title="Loading your dashboard" description="Reading your saved applications." />
  }

  // A load that failed and a search that found nothing are different facts. Rendering the
  // cards here would report zeros, which reads as "your pipeline is empty".
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

  const { total, interviews, offers } = summarise(applications)

  return (
    <section className="page-stack">
      <div>
        <p className="eyebrow">Overview</p>
        <h2>Track your job search in one place</h2>
        <p className="muted-text">
          Every count below is derived from your saved applications, so it changes the moment you
          add or edit one.
        </p>
      </div>

      <div className="stats-grid">
        <article className="stat-card">
          <span>Total applications</span>
          <strong>{total}</strong>
        </article>
        <article className="stat-card">
          <span>Interviews</span>
          <strong>{interviews}</strong>
        </article>
        <article className="stat-card">
          <span>Offers</span>
          <strong>{offers}</strong>
        </article>
      </div>
    </section>
  )
}
