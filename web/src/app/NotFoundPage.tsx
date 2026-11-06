import { Link } from 'react-router'
import { routes } from '../shared/lib/routes'
import { Seo } from '../shared/seo/Seo'

/** Unknown client route (AC-S4). */
export function NotFoundPage() {
  return (
    <section className="page page--narrow">
      <Seo title="Page not found" noindex />
      <h1>Page not found</h1>
      <p>We could not find the page you were looking for.</p>
      <p>
        <Link to={routes.home}>Back to the home page</Link> or <Link to={routes.catalogue}>browse all perfumes</Link>.
      </p>
    </section>
  )
}
