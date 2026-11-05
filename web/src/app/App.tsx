import { useHealth } from '../api/queries/health'

/** Phase 0 placeholder page. Real routes arrive in Phase 3. */
export function App() {
  const health = useHealth()

  return (
    <main>
      <h1>UTperfume</h1>
      {health.isPending && <p role="status">Checking service...</p>}
      {health.isError && <p role="alert">The service is unavailable.</p>}
      {health.isSuccess && <p role="status">Service status: {health.data.status}</p>}
    </main>
  )
}
