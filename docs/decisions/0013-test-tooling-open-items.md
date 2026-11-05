# ADR-0013: Test tooling – FluentAssertions 7.x and a Docker-free SQL Server for tests

- Status: **Accepted** (user decisions 2026-11-05: stay on FluentAssertions 7.x; Docker is not available on the user's computer)
- Date: 2026-11-05
- Related: architecture §10, §10.1, §10.2; CLAUDE.md approved stack

## Context

- FluentAssertions v8 and later is commercially licensed. Version 7.x is Apache-2.0.
- Integration tests must run against real SQL Server behaviour, so the EF InMemory provider is not acceptable.
- The user **cannot run Docker** locally, so Testcontainers cannot be used.

## Decision

1. **FluentAssertions 7.x**, pinned with the version range `[7.0.0,8.0.0)` in `Directory.Packages.props`.
   - Dependabot or Renovate (if added later) must ignore major version 8.
   - The Reviewer rejects any change to 8.x.
2. **No Testcontainers, and no Docker dependency on developer machines.**
3. **Local test database:** a real SQL Server on Windows. Either:
   - **SQL Server Express LocalDB** 2019 or later, `(localdb)\MSSQLLocalDB` (the default), or
   - a local **SQL Server Developer Edition** instance.

   The connection string comes from `TestDatabase:ConnectionString`, set by environment variable or user-secrets.
4. **CI test database:** a **GitHub Actions service container**, `mcr.microsoft.com/mssql/server:2022-latest`, on `ubuntu-latest`.
   - Docker runs on GitHub's runner only.
   - The SA password is the GitHub secret `CI_SQL_SA_PASSWORD`. It is used only by this throwaway container and is never committed.
5. **Isolation:**
   - Each run creates a uniquely named database, applies migrations (`MigrateAsync`, never `EnsureCreated`) and drops the database at the end.
   - Database tests run in a single xUnit collection without parallelism.
   - Each test class resets data with an FK-ordered delete helper. No Respawn package.
6. **External services in tests:**
   - Blob Storage is replaced by an in-memory `IImageStorage` fake.
   - Email is replaced by a capturing `IEmailSender<AdminUser>`.
   - No Azure resources are needed to run `dotnet test`.
7. **Local Blob Storage for manual runs and E2E:** **Azurite started with Node** (`npx azurite`). No Docker. A real development storage account is an alternative.
8. **xUnit v3.** The EF Core InMemory provider is not used anywhere.

## Windows setup the Developer must verify

These steps are documented in `docs/dev-setup.md` (P0-05).

- `sqllocaldb versions` shows 15.x or later, and `sqllocaldb info MSSQLLocalDB` / `sqllocaldb start MSSQLLocalDB` succeed.
- The current Windows user can create and drop databases. The first connection after idle can be slow, so use `Connect Timeout=60`.
- `dotnet ef database update` succeeds on LocalDB. This covers schemas, filtered unique indexes, `rowversion` and CHECK constraints.
- No SQL Server 2022-only T-SQL is used, because LocalDB may be 2019 while CI runs 2022.
- `npx azurite` starts on ports 10000–10002, and they are not blocked by the firewall or Hyper-V reserved port ranges (`netsh interface ipv4 show excludedportrange protocol=tcp`).
- `dotnet dev-certs https --trust` and `npx playwright install chromium` succeed.

## Consequences

- Developers need LocalDB or SQL Server installed. This is a documented, Docker-free prerequisite.
- Local tests (Windows LocalDB) and CI tests (Linux SQL Server 2022) differ in version and OS. CI is the authoritative gate.
- Pinning 7.x freezes the library at 7.x features. It has no licence exposure.
