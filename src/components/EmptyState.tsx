import type { ReactNode } from 'react'

type EmptyStateProps = {
  title: string
  description: string
  /** Optional next step. An empty state with no way out just reports the dead end. */
  action?: ReactNode
}

export default function EmptyState({ title, description, action }: EmptyStateProps) {
  return (
    <section className="empty-state" aria-live="polite">
      <h2>{title}</h2>
      <p>{description}</p>
      {action}
    </section>
  )
}
