# ADR-0010: Admin authentication with ASP.NET Core Identity and a same-origin cookie

- Status: **Accepted** (user decision 2026-11-05: ASP.NET Core Identity; Entra ID is not the plan). The email provider (Q-12) and enforcing two-factor sign-in (Q-14) remain **Proposed**.
- Date: 2026-11-05
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
   - *Reason:* on the same origin, an HttpOnly cookie keeps the credential out of JavaScript. Bearer tokens would need storage in the browser, where XSS could steal them, plus refresh logic.
3. **CSRF protection:**
   - ASP.NET Core antiforgery, with the token sent in the `X-XSRF-TOKEN` header from the readable cookie `__Host-utp-xsrf`. `GET /api/v1/admin/auth/antiforgery` issues it, and it is re-issued after sign-in.
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
   - The first admin is created by the `bootstrap-admin` command from `Bootstrap:*` secrets, and only when no admin exists. Running it in production requires user approval.
   - Later admins are created and deactivated by admins (`AdminUser.IsActive`).
7. **Authorization:**
   - The `AdminOnly` policy requires the role `Catalog.Admin`.
   - The fallback policy requires an authenticated user.
   - Unchanged from ADR-0007.
8. **Password reset is in scope for Phase 4 (P4-05):**
   - A request always returns 202. The token is valid for 1 hour and is single-use, because the security stamp changes on reset.
   - Signed-in admins can change their own password.
   - **Email sender:** `IEmailSender<AdminUser>`. The Development implementation logs the link and sends nothing.
   - The production provider (Azure Communication Services Email or company SMTP) is **open (Q-12)** and will be a new dependency that needs approval. Until it is chosen, production reset is admin-assisted.
9. **Data Protection keys** (cookies, antiforgery and reset tokens) are persisted to a private Blob container before deployment (Phase 5). Without that, every restart signs all admins out and invalidates reset links.
10. **Two-factor sign-in (TOTP)** is supported by the default token providers but not enforced (Q-14). It is recommended before go-live.

Rejected alternatives:

- **Entra ID with MSAL and JWT:** not chosen by the user.
- **Identity bearer tokens through `MapIdentityApi`:** tokens would be exposed to JavaScript, and self-registration would be on by default.

## Consequences

- We own credential security: hashing, lockout, reset, and monitoring of failed sign-ins.
- New package: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. A further package for email depends on Q-12.
- New migration `AddAdminIdentity` in Phase 4.
- **Customer accounts (future)** must use a separate cookie and must never be given `Catalog.Admin`. They need their own ADR.
- Configuring production authentication (bootstrap, keys, email) requires explicit user approval.
