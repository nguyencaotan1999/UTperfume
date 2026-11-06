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
| 4 | `dotnet ef database update` on LocalDB | **Not run.** There is no `DbContext` or migration yet (P1-02). It was also outside the Phase 0 instructions. A substitute test (`Engine_supports_schemas_filtered_unique_indexes_rowversion_and_check_constraints`) passed on LocalDB: schemas, filtered unique index, `rowversion` and CHECK constraints work. Repeat the real check in P1-02. |
| 5 | Azurite via `npx`, ports 10000-10002 | Passed. Ports free and not reserved; Azurite listened on all three. It needs `--skipApiVersionCheck` (see above). The API created the `catalog-images` container on Development startup. |
| 6 | `dotnet dev-certs https` | `--check` passed: a valid certificate exists (valid until 2026-12-17). `--trust` was **not run** because it opens a Windows prompt. |
| 7 | `npx playwright install chromium` | **Failed**: `UNABLE_TO_GET_ISSUER_CERT_LOCALLY` (TLS inspection on this network). See section 4. |
