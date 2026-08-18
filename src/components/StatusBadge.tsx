import type { ApplicationStatus } from '../types/application'

type StatusBadgeProps = {
  status: ApplicationStatus
}

export default function StatusBadge({ status }: StatusBadgeProps) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>
}
