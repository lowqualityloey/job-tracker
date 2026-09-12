import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LoginPage from './LoginPage'
import RequireSession from '../components/RequireSession'
import { createInMemoryRepository } from '../data/localStorageApplicationRepository'
import { seedApplications } from '../data/seedApplications'
import { ApplicationsProvider, useApplications } from '../state/applicationsProvider'
import type { ApplicationRepository, Result } from '../domain/applicationRepository'
import type { JobApplication } from '../types/application'

/**
 * BEHAVIOR-061 — the login screen, and the client's reaction to being told it is signed out. Seam Unit (FE), p0.
 *
 * ## What this row actually establishes
 *
 * `-060` put `unauthorized` in a table. This is the other half of DECISION-m4-auth-005: **what a client is for, once it
 * knows**. The provider now clears its snapshot on a 401 — data that belonged to a session that just died must not stay on
 * screen — and `RequireSession` turns any such request into a visit to `/login` that **remembers where you were**, so the
 * password costs you your place in the app.
 *
 * ## Two decisions worth reading before the tests
 *
 * **One message for every refusal.** A 400 ("an email address is required") and a 401 ("those credentials are not right")
 * render the SAME text here, because the difference is exactly what `-053` spent a whole behaviour refusing to leak, and an
 * enumeration oracle is not hard to build out of a friendly UI. The blank-field case is also unreachable through this form
 * (submit is disabled), so the 400 branch exists only to be refused.
 *
 * **`?next=` is validated, not trusted.** It arrives in a URL a stranger can write. Honouring `https://evil.test` after a
 * successful password entry is an open redirect — the credential-phishing pattern — so only a root-relative path that is not
 * protocol-relative survives, and everything else falls back to `/`. That rule is asserted, not asserted-and-explained.
 *
 * ## Why a successful login does a document navigation
 *
 * `onAuthenticated` defaults to `window.location.assign`, not `useNavigate`. The provider cleared its snapshot on the way
 * here and re-reads only when the app boots; an in-SPA redirect would land you on a board that still believes it failed. A
 * full navigation is the honest reset — and it is injected here so the test can watch it instead of fighting jsdom's
 * unimplemented navigation.
 */

function renderLogin(
  entry: string,
  repository: ApplicationRepository,
  onAuthenticated = vi.fn(),
) {
  render(
    <MemoryRouter initialEntries={[entry]}>
      <ApplicationsProvider repository={repository}>
        <Routes>
          <Route path="/login" element={<LoginPage onAuthenticated={onAuthenticated} />} />
          <Route path="/applications" element={<RequireSession><h1>The board</h1></RequireSession>} />
          <Route path="/" element={<RequireSession><h1>Dashboard</h1></RequireSession>} />
        </Routes>
      </ApplicationsProvider>
    </MemoryRouter>,
  )
  return onAuthenticated
}

function repo(): ApplicationRepository {
  return createInMemoryRepository(seedApplications)
}

const fetchMock = vi.fn()

beforeEach(() => {
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
})

async function fillAndSubmit(companyName = 'acme@example.test', password = 'correct horse'): Promise<void> {
  fireEvent.change(screen.getByLabelText(/email address/i), { target: { value: companyName } })
  fireEvent.change(screen.getByLabelText(/password/i), { target: { value: password } })
  fireEvent.click(screen.getByRole('button', { name: /sign in/i }))
  await waitFor(() => expect(fetchMock).toHaveBeenCalled())
}

describe('login screen — the four states the app owes the user', () => {
  it('empty: nothing typed, nothing sent', () => {
    renderLogin('/login', repo())
    const button = screen.getByRole('button', { name: /sign in/i })

    expect(button).toBeDisabled()
    // The form must not be submittable at all: a keyboard user pressing Enter on an empty form would otherwise fire a
    // login request whose only purpose is to tell the server we have not typed anything yet.
    expect(screen.getByRole('form', { name: /sign in/i })).toBeTruthy()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('loading: the request is in flight, and a second click cannot start a second one', async () => {
    let release: (value: Response) => void = () => {}
    fetchMock.mockReturnValue(new Promise<Response>((resolve) => { release = resolve }))

    renderLogin('/login', repo())
    await fillAndSubmit()

    const button = screen.getByRole('button', { name: /sign in/i })
    expect(button).toBeDisabled()
    expect(screen.getByText(/signing in/i)).toBeTruthy()

    fireEvent.click(button)
    expect(fetchMock).toHaveBeenCalledTimes(1)

    release(new Response(JSON.stringify({ type: 'about:blank', status: 401, code: 'unauthorized' }), {
      status: 401,
      headers: { 'content-type': 'application/problem+json' },
    }))
    await waitFor(() => expect(screen.queryByText(/signing in/i)).toBeNull())
    expect(button).not.toBeDisabled()
  })

  it('error: a 401 says what happened without saying which half was wrong', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ code: 'unauthorized' }), {
      status: 401,
      headers: { 'content-type': 'application/problem+json' },
    }))

    renderLogin('/login', repo())
    await fillAndSubmit('nobody@example.test')

    expect(await screen.findByRole('alert')).toHaveTextContent(/email or password/i)
    // The typed value must not come back inside the refusal: that is the enumeration channel -053 closed on the wire,
    // and a UI that echoes it re-opens it for anyone with a screenshot.
    expect(screen.getByRole('alert').textContent).not.toContain('nobody@example.test')
    expect(screen.getByLabelText(/email address/i)).toHaveValue('nobody@example.test')
  })

  it('error: a 400 — unreachable through this form — renders the SAME message, on purpose', async () => {
    fetchMock.mockResolvedValue(new Response(
      JSON.stringify({ code: 'validation', errors: [{ pointer: '#/email', detail: 'An email address is required.' }] }),
      { status: 400, headers: { 'content-type': 'application/problem+json' } },
    ))

    renderLogin('/login', repo())
    fireEvent.change(screen.getByLabelText(/email address/i), { target: { value: 'a@b.test' } })
    fireEvent.change(screen.getByLabelText(/password/i), { target: { value: 'x' } })
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/email or password/i)
    expect(alert).not.toHaveTextContent(/required/i)
  })

  it('success: the intended URL is honoured, not the default route', async () => {
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 }))
    const onAuthenticated = renderLogin('/login?next=%2Fapplications', repo())

    await fillAndSubmit()
    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledWith('/applications'))
  })

  it('success: with no intended URL, the app is entered at the top', async () => {
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 })
    )
    const onAuthenticated = renderLogin('/login', repo())

    await fillAndSubmit()
    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledWith('/'))
  })

  it('an absolute ?next is refused — this form does not hand you to a stranger', async () => {
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 }))
    const onAuthenticated = renderLogin('/login?next=https%3A%2F%2Fevil.test%2Fsteal', repo())

    await fillAndSubmit()
    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledWith('/'))
    expect(onAuthenticated).not.toHaveBeenCalledWith('https://evil.test/steal')
  })

  it('a protocol-relative ?next is refused too — the same attack wearing a leading slash', async () => {
    // Its own test rather than a second render() in the one above: two screens left in the same document is how this file
    // first failed ("found multiple elements with the role button and name /sign in/i"), and that is the kind of red a
    // careless hand "fixes" by loosening the query — the assertion was right, the fixture was wrong.
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 }))
    const onAuthenticated = renderLogin('/login?next=%2F%2Fevil.test%2Fsteal', repo())

    await fillAndSubmit()
    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledWith('/'))
    expect(onAuthenticated).not.toHaveBeenCalledWith('//evil.test/steal')
  })

  it('the request is a login request: POST, JSON, credentials included, both fields', async () => {
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 }))
    renderLogin('/login', repo())
    await fillAndSubmit('you@example.test', 'hunter2')

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/auth/login')
    expect(init.method).toBe('POST')
    // 'include', not the default: without it the __Host- session cookie is not stored on a cross-origin deployment, and
    // AC-16's exact-origin CORS is the half that makes the browser allow it at all.
    expect(init.credentials).toBe('include')
    // `as string`, not String(...): the adapter's body is JSON.stringify's output, so it is a string by construction, and
    // `no-base-to-string` was right to refuse a cast that would have printed [object Object] if that ever stopped being true.
    expect(JSON.parse(init.body as string)).toEqual({ email: 'you@example.test', password: 'hunter2' })
  })
})

function Board({ id }: { id: string }): JSX.Element {
  const { applications, status, updateApplication } = useApplications()
  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="count">{applications.length}</span>
      {/* A real write, because that is how a tab usually learns it has been signed out: the read happened while the
          session was alive, and the next thing the user does is edit something. */}
      <button type="button" onClick={() => void updateApplication(id, { notes: 'edited after the session died' })}>
        Save a change
      </button>
    </div>
  )
}

describe('a 401 clears the session state and preserves where the user was', () => {
  it('data that belonged to the dead session stops being shown, and /login is told where to go back to', async () => {
    const seeded = seedApplications[0]
    const base = createInMemoryRepository(seedApplications)
    const repository: ApplicationRepository = {
      ...base,
      update: (): Promise<Result<JobApplication>> =>
        Promise.resolve({ ok: false, error: { code: 'unauthorized' } }),
    }

    render(
      <MemoryRouter initialEntries={[`/applications/${seeded.id}`]}>
        <ApplicationsProvider repository={repository}>
          <Routes>
            <Route path="/login" element={<LoginPage onAuthenticated={vi.fn()} />} />
            <Route
              path="/applications/:id"
              element={<RequireSession><Board id={seeded.id} /></RequireSession>}
            />
          </Routes>
        </ApplicationsProvider>
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe(String(seedApplications.length)))
    expect(screen.getByTestId('status').textContent).toBe('ready')

    fireEvent.click(screen.getByRole('button', { name: /save a change/i }))

    // DECISION-m4-auth-005's "clear the session state": the snapshot has to go with the session, because rows loaded
    // under someone's credentials sitting on a screen that is now unsigned is a leak, not a cache. Drafts are NOT
    // touched -- that is the "draft-free" in the decision's wording, and it is why the clear lives in the provider's
    // read path rather than in a global wipe.
    expect(await screen.findByRole('heading', { name: /sign in/i })).toBeTruthy()
    expect(screen.queryByTestId('count')).toBeNull()
    // The intended URL is not just carried, it is visible: "where will this take me" is answerable from the screen.
    expect(screen.getByTestId('destination').textContent).toBe(`/applications/${seeded.id}`)
    expect(screen.getByLabelText(/email address/i)).toHaveValue('')
  })
})
