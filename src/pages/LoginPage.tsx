import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'

/**
 * The login screen. BEHAVIOR-061; the client half of DECISION-m4-auth-005 — the reason `unauthorized` earned its own
 * `RepositoryError` variant is that this page exists to be redirected to.
 *
 * ## Three things a 401 has to do, and where each one lives
 *
 * The decision asks for "clear the draft-free session state, redirect to login, and preserve the intended URL". Only the
 * last two belong here: the clear happens in `applicationsProvider`, because the data is *there*, and this page must not be
 * able to decide what else gets wiped. What this file owns is the form's four states and the round trip back.
 *
 * ## One message for every refusal
 *
 * A 401 and a 400 render the same sentence. `-053` spent an entire behaviour making those two byte-identical on the wire so
 * the endpoint could not be used to ask "does this address have an account?", and a UI that printed the difference would
 * reopen the oracle with better typography. The blank-field 400 is unreachable anyway — submit is disabled — so that branch
 * exists only to be refused.
 *
 * ## Why a success does a document navigation
 *
 * `onAuthenticated` defaults to `window.location.assign` rather than `useNavigate`. The provider cleared its snapshot on the
 * way here and re-reads only when the app boots, so an in-SPA redirect would land on a board that still believes it failed.
 * A full navigation is the honest reset; it is a prop so a test can watch it instead of fighting jsdom.
 */

const HOME = '/'

/**
 * Only a root-relative path survives as a destination. `next` arrives from a URL a stranger can write, and honouring
 * `https://evil.test` after someone has just typed their password is the open-redirect pattern: the redirect IS the
 * credential hand-off. `//evil.test` is the same attack wearing a leading slash — browsers read it as protocol-relative —
 * and `/\evil.test` is how that trick reaches a parser that only checks the first two characters.
 */
export function safeDestination(raw: string | null | undefined): string {
  if (!raw || !raw.startsWith('/')) {
    return HOME
  }
  if (raw.startsWith('//') || raw.startsWith('/\\')) {
    return HOME
  }
  return raw
}

function reloadAt(destination: string): void {
  window.location.assign(destination)
}

export default function LoginPage({
  onAuthenticated = reloadAt,
}: {
  onAuthenticated?: (destination: string) => void
}): JSX.Element {
  const [params] = useSearchParams()
  const destination = safeDestination(params.get('next'))
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [refused, setRefused] = useState(false)

  const canSubmit = email.trim().length > 0 && password.length > 0 && !pending

  async function submit(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (!canSubmit) {
      return
    }

    setPending(true)
    setRefused(false)

    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        // 'include' rather than the default: the session cookie is what this request exists to obtain, and AC-16's
        // exact-origin CORS with Allow-Credentials is the server half that lets a cross-origin deployment keep it.
        credentials: 'include',
        body: JSON.stringify({ email: email.trim(), password }),
      })

      if (!response.ok) {
        setRefused(true)
        setPending(false)
        return
      }

      onAuthenticated(destination)
    } catch {
      // No fetch failure is login's business beyond "it did not work": an offline user and a wrong password get the same
      // sentence here too, for the same reason -053 gives the API.
      setRefused(true)
      setPending(false)
    }
  }

  return (
    <section className="panel">
      <h1>Sign in</h1>
      <form aria-label="Sign in to Job Tracker" onSubmit={(event) => void submit(event)}>
        <p>
          Then we&apos;ll take you back to <code data-testid="destination">{destination}</code>.
        </p>

        <label htmlFor="login-email">Email address</label>
        <input
          id="login-email"
          name="email"
          type="email"
          autoComplete="username"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />

        <label htmlFor="login-password">Password</label>
        <input
          id="login-password"
          name="password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />

        {/* The button's name stays "Sign in" while a request is in flight on purpose: it is disabled by `canSubmit`, and
            the status lives in its own live region so a screen reader announces progress without the button's accessible
            name changing under a user who is still reading it. */}
        <div aria-live="polite" className="login-status">
          {pending ? 'Signing in…' : ''}
        </div>

        {refused && (
          <p role="alert">
            We don&apos;t recognise that email or password. Try again, or ask for access.
          </p>
        )}

        <button type="submit" disabled={!canSubmit}>
          Sign in
        </button>
      </form>
    </section>
  )
}
