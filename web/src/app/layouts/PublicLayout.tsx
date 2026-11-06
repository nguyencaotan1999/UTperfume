import { Link, NavLink, Outlet, useLocation } from 'react-router'
import { routes } from '../../shared/lib/routes'
import { ErrorBoundary } from '../ErrorBoundary'

export function PublicLayout() {
  const { pathname } = useLocation()

  return (
    <div className="layout">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="layout__header">
        <Link to={routes.home} className="layout__brand">
          UTperfume
        </Link>
        <nav aria-label="Main">
          <NavLink to={routes.home} end>
            Home
          </NavLink>
          <NavLink to={routes.catalogue}>Perfumes</NavLink>
          <NavLink to={routes.brands}>Brands</NavLink>
        </nav>
      </header>
      <main id="main" tabIndex={-1} className="layout__main">
        {/* A page error stays inside the page area, so the header and navigation keep working. The key resets the
            boundary when the path changes. */}
        <ErrorBoundary key={pathname}>
          <Outlet />
        </ErrorBoundary>
      </main>
      <footer className="layout__footer">
        <p>UTperfume. Prices are in Vietnamese dong (VND).</p>
      </footer>
    </div>
  )
}
