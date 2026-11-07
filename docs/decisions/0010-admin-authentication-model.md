# ADR-0010: Admin authentication with ASP.NET Core Identity and a same-origin cookie

- Status: **Accepted** (user decision 2026-11-05: ASP.NET Core Identity; Entra ID is not the plan). The email provider and sender (Q-12) and two-factor sign-in (Q-14: not required before go-live) were decided by the user on 2026-11-06, as was the single admin role (D-A1).
- Date: 2026-11-05 (amended 2026-11-06; amended 2026-11-07 for the user decisions D-A5 (sign-out refreshes the security stamp) and D-A6 (bootstrap approval switch) recorded in `0002` §9, and the two antiforgery cookies)
- Related: ADR-0007; architecture §6.5, §7, §9; requirements BR-29, AC-A1 to AC-A9

## Context

Only staff use the admin side, and the public side is anonymous. The SPA and API share one origin. The user chose ASP.NET Core Identity, with local accounts stored in our database. The required controls are CSRF protection, lockout, a password policy, and an email sender for password reset.

## Decision

1. **Identity setup:**
   - `AddIdentityCore<AdminUser>()` with `IdentityRole<int>`, EF stores in the `identity` schema, the sign-in manager and the default token providers.
   - **`MapIdentityApi` is not used.** It exposes self-registration and does not apply antiforgery. Small custom endpoints are used under `/api/v1/admin/auth`.
2. **Cookie session, not bearer tokens:**
   - Cookie `__Host-utp-admin`: `HttpOnly`, `Secure`, `SameSite=Strict`.
   - 30-minute sliding idle timeout, 8-hour absolute limit, no persistent "remember me".
   - Login redirects are replaced with 401 or 403 ProblemDetails.
   - The security stamp is revalidated every 5 minutes.
   - **Sign-out (D-A5, user decision 2026-11-07, option (b)):** `POST /api/v1/admin/auth/sign-out` clears the session cookie and, when the request carries a valid session, also refreshes that user's security stamp. A copy of the session cookie therefore stops working at the next revalidation, at the latest 5 minutes after sign-out (normal case; see `0002` R-A-09 for the fail-open case). Accepted side effect: every other session of the same user also ends within that interval ("sign out everywhere"). The remaining 5-minute window is risk `0002` R-A-09. Without a session, sign-out changes nothing and still returns 204 (`0002` BR-A-03). A failed stamp refresh is accepted as fail-open: the cookie is still cleared and 204 returned, a copied cookie and other sessions stay valid until the 30-minute idle or 8-hour absolute limit, and the failure is logged as event 5103 at Warning (user ID, client IP and trace ID only); operations should alert on 5103 (Phase 5, `0002` R-A-09).
   - *Reason:* on the same origin, an HttpOnly cookie keeps the credential out of JavaScript. Bearer tokens would need storage in the browser, where XSS could steal them, plus refresh logic.
3. **CSRF protection:**
   - ASP.NET Core antiforgery (double-submit pattern) with **two cookies**, both `Secure`, `SameSite=Strict` and `Path=/`:
     - `__Host-utp-antiforgery`: `HttpOnly`. It holds the antiforgery **cookie token**, which the framework compares with the request token. JavaScript never needs it, so it is kept out of reach of scripts.
     - `__Host-utp-xsrf`: **not** `HttpOnly`, so the SPA can read it. It holds the **request token**. The SPA URL-decodes the cookie value and sends it in the `X-XSRF-TOKEN` header on POST, PUT, PATCH and DELETE.
     - *Why two:* ASP.NET Core antiforgery needs a cookie token and a matching request token. The cookie token is sent by the browser automatically and must stay HttpOnly; the request token must be readable by the SPA so it can be copied into a header, which a cross-site attacker cannot do.
   - `GET /api/v1/admin/auth/antiforgery` issues both cookies (204). It is not rate-limited (`0002` §6.2, review L-2). The SPA calls it again after sign-in and after sign-out, because tokens are bound to the user identity.
   - An endpoint filter validates it on every POST, PUT, PATCH and DELETE under `/api/v1/admin`, **including sign-in**, which prevents login CSRF. Failure returns 400 `antiforgery_invalid`.
   - `SameSite=Strict` is a second layer.
4. **Lockout:**
   - 5 consecutive failures cause a 15-minute lockout, enabled for new users.
   - Sign-in uses `lockoutOnFailure: true`.
   - Unknown user, wrong password, locked account and deactivated account all return the same 401.
   - Sign-in and reset requests are rate-limited to 5 per minute per IP.
5. **Password policy:** minimum length 12, at least one digit, upper-case and lower-case letter, 4 unique characters, no symbol requirement. Hashing uses Identity's default PBKDF2.
6. **Accounts:**
   - No self-registration. The `Catalog.Admin` role is seeded with `HasData`.
   - The first admin is created by the `bootstrap-admin` command from `Bootstrap:*` secrets (`Bootstrap:AdminEmail`, `Bootstrap:AdminPassword`, optional `Bootstrap:AdminDisplayName`), and only when no admin exists. The command takes no further argument, so the password can never be passed on the command line; an extra argument is refused (exit code 2). Usage: `docs/dev-setup.md` §8.
   - **Approval gate (D-A6, user decision 2026-11-07):** outside Development the command refuses to run (exit code 1) unless `Bootstrap:AllowNonDevelopment=true` is set. Setting that switch is the **explicit user-approval gate**: it is set only after the user has approved that specific run (for production this is the approval required by the CLAUDE.md safety rules), and never as a standing setting.
   - **Removal after use:** straight after the command has run, `Bootstrap:AllowNonDevelopment` and any temporary `Bootstrap:AdminPassword` setting (App Service application setting, environment variable or user-secret) are removed.
   - Later admins are created and deactivated by admins (`AdminUser.IsActive`).
7. **Authorization:**
   - The `AdminOnly` policy requires the role `Catalog.Admin`.
   - **One Admin role for all admin functions** (D-A1, user decision 2026-11-06), including admin user management. No further roles or policies.
   - The fallback policy requires an authenticated user.
   - Unchanged from ADR-0007.
8. **Password reset is in scope for Phase 4 (P4-05 delivers the endpoints and the email abstraction; P4-05a delivers the production sender):**
   - A request always returns 202. The token is valid for 1 hour and is single-use, because the security stamp changes on reset.
   - Signed-in admins can change their own password.
   - **Email sender:** `IEmailSender<AdminUser>`. The Development implementation (`LoggingEmailSender`) sends nothing and logs only that a message was suppressed; it never logs the reset link or the token.
     - **Decided (OP-1, user 2026-11-07, option B):** reset tokens and reset links are never logged, in any environment; `0002` BR-A-31 holds without exception. Automated tests (P4-25, integration tests only since the user decision of 2026-11-07, option A) obtain the reset link through a test-only capturing email sender, never from the log. That sender is registered only in the integration test host (decision 2026-11-07), never in Development or any other environment. P4-05 delivers that sender and owns two tests (decisions 2026-11-07): the log test, that the logs of the request-and-confirm flow and of the change-password flow contain no reset token, reset link, password or password hash (applying BR-A-31); and the absence test, that a non-test host does not register the capturing sender. P4-25 reuses both. No backend channel exposes reset links to Playwright.
   - **Production provider and sender (Q-12, user decision 2026-11-06):**
     - Provider: **Gmail**, authenticated as the provider account the user named. That account address is not recorded in the project documents.
     - Sender address: the sender address the user named. It is not recorded in the project documents.
     - Both values are deployment configuration (`Email:*`), set outside the repository like the other production values.
   - **Credential handling:** the Gmail app password (or equivalent credential) is a **secret**. It is stored only in the deployment secret store (App Service settings or Key Vault) and in user-secrets for any local test send. It is never written to the repository, configuration files or documents, and never logged.
   - **Implementation library:** SMTP submission to Gmail. If the implementation needs a package beyond the .NET base library, that package is a new dependency that needs justification and approval (P4-05a).
   - **Verification before production use:** Gmail normally sends only as the authenticated account or a verified "Send mail as" alias, and an app password requires 2-Step Verification on the account. The implementer and the user must confirm that the sender address is accepted for the provider account; otherwise Gmail may reject or rewrite the sender.
   - **Risk (accepted by the user, to be reviewed before go-live):** Gmail sending limits apply, and a personal Gmail account is a non-enterprise production dependency (`0002` R-A-06).
   - Until the provider is wired (P4-05a) and its credential is configured, and as a fallback afterwards, production reset is admin-assisted.
     - **Decided (OP-2, user 2026-11-07, option A):** outside Development, before P4-05a exists, a reset request returns 202 as usual, sends no email, and logs only the fact that no email was sent (no token, no email address, no link). P4-05a remains blocked on the sender setup, unchanged.
9. **Data Protection keys** (cookies, antiforgery and reset tokens) are persisted to a private Blob container before deployment (Phase 5). Without that, every restart signs all admins out and invalidates reset links.
10. **Two-factor sign-in (TOTP)** is supported by the default token providers but not enforced. **Q-14, user decision 2026-11-06: not required before go-live.** The security risk (a single stolen password gives full admin access) is accepted and recorded in `0002` R-A-07. It can be enabled later without a schema change.

Rejected alternatives:

- **Entra ID with MSAL and JWT:** not chosen by the user.
- **Identity bearer tokens through `MapIdentityApi`:** tokens would be exposed to JavaScript, and self-registration would be on by default.

## Consequences

- We own credential security: hashing, lockout, reset, and monitoring of failed sign-ins.
- New package: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. A further package for Gmail SMTP sending (Q-12) is needed only if the base library is not used; it requires approval.
- Production password-reset email depends on a personal Gmail account and its sending limits; review before go-live.
- New migration `AddAdminIdentity` in Phase 4.
- **Customer accounts (future)** must use a separate cookie and must never be given `Catalog.Admin`. They need their own ADR.
- Configuring production authentication (bootstrap, keys, email) requires explicit user approval.
