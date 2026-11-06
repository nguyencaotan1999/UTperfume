import { ErrorBoundary } from './ErrorBoundary'
import { AppRoutes } from './router'

export function App() {
  return (
    <ErrorBoundary>
      <AppRoutes />
    </ErrorBoundary>
  )
}
