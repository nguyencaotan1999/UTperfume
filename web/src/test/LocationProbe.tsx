import { useLocation } from 'react-router'

/** Renders the current path and query so tests can assert the URL state. */
export function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}
