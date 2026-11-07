import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useSignIn } from '../../api/queries/admin'
import { ApiError } from '../../api/problem'
import { safeAdminReturnPath } from '../../shared/lib/routes'

interface FieldErrors {
  email?: string
  password?: string
}

/** The same text for every 401, so the page never reveals whether the account exists, is locked out or is inactive. */
export const SIGN_IN_FAILED = 'Sign-in failed. Check your email and password and try again.'
export const SIGN_IN_RATE_LIMITED = 'Too many sign-in attempts. Wait a minute and try again.'
export const SIGN_IN_UNAVAILABLE = 'Sign-in is not available right now. Please try again.'

function formMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return SIGN_IN_FAILED
    if (error.status === 429) return SIGN_IN_RATE_LIMITED
  }
  return SIGN_IN_UNAVAILABLE
}

export function AdminLoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const signIn = useSignIn()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)

  const returnTo = safeAdminReturnPath((location.state as { from?: unknown } | null)?.from)
  const formError = signIn.isError ? formMessage(signIn.error) : undefined

  // Focus the first invalid field.
  useEffect(() => {
    if (fieldErrors.email) emailRef.current?.focus()
    else if (fieldErrors.password) passwordRef.current?.focus()
  }, [fieldErrors])

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const errors: FieldErrors = {}
    if (!email.trim()) errors.email = 'Enter your email.'
    if (!password) errors.password = 'Enter your password.'
    setFieldErrors(errors)
    if (errors.email || errors.password) return
    signIn.mutate(
      { email: email.trim(), password },
      {
        onSuccess: () => navigate(returnTo, { replace: true }),
        // Keep the email, drop the password and put the cursor where the user retypes it. The alert is announced by its role.
        onError: () => {
          setPassword('')
          passwordRef.current?.focus()
        },
      },
    )
  }

  return (
    <main id="main" className="layout__main">
      <h1>Admin sign-in</h1>
      <form onSubmit={handleSubmit} noValidate aria-label="Sign in">
        {formError && (
          <p id="signin-error" role="alert" className="field__error">
            {formError}
          </p>
        )}
        <div className="field">
          <label htmlFor="signin-email">Email</label>
          <input
            ref={emailRef}
            id="signin-email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={fieldErrors.email ? true : undefined}
            aria-describedby={fieldErrors.email ? 'signin-email-error' : formError ? 'signin-error' : undefined}
          />
          {fieldErrors.email && (
            <p id="signin-email-error" className="field__error">
              {fieldErrors.email}
            </p>
          )}
        </div>
        <div className="field">
          <label htmlFor="signin-password">Password</label>
          <input
            ref={passwordRef}
            id="signin-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={fieldErrors.password ? true : undefined}
            aria-describedby={fieldErrors.password ? 'signin-password-error' : formError ? 'signin-error' : undefined}
          />
          {fieldErrors.password && (
            <p id="signin-password-error" className="field__error">
              {fieldErrors.password}
            </p>
          )}
        </div>
        <button type="submit" className="button" disabled={signIn.isPending}>
          {signIn.isPending ? 'Signing in' : 'Sign in'}
        </button>
      </form>
    </main>
  )
}
