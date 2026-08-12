# Runtime Setup

This document describes the reproducible local runtime for the Transfer Orchestration Platform.

## Prerequisites

- .NET 8 SDK
- Docker and Docker Compose
- Git

## Local tool restore

Restore repository-owned EF Core tooling before running migrations:

```bash
dotnet tool restore
```

The manifest lives in `.config/dotnet-tools.json` and pins `dotnet-ef` 8.0.11.

## Configuration

1. Copy `.env.example` to `.env`.
2. Replace the placeholder values with local development-only values.
3. Never commit `.env`.

Required variables:

| Variable | Used by | Purpose |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | Compose `postgres`, `migrate`, `api` | PostgreSQL password |
| `JWT_SIGNING_KEY` | Compose `api` | JWT signing key (minimum 32 characters) |

The API also receives:

- `Authentication__Jwt__Issuer=transfer-orchestration`
- `Authentication__Jwt__Audience=transfer-orchestration-api`

These are set directly in `docker-compose.yml` and do not require secrets.

## Database migration strategy

Each module owns its schema and DbContext:

- `AccountBalanceDbContext` → `account_balance`
- `TransferManagementDbContext` → `transfer_management`
- `NotificationDbContext` → `notification`
- `AuditOperationsDbContext` → `audit_operations`

Migrations are applied deterministically before the API starts.

### Docker Compose

Compose runs a one-shot `migrate` service:

1. Wait for PostgreSQL health.
2. Run `scripts/apply-database-migrations.sh` inside `Dockerfile.migrate`.
3. Apply module migrations sequentially with the restored local `dotnet-ef` tool.
4. Exit with a non-zero status if any migration fails.
5. Start the API only after `migrate` completes successfully.

The API readiness probe checks PostgreSQL reachability. It does not auto-run migrations at runtime.

### Local commands without Docker

With PostgreSQL running locally and `ConnectionStrings__Database` or `TEST_DATABASE_CONNECTION_STRING` set:

```bash
dotnet tool restore
./scripts/apply-database-migrations.sh
```

On Windows PowerShell:

```powershell
dotnet tool restore
./scripts/apply-database-migrations.ps1
```

Equivalent manual commands:

```bash
dotnet ef database update --startup-project src/TransferOrchestration.Api --project src/Modules/AccountBalance/TransferOrchestration.AccountBalance.csproj --context AccountBalanceDbContext
dotnet ef database update --startup-project src/TransferOrchestration.Api --project src/Modules/TransferManagement/TransferOrchestration.TransferManagement.csproj --context TransferManagementDbContext
dotnet ef database update --startup-project src/TransferOrchestration.Api --project src/Modules/Notification/TransferOrchestration.Notification.csproj --context NotificationDbContext
dotnet ef database update --startup-project src/TransferOrchestration.Api --project src/Modules/AuditOperations/TransferOrchestration.AuditOperations.csproj --context AuditOperationsDbContext
```

### Concurrency and failure behavior

- Compose starts one `migrate` container per `docker compose up`.
- Migrations run sequentially in a fixed module order.
- Concurrent migration execution is not supported and is out of scope for local Compose.
- If migration fails, the `migrate` service exits non-zero and the API does not start.

## Docker Compose runtime

Build and start PostgreSQL, migrations, and the API:

```bash
docker compose up --build
```

Health endpoints:

- Liveness: `GET http://localhost:8080/health/live`
- Readiness: `GET http://localhost:8080/health/ready`

Both endpoints remain anonymous. Business endpoints require JWT authentication.

Persistent PostgreSQL data is stored in the named volume `transfer_postgres_data`.

## Clean build and test gates

From a clean checkout:

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
```

PostgreSQL-dependent integration tests require `TEST_DATABASE_CONNECTION_STRING`.

## Compose verification

Run the repository-owned verification script after Compose is available:

```bash
./scripts/verify-compose-runtime.sh
```

On Windows PowerShell:

```powershell
./scripts/verify-compose-runtime.ps1
```

The script verifies service health, readiness failure/recovery, and persistent-volume behavior, then cleans up the Compose project it started.
