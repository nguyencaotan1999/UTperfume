import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { client } from '../client'
import { ApiError, unwrap } from '../problem'
import { adminKeys } from './keys'

export const ANTIFORGERY_INVALID = 'antiforgery_invalid'

/** GET auth/antiforgery sets the token cookies. The client middleware then sends them as X-XSRF-TOKEN. */
export function fetchAntiforgery(): Promise<void> {
  return unwrap(client.GET('/api/v1/admin/auth/antiforgery')).then(() => undefined)
}

/**
 * Runs an unsafe admin call after fetching a token. On 400 antiforgery_invalid it fetches a new token and retries
 * exactly once (BR-A-04, requirements 7). A second failure is returned to the caller, so there is no loop.
 */
export async function withAntiforgery<T>(call: () => Promise<T>): Promise<T> {
  await fetchAntiforgery()
  try {
    return await call()
  } catch (error) {
    if (error instanceof ApiError && error.status === 400 && error.code === ANTIFORGERY_INVALID) {
      await fetchAntiforgery()
      return call()
    }
    throw error
  }
}

/** Best effort: tokens are bound to the user identity, so a new one is needed after sign-in and sign-out. */
async function refreshAntiforgery(): Promise<void> {
  try {
    await fetchAntiforgery()
  } catch {
    // The next unsafe call fetches a token again.
  }
}

/** The signed-in admin. A 401 ApiError means there is no session. */
export function useAdminMe() {
  return useQuery({
    queryKey: adminKeys.me(),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/admin/me', { signal })),
    retry: false,
    // Re-check the session on every mount: a cached admin must not open the shell after the session ended.
    staleTime: 0,
    refetchOnMount: 'always',
  })
}

/** Drops every admin query and all mutation state (sign-in variables hold the password) when a session ends. */
export function clearAdminSession(queryClient: QueryClient): void {
  queryClient.removeQueries({ queryKey: adminKeys.all })
  queryClient.getMutationCache().clear()
}

export function useSignIn() {
  const queryClient = useQueryClient()
  return useMutation({
    // Do not keep the credentials in the mutation cache after the form is gone.
    gcTime: 0,
    mutationFn: async (credentials: { email: string; password: string }) => {
      const me = await withAntiforgery(() =>
        unwrap(client.POST('/api/v1/admin/auth/sign-in', { body: credentials })),
      )
      await refreshAntiforgery()
      return me
    },
    onSuccess: (me) => {
      queryClient.setQueryData(adminKeys.me(), me)
    },
  })
}

/** Clearing the admin caches is the caller's job, after the admin layout has left the page (see AdminLayout). */
export function useSignOut() {
  return useMutation({
    mutationFn: async () => {
      await withAntiforgery(() => unwrap(client.POST('/api/v1/admin/auth/sign-out')))
      await refreshAntiforgery()
    },
  })
}
