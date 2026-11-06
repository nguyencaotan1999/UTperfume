import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'
import { applyChange, parseParams, toSearchParams, type CatalogueParams } from './catalogueParams'

/**
 * The URL query string is the single source of catalogue state (AC-S2). Reading derives the state from it; every
 * change navigates to a new URL, so reload, share and browser back all restore the same view.
 */
export function useCatalogueParams() {
  const [searchParams, setSearchParams] = useSearchParams()
  const params = useMemo(() => parseParams(searchParams), [searchParams])

  const update = useCallback(
    (patch: Partial<CatalogueParams>, options?: { keepPage?: boolean }) => {
      setSearchParams((previous) => toSearchParams(applyChange(parseParams(previous), patch, options)))
    },
    [setSearchParams],
  )

  const clear = useCallback(() => setSearchParams(new URLSearchParams()), [setSearchParams])

  return { params, update, clear }
}
