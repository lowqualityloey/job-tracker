import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useApplications } from '../state/applicationsProvider'

/**
 * Sends an unsigned browser to the login screen and tells it where it was going. BEHAVIOR-061; the redirect half of
 * DECISION-m4-auth-005.
 *
 * ## Why the signal is the variant, not the status code
 *
 * The 401 is long gone by the time a page renders — the repository adapter turned it into `{ code: 'unauthorized' }` in
 * `problemCodeContract`, which is the whole reason `-060` wanted that variant to exist. Before it, a signed-out tab was
 * rendering `corrupt-data`: "the server sent something I cannot parse", which is a *worse* lie than "you are signed out" and
 * the reason this component could not be written honestly in Slice 3.
 *
 * ## Why the destination is encoded, and why `search` is included
 *
 * `next` is user-visible text on the login screen and an input to a navigation, so it is encoded rather than concatenated.
 * `location.search` rides along because losing `?status=escalated` on the way to a sign-in is a second bug the user would
 * experience as the app forgetting what they were doing twice. `LoginPage.safeDestination` is the other half of this pair:
 * encoding proves nothing about trustworthiness, so the destination is validated where it is used.
 */
export default function RequireSession({ children }: { children: ReactNode }): JSX.Element {
  const { error } = useApplications()
  const location = useLocation()

  if (error?.code === 'unauthorized') {
    const next = encodeURIComponent(`${location.pathname}${location.search}`)
    return <Navigate to={`/login?next=${next}`} replace />
  }

  return <>{children}</>
}
