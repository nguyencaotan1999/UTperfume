import type { QueryClient } from '@tanstack/react-query'
import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useNavigate } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { XSRF_HEADER_NAME } from '../../api/client'
import { ApiError } from '../../api/problem'
import { adminKeys } from '../../api/queries/keys'
import { App } from '../../app/App'
import { createQueryClient } from '../../app/queryClient'
import { jsonResponse, mockApi, problemResponse } from '../../test/fetchMock'
import { LocationProbe } from '../../test/LocationProbe'
import { renderWithProviders } from '../../test/renderWithProviders'
import { SIGN_IN_FAILED, SIGN_IN_RATE_LIMITED } from './AdminLoginPage'

const ME = { id: '1', email: 'admin@example.com', displayName: 'Alex Admin', roles: ['Catalog.Admin'] }
const ANTIFORGERY = '/api/v1/admin/auth/antiforgery'
const SIGN_IN = '/api/v1/admin/auth/sign-in'
const SIGN_OUT = '/api/v1/admin/auth/sign-out'
const ME_PATH = '/api/v1/admin/me'

const noContent = () => new Response(null, { status: 204 })

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderApp(route: string, client?: QueryClient) {
  return renderWithProviders(
    <>
      <App />
      <LocationProbe />
    </>,
    { route, client },
  )
}

function GoTo({ to }: { to: string }) {
  const navigate = useNavigate()
  return (
    <button type="button" onClick={() => navigate(to)}>
      Go to {to}
    </button>
  )
}

/** An admin query with a prefix other than `me`, as later admin pages will have. It fails with the given status. */
function failAdminQuery(client: QueryClient, status: number) {
  return act(async () => {
    await client
      .fetchQuery({
        queryKey: [...adminKeys.all, 'products', 'list'],
        queryFn: () => Promise.reject(new ApiError(status, 'Unauthorized')),
        retry: false,
      })
      .catch(() => undefined)
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => {
    resolve = r
  })
  return { promise, resolve }
}

/** No admin name, admin heading, admin outlet content or Sign out button is rendered. */
function expectNoAdminContent() {
  expect(screen.queryByText('Alex Admin')).not.toBeInTheDocument()
  expect(screen.queryByRole('heading', { level: 1, name: 'Admin' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>, password = 'secret-pass') {
  await user.type(await screen.findByLabelText('Email'), 'admin@example.com')
  await user.type(screen.getByLabelText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
}

describe('admin sign-in', () => {
  it('fetches the token, signs in and goes to the admin home', async () => {
    const user = userEvent.setup()
    const api = mockApi({
      [ANTIFORGERY]: noContent,
      [SIGN_IN]: () => ME,
      [ME_PATH]: () => ME,
    })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    expect(await screen.findByRole('heading', { level: 1, name: 'Admin' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/admin$/)
    expect(api.requests.map((r) => `${r.method} ${new URL(r.url).pathname}`)[0]).toBe(`GET ${ANTIFORGERY}`)
    expect(api.forPath(SIGN_IN)).toHaveLength(1)
    expect(JSON.stringify(localStorage) + JSON.stringify(sessionStorage)).not.toContain('secret-pass')
  })

  it('shows the same generic message for every 401 and links it to the fields (BR-A-01, AC-A7)', async () => {
    const user = userEvent.setup()
    mockApi({ [ANTIFORGERY]: noContent, [SIGN_IN]: () => problemResponse(401, { detail: 'Account is locked out' }) })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(SIGN_IN_FAILED)
    expect(alert).not.toHaveTextContent(/locked|inactive|exist/i)
    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription(SIGN_IN_FAILED)
    expect(screen.getByTestId('location')).toHaveTextContent('/admin/login')
  })

  it('keeps the email, clears the password and focuses the password field after a failed sign-in', async () => {
    const user = userEvent.setup()
    mockApi({ [ANTIFORGERY]: noContent, [SIGN_IN]: () => problemResponse(401) })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    await screen.findByRole('alert')
    expect(screen.getByLabelText('Email')).toHaveValue('admin@example.com')
    expect(screen.getByLabelText('Password')).toHaveValue('')
    expect(screen.getByLabelText('Password')).toHaveFocus()
  })

  it('sends the X-XSRF-TOKEN header from the sign-in page on the sign-in POST (BR-A-04)', async () => {
    const user = userEvent.setup()
    vi.spyOn(document, 'cookie', 'get').mockReturnValue('__Host-utp-xsrf=token-123')
    const api = mockApi({ [ANTIFORGERY]: noContent, [SIGN_IN]: () => ME, [ME_PATH]: () => ME })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    await screen.findByRole('heading', { level: 1, name: 'Admin' })
    expect(api.forPath(SIGN_IN)[0]?.headers.get(XSRF_HEADER_NAME)).toBe('token-123')
  })

  it('keeps no mutation entry with the password after a successful sign-in (L-A)', async () => {
    const user = userEvent.setup()
    const client = createQueryClient()
    mockApi({ [ANTIFORGERY]: noContent, [SIGN_IN]: () => ME, [ME_PATH]: () => ME })
    renderApp('/admin/login', client)

    await fillAndSubmit(user, 'secret-pass')
    await screen.findByText('Alex Admin')

    // gcTime 0 removes the settled sign-in mutation on the next timer tick.
    await waitFor(() =>
      expect(JSON.stringify(client.getMutationCache().getAll().map((m) => m.state.variables))).not.toContain('secret-pass'),
    )
  })

  it('shows a rate-limit message on 429', async () => {
    const user = userEvent.setup()
    mockApi({ [ANTIFORGERY]: noContent, [SIGN_IN]: () => problemResponse(429) })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    expect(await screen.findByRole('alert')).toHaveTextContent(SIGN_IN_RATE_LIMITED)
  })

  it('validates empty fields and focuses the first error without calling the API', async () => {
    const user = userEvent.setup()
    const api = mockApi({})
    renderApp('/admin/login')

    await user.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(screen.getByLabelText('Email')).toHaveFocus()
    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Enter your email.')
    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('Enter your password.')
    expect(api.requests).toHaveLength(0)
  })

  it('fetches a new token and retries sign-in exactly once on antiforgery_invalid (BR-A-04)', async () => {
    const user = userEvent.setup()
    let signInCalls = 0
    const api = mockApi({
      [ANTIFORGERY]: noContent,
      [SIGN_IN]: () => {
        signInCalls += 1
        return signInCalls === 1 ? problemResponse(400, { code: 'antiforgery_invalid' }) : jsonResponse(ME)
      },
      [ME_PATH]: () => ME,
    })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    expect(await screen.findByRole('heading', { level: 1, name: 'Admin' })).toBeInTheDocument()
    expect(api.forPath(SIGN_IN)).toHaveLength(2)
    // Before the first attempt, before the retry, and after sign-in.
    expect(api.forPath(ANTIFORGERY)).toHaveLength(3)
  })

  it('does not loop when antiforgery_invalid repeats (BR-A-04)', async () => {
    const user = userEvent.setup()
    const api = mockApi({
      [ANTIFORGERY]: noContent,
      [SIGN_IN]: () => problemResponse(400, { code: 'antiforgery_invalid' }),
    })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(api.forPath(SIGN_IN)).toHaveLength(2)
    expect(api.forPath(ANTIFORGERY)).toHaveLength(2)
  })

  it('does not retry a 400 that is not antiforgery_invalid', async () => {
    const user = userEvent.setup()
    const api = mockApi({ [ANTIFORGERY]: noContent, [SIGN_IN]: () => problemResponse(400, { code: 'validation' }) })
    renderApp('/admin/login')

    await fillAndSubmit(user)

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(api.forPath(SIGN_IN)).toHaveLength(1)
  })
})

describe('admin shell', () => {
  it('redirects to /admin/login without a session and returns to the requested route after sign-in (AC-A-41)', async () => {
    const user = userEvent.setup()
    let signedIn = false
    mockApi({
      [ME_PATH]: () => (signedIn ? ME : problemResponse(401)),
      [ANTIFORGERY]: noContent,
      [SIGN_IN]: () => {
        signedIn = true
        return ME
      },
    })
    renderApp('/admin/products')

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expect(screen.getByTestId('location')).toHaveTextContent('/admin/login')
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()

    await fillAndSubmit(user)

    await screen.findByRole('heading', { level: 1, name: 'Admin page not available' })
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/admin\/products$/)
  })

  it('redirects with the original route on a 401 from another admin query and returns after sign-in (AC-A-41)', async () => {
    const user = userEvent.setup()
    const client = createQueryClient()
    mockApi({ [ME_PATH]: () => ME, [ANTIFORGERY]: noContent, [SIGN_IN]: () => ME })
    renderApp('/admin/products', client)
    await screen.findByRole('heading', { level: 1, name: 'Admin page not available' })

    await failAdminQuery(client, 401)

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/admin\/login$/)

    await fillAndSubmit(user)

    await screen.findByRole('heading', { level: 1, name: 'Admin page not available' })
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/admin\/products$/)
  })

  it('shows the login page, not a cached shell, when /admin is opened after the session ended (M-2)', async () => {
    const user = userEvent.setup()
    const client = createQueryClient()
    let signedIn = true
    const pending = deferred<Response>()
    const api = mockApi({ [ME_PATH]: () => (signedIn ? ME : pending.promise) })
    renderWithProviders(
      <>
        <App />
        <LocationProbe />
        <GoTo to="/admin" />
      </>,
      { route: '/admin', client },
    )
    await screen.findByText('Alex Admin')
    signedIn = false

    await failAdminQuery(client, 401)
    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expect(client.getQueryCache().find({ queryKey: adminKeys.me() })).toBeUndefined()

    await user.click(screen.getByRole('button', { name: 'Go to /admin' }))

    // While /me is held pending: spinner only, no admin content.
    expect(await screen.findByText('Checking your session')).toBeInTheDocument()
    expectNoAdminContent()
    pending.resolve(problemResponse(401))

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expectNoAdminContent()
    expect(api.forPath(ME_PATH).length).toBeGreaterThanOrEqual(2)
  })

  it('renders no cached admin content while /me is refetching after the session ended elsewhere (M2-R)', async () => {
    const user = userEvent.setup()
    const client = createQueryClient()
    let mode: 'ok' | 'pending' = 'ok'
    const pending = deferred<Response>()
    mockApi({ [ME_PATH]: () => (mode === 'ok' ? ME : pending.promise), '/api/v1/brands': () => [] })
    renderWithProviders(
      <>
        <App />
        <LocationProbe />
        <GoTo to="/brands" />
        <GoTo to="/admin" />
      </>,
      { route: '/admin', client },
    )
    await screen.findByText('Alex Admin')
    await user.click(screen.getByRole('button', { name: 'Go to /brands' }))
    await screen.findByRole('heading', { level: 1, name: 'Brands' })
    // The cached session is still in the cache while the shell is unmounted.
    expect(client.getQueryData(adminKeys.me())).toEqual(ME)

    mode = 'pending'
    await user.click(screen.getByRole('button', { name: 'Go to /admin' }))

    expect(await screen.findByText('Checking your session')).toBeInTheDocument()
    expectNoAdminContent()
    pending.resolve(problemResponse(401))

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expectNoAdminContent()
  })

  it('does not render admin content without a session', async () => {
    mockApi({ [ME_PATH]: () => problemResponse(401) })
    renderApp('/admin')

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expect(screen.queryByRole('heading', { level: 1, name: 'Admin' })).not.toBeInTheDocument()
  })

  it('does not render admin content while the session check is pending', async () => {
    mockApi({ [ME_PATH]: () => new Promise<Response>(() => undefined) })
    renderApp('/admin')

    expect(await screen.findByText('Checking your session')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 1, name: 'Admin' })).not.toBeInTheDocument()
  })

  it('shows the admin name and signs out: token fetched, session view cleared, back to login', async () => {
    const user = userEvent.setup()
    let signedIn = true
    const api = mockApi({
      [ME_PATH]: () => (signedIn ? ME : problemResponse(401)),
      [ANTIFORGERY]: noContent,
      [SIGN_OUT]: () => {
        signedIn = false
        return noContent()
      },
    })
    renderApp('/admin')

    expect(await screen.findByText('Alex Admin')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expect(screen.queryByText('Alex Admin')).not.toBeInTheDocument()
    expect(api.forPath(SIGN_OUT)).toHaveLength(1)
    // Before sign-out and again after it.
    expect(api.forPath(ANTIFORGERY)).toHaveLength(2)
  })

  it('leaves no admin query and no mutation variables in the caches after sign-out (M-3)', async () => {
    const user = userEvent.setup()
    const client = createQueryClient()
    mockApi({ [ME_PATH]: () => ME, [ANTIFORGERY]: noContent, [SIGN_IN]: () => ME, [SIGN_OUT]: noContent })
    renderApp('/admin/login', client)
    await fillAndSubmit(user)
    await screen.findByText('Alex Admin')
    // An admin query with another prefix, next to the session query.
    client.setQueryData([...adminKeys.all, 'products', 'list'], [{ id: 1 }])
    expect(client.getQueryCache().getAll().length).toBeGreaterThan(1)

    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })

    expect(client.getQueryCache().getAll()).toEqual([])
    expect(client.getMutationCache().getAll()).toEqual([])
  })

  it('retries sign-out exactly once on antiforgery_invalid (BR-A-04)', async () => {
    const user = userEvent.setup()
    let calls = 0
    const api = mockApi({
      [ME_PATH]: () => ME,
      [ANTIFORGERY]: noContent,
      [SIGN_OUT]: () => {
        calls += 1
        return calls === 1 ? problemResponse(400, { code: 'antiforgery_invalid' }) : noContent()
      },
    })
    renderApp('/admin')

    await user.click(await screen.findByRole('button', { name: 'Sign out' }))

    await screen.findByRole('heading', { level: 1, name: 'Admin sign-in' })
    expect(api.forPath(SIGN_OUT)).toHaveLength(2)
  })

  it('does not loop sign-out when antiforgery_invalid repeats (BR-A-04)', async () => {
    const user = userEvent.setup()
    const api = mockApi({
      [ME_PATH]: () => ME,
      [ANTIFORGERY]: noContent,
      [SIGN_OUT]: () => problemResponse(400, { code: 'antiforgery_invalid' }),
    })
    renderApp('/admin')

    await user.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Sign-out failed. Please try again.')).toBeInTheDocument()
    expect(api.forPath(SIGN_OUT)).toHaveLength(2)
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/admin$/)
  })

  it('keeps the public pages free of admin links', async () => {
    mockApi({ '/api/v1/brands': () => [] })
    renderApp('/brands')

    await screen.findByRole('heading', { level: 1, name: 'Brands' })
    expect(document.querySelector('a[href^="/admin"]')).toBeNull()
  })
})

describe('adminKeys', () => {
  it('derives every key from adminKeys.all so one prefix clears them all (M-3)', () => {
    const keys = Object.values(adminKeys).map((k) => (typeof k === 'function' ? (k as () => readonly unknown[])() : k))
    expect(keys.length).toBeGreaterThan(1)
    for (const key of keys) expect(key.slice(0, adminKeys.all.length)).toEqual([...adminKeys.all])
  })
})
