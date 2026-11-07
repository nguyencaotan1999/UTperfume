import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { Navigate, Outlet, useLocation, useNavigate } from 'react-router'
import { clearAdminSession, useAdminMe, useSignOut } from '../../api/queries/admin'
import { ApiError } from '../../api/problem'
import { routes } from '../../shared/lib/routes'
import { Spinner } from '../../shared/ui/Spinner'

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}

/**
 * Protected admin shell (AC-A-41, BR-A-02). It renders no admin content (name, heading, outlet) until a
 * `GET /admin/me` that started after this layout mounted has succeeded. Cached data is never enough, because the
 * session may have ended elsewhere. The admin caches are cleared only after the layout has unmounted, so its own
 * observers cannot re-create the queries.
 */
export function AdminLayout() {
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const me = useAdminMe()
  const signOut = useSignOut()
  const returnTo = location.pathname + location.search
  const sessionEnded = useRef(false)

  useEffect(
    () => () => {
      // Unmount: the observers are gone once this microtask runs, so nothing re-creates the removed queries.
      if (sessionEnded.current) queueMicrotask(() => clearAdminSession(queryClient))
    },
    [queryClient],
  )

  // A 401 from any admin query or mutation means the session ended: go to sign-in and come back afterwards.
  useEffect(() => {
    const redirect = (error: unknown) => {
      if (!isUnauthorized(error)) return
      sessionEnded.current = true
      navigate(routes.adminLogin, { replace: true, state: { from: returnTo } })
    }
    const stopQueries = queryClient.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') redirect(event.action.error)
    })
    const stopMutations = queryClient.getMutationCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') redirect(event.action.error)
    })
    return () => {
      stopQueries()
      stopMutations()
    }
  }, [queryClient, navigate, returnTo])

  if (isUnauthorized(me.error)) {
    return <Navigate to={routes.adminLogin} replace state={{ from: returnTo }} />
  }

  const ready = me.isFetchedAfterMount && !me.isError && me.data !== undefined

  const handleSignOut = () =>
    signOut.mutate(undefined, {
      onSuccess: () => {
        sessionEnded.current = true
        navigate(routes.adminLogin, { replace: true })
      },
    })

  return (
    <div className="layout">
      <header className="layout__header">
        <span className="layout__brand">UTperfume Admin</span>
        {ready && me.data && (
          <div className="toolbar">
            <span>{me.data.displayName}</span>
            <button type="button" className="button" onClick={handleSignOut} disabled={signOut.isPending}>
              Sign out
            </button>
          </div>
        )}
      </header>
      <main id="main" tabIndex={-1} className="layout__main">
        {!ready && !me.isError && <Spinner label="Checking your session" />}
        {me.isError && (
          <p role="alert" className="field__error">
            The admin area could not be loaded. Please try again.
          </p>
        )}
        {signOut.isError && (
          <p role="alert" className="field__error">
            Sign-out failed. Please try again.
          </p>
        )}
        {ready && <Outlet />}
      </main>
    </div>
  )
}
