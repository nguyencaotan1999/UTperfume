# Developer setup (Windows, no Docker)

No step here needs Docker. Integration tests use SQL Server LocalDB, and local blob storage uses Azurite run through Node.

## Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.0.401 (see `global.json`) | `dotnet --version` |
| Node.js | **26 or later** (`.node-version` pins 26.13.0; Vite 8 and Vitest 5 need 22+) | `node -v` |
| SQL Server LocalDB | 2019 (15.x) or later | `sqllocaldb versions` |
| Git | any recent | `git --version` |

After cloning, restore the local tools once: `dotnet tool restore` (installs `dotnet-ef`).

## 1. SQL Server LocalDB

Install LocalDB through the Visual Studio workload *Data storage and processing*, or the standalone SQL Server Express installer (choose the LocalDB feature).

```powershell
sqllocaldb versions                 # expect 15.x or later
sqllocaldb info MSSQLLocalDB        # the automatic instance; created on first use
sqllocaldb start MSSQLLocalDB
```

- The first connection after idle can take more than 15 s, so test connection strings use `Connect Timeout=60`.
- LocalDB is per Windows user. A service account or CI agent does not see your instance.
- Parity: LocalDB may be newer or older than CI (SQL Server 2022 on Linux). Do not use T-SQL that only exists in one version. CI is the authoritative gate.

### Test database connection

The integration tests read the environment variable `TestDatabase__ConnectionString`. When it is not set, they use:

```
Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60
```

Each test run creates a database named `UTperfume_Test_<guid>` and drops it at the end. To use a local SQL Server Developer instance instead, set the variable to its connection string with Windows authentication. Never put a password in a file.

### Using a named SQL Server instance instead of LocalDB

LocalDB is the default. To use another instance (for example SQL Server Express), set the environment variable for the current session only, with Windows authentication and placeholders replaced by your own values:

```powershell
$env:TestDatabase__ConnectionString = "Server=<server>\<instance>;Integrated Security=true;TrustServerCertificate=true;Encrypt=true"
```

Any `Database` or `Initial Catalog` in the string is ignored; the harness connects to `master` and creates and drops its own `UTperfume_Test_<guid>` database. Never write a real server name, connection string or credential into a file.

## 2. Azurite (local Blob Storage, no Docker)

```powershell
npx azurite --location .azurite --silent --skipApiVersionCheck
```

- Ports 10000 (blob), 10001 (queue), 10002 (table). Check that they are not reserved: `netsh interface ipv4 show excludedportrange protocol=tcp`.
- `--skipApiVersionCheck` is needed because the current Azure SDK sends a newer storage API version than Azurite knows; without it every request fails with `InvalidHeaderValue`.
- `.azurite/` is git-ignored.
- In Development the API reads `appsettings.Development.json` (`UseDevelopmentStorage=true`, container `catalog-images`) and creates the container on startup. If Azurite is not running, the API logs a warning and still starts.
- `dotnet test` never needs Azurite. One round-trip test runs only when Azurite is listening, and is skipped otherwise.

## 3. HTTPS development certificate

```powershell
dotnet dev-certs https --check
dotnet dev-certs https --trust     # shows a Windows trust prompt
```

## 4. Playwright browsers

```powershell
cd web
npx playwright install chromium
```

On a corporate network with TLS inspection the download can fail with `UNABLE_TO_GET_ISSUER_CERT_LOCALLY`. Point Node at your corporate root certificate (`NODE_EXTRA_CA_CERTS=C:\path\to\corp-root.pem`) or set the proxy variables. Do not disable certificate verification.

`web/playwright.config.ts` sets `reuseExistingServer` for local runs. If an API is already listening on port 5080 (for example from `dotnet run`), Playwright reuses it, even when it does not serve the built SPA, and the smoke test fails. Stop any local API on port 5080 before running `npx playwright test`.

### Running the E2E tests

The E2E tests check real data, so they need a database. Set `ConnectionStrings__Catalog` in the environment of the session that runs Playwright, pointing at a **throwaway** database name of your own (the Development API creates it, applies the migrations and seeds the sample catalogue on first start). `playwright.config.ts` passes the variable through to the API, and `e2e/global-setup.ts` fails the run, with a clear message, when it is missing or when the seed does not appear within 120 seconds (`E2E_SEED_WAIT_SECONDS` changes that wait). Use placeholders like these and never save a real value in a file:

```powershell
$env:ConnectionStrings__Catalog = "Server=<server>\<instance>;Database=<throwaway-e2e-database>;Integrated Security=true;TrustServerCertificate=true"
npx playwright test
```

Each page test waits for its API call, asserts HTTP 200, shows seeded cards, and fails if an error message is visible, so a broken API makes the run fail. Drop the throwaway database when you no longer need it. In CI the E2E job passes `ConnectionStrings__Catalog` for the SQL Server service container (database `UTperfume_E2E`, password from the `CI_SQL_SA_PASSWORD` secret).

## 5. Run the checks

```powershell
dotnet restore
dotnet build -c Release
dotnet test  -c Release

cd web
npm ci
npm run lint
npm run build
npm run test
npx playwright test
```

`dotnet build` regenerates `openapi/v1.json`. It must not change unless the API contract changed.

## 6. Database migrations and running the app with seed data

Install the local tools once with `dotnet tool restore`.

**Always pass `--connection` explicitly to `dotnet ef database update`.** The design-time factory (`AppDbContextFactory`) falls back to the `ConnectionStrings__Catalog` environment variable, and otherwise to a placeholder LocalDB name. Without `--connection` you could update a database you did not intend to. `migrations add` and `migrations script` do not connect, so they need no connection.

```powershell
dotnet ef database update -p src/UTperfume.Infrastructure -s src/UTperfume.Api `
  --connection "Server=(localdb)\MSSQLLocalDB;Database=<your-dev-database>;Integrated Security=true;TrustServerCertificate=true"
```

To run the app in Development with the sample catalogue, set `ConnectionStrings:Catalog` for your own database. On startup the app (Development only) applies migrations and seeds about 40 products once, and skips seeding when products exist. If no connection string is set, it logs that seeding was skipped and still starts.

- Environment variable for the current session: `$env:ConnectionStrings__Catalog = "Server=<server>\<instance>;Database=<your-dev-database>;Integrated Security=true;TrustServerCertificate=true"`
- Or user-secrets (stored outside the repository): `dotnet user-secrets set "ConnectionStrings:Catalog" "<connection-string>" --project src/UTperfume.Api` (run `dotnet user-secrets init --project src/UTperfume.Api` once first).

Never commit a real server name, connection string or credential. Start Azurite (section 2) first if you want the placeholder images uploaded; otherwise only the blob paths are seeded.

## 7. Deployment settings (App Service configuration)

These are settings the app needs outside Development. Set them as App Service application settings (never in a committed file).

| Setting | Value | Notes |
|---|---|---|
| `Store__PublicBaseUrl` | The public origin, for example `https://shop.example.com` | **Required outside Development.** The app refuses to start, with a clear message, when it is missing or is not an absolute http or https URL. Only the scheme and host (and port) are used: any path is ignored, so the app cannot be served under a sub-path (ADR-0012, one same-origin App Service). The sitemap builds its absolute URLs from it. In Development it falls back to the request host. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Needed so the rate limiter sees the real client IP instead of the App Service front end. Without it every client shares one limit. **When this is set, the app must be reachable only through the App Service front end** (add access restrictions if a private endpoint or any other path to the app exists). Otherwise a client can spoof `X-Forwarded-For` and bypass the rate limit. |
| `RateLimiting__PublicPerMinute` | optional, default `120` | Public requests per client IP per minute (429 with `Retry-After` when exceeded). |
| `Sitemap__MaxUrls` | optional, default and maximum `50000` | Cap on sitemap URLs (protocol limit). |

Public responses are cached for 60 seconds on the server (output cache, keyed by path and full query string) and carry `Cache-Control: public, max-age=60`. Errors and anything under `/api/v1/admin` are `no-store`.

## Secrets

- Local secrets go in `dotnet user-secrets` (API project) or environment variables. Nothing secret is committed.
- CI uses the GitHub repository secret `CI_SQL_SA_PASSWORD` (a throwaway value) for the SQL Server service container.

## Verification record (architecture section 10.2)

Run on 2026-11-05 on a Windows development machine (user `<your-windows-user>`). Backend and LocalDB checks used .NET SDK 10.0.401. The frontend checks (lint, build, test) used Node v24.22.2; Node v20.11.1 is below the required 22.12 and cannot run Vite 8.

| # | Check | Result |
|---|---|---|
| 1 | `sqllocaldb versions` | Passed. Lists SQL Server 2019 (15.0.4153.1) and 2025 (17.0.4025.3). |
| 1 | `sqllocaldb info MSSQLLocalDB` | Passed after the instance was auto-created: version 17.0.4025.3, auto-create Yes, state Running. Before first start it reported "The automatic instance MSSQLLocalDB is not created", which is normal. |
| 2 | `sqllocaldb start MSSQLLocalDB` | Passed. |
| 3 | Create and drop databases as the current user | Passed. The integration tests create and drop `UTperfume_Test_*` databases; none were left behind. Collation `SQL_Latin1_General_CP1_CI_AS`. |
| 4 | `dotnet ef database update` on LocalDB | Passed (2026-11-06, P1-02). Migration `InitialCatalog` applied to a throwaway database on `(localdb)\MSSQLLocalDB` with `--connection`: 10 tables in schema `catalog`, 6 Concentration rows, the filtered unique primary-image index present. The database was dropped afterwards. |
| 5 | Azurite via `npx`, ports 10000-10002 | Passed. Ports free and not reserved; Azurite listened on all three. It needs `--skipApiVersionCheck` (see above). The API created the `catalog-images` container on Development startup. |
| 6 | `dotnet dev-certs https` | `--check` passed: a valid certificate exists (valid until 2026-12-17). `--trust` was **not run** because it opens a Windows prompt. |
| 7 | `npx playwright install chromium` | Passed (Chromium installed; earlier it failed with `UNABLE_TO_GET_ISSUER_CERT_LOCALLY` under TLS inspection, see section 4). `npx playwright test` passed 3 of 3 on Node v26.13.0. |
