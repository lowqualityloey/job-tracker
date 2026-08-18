import { mockApplications } from '../data/mockApplications'

const total = mockApplications.length
const interviews = mockApplications.filter((item) => item.status === 'Interview').length
const offers = mockApplications.filter((item) => item.status === 'Offer').length

export default function DashboardPage() {
  return (
    <section className="page-stack">
      <div>
        <p className="eyebrow">Overview</p>
        <h2>Track your job search in one place</h2>
        <p className="muted-text">
          This starter gives you typed data, routing, reusable UI, and testing support so you can
          focus on building features next.
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
