/** RFC 9457 problem body as sent by the API (ADR-0011). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

/** Error thrown for every non-2xx API response, so TanStack Query's `error` is always typed. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  readonly traceId: string | undefined
  /** Field errors from a 400 ValidationProblemDetails, keyed by camelCase field name. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>

  constructor(
    status: number,
    message: string,
    options: { code?: string; traceId?: string; fieldErrors?: Record<string, string[]> } = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = options.code
    this.traceId = options.traceId
    this.fieldErrors = options.fieldErrors ?? {}
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readFieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) return {}
  const result: Record<string, string[]> = {}
  for (const [key, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) result[key] = messages.filter((m): m is string => typeof m === 'string')
  }
  return result
}

/** Maps an error body (ProblemDetails or anything else) and the HTTP status to an ApiError. */
export function toApiError(body: unknown, status: number): ApiError {
  if (!isRecord(body)) {
    return new ApiError(status, `Request failed with status ${status}`)
  }
  const title = typeof body.title === 'string' ? body.title : undefined
  const detail = typeof body.detail === 'string' ? body.detail : undefined
  return new ApiError(status, detail ?? title ?? `Request failed with status ${status}`, {
    code: typeof body.code === 'string' ? body.code : undefined,
    traceId: typeof body.traceId === 'string' ? body.traceId : undefined,
    fieldErrors: readFieldErrors(body.errors),
  })
}

interface FetchResult<T> {
  data?: T
  error?: unknown
  response: Response
}

/** Returns `data` or throws an ApiError built from the ProblemDetails. Network failures propagate as-is. */
export async function unwrap<T>(result: Promise<FetchResult<T>>): Promise<T> {
  const { data, error, response } = await result
  if (!response.ok) throw toApiError(error, response.status)
  return data as T
}
