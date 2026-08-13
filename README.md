# Transfer Orchestration Platform

Backend engineering challenge implementation for a resilient domestic interbank transfer workflow.

This repository delivers a **Modular Monolith** for the new transfer capability inside an **Incremental Hybrid** modernisation context. It is a challenge submission, not a complete production banking platform.

## Project overview

The platform orchestrates domestic interbank transfers with:

- Durable HTTP idempotency on submission
- Account balance reservation as the financial concurrency boundary
- A persistent Process Manager coordinating workflow steps
- Payment Network ACL with timeout-is-not-rejection semantics
- Transactional Outbox with at-least-once delivery and idempotent consumers
- Reconciliation, manual review, and auditable operator commands
- JWT authentication and authorization boundaries

**Critical goals:** financial safety (no negative available balance, no duplicate reservation consumption), reliable messaging (no lost committed events), and safe external payment handling (no blind resubmission after ambiguous timeout).

**Current status:** TASK-01 through TASK-22 are complete on `main` (SHA `06a3379`). Challenge compliance audit passed with 0 blockers.

## Architecture

| Decision | Implementation |
| -------- | -------------- |
| System context | Incremental Hybrid — new Modular Monolith coexists with documented Legacy target |
| New capability | Single deployable `TransferOrchestration.Api` with modules under `src/Modules/` |
| Database | One PostgreSQL database; module-owned schemas and DbContexts |
| Workflow | Persistent Process Manager with durable `TransferProcessState` |
| Financial boundary | `Account` aggregate owns reservations; optimistic concurrency + DB constraints |
| External payments | Payment Network ACL; timeout → `SubmissionStatusUnknown`, not rejection |
| Messaging | Transactional Outbox; **at-least-once** delivery; idempotent consumers |
| Operations | Reconciliation, manual review, correlation/causation, auditable manual commands |
| Security | JWT validation; customer ownership concealment; operator-only manual routes |

The system does **not** claim exactly-once delivery.

### Modules

| Module | Owns |
| ------ | ---- |
| TransferManagement | Transfer workflow, Process Manager, Outbox, idempotency, reconciliation steps |
| AccountBalance | Account, balance reservations, financial concurrency |
| PaymentNetwork | External payment submission ACL |
| Notification | Idempotent notification consumer |
| AuditOperations | Manual operation audit trail |

### Documentation map

| Document | Purpose |
| -------- | ------- |
| [Architecture overview](docs/architecture.md) | System design, module boundaries, and §30 microservice review (§25) |
| [ADR-001 Architecture style](docs/adr/ADR-001-architecture-style.md) | Modular Monolith decision |
| [ADR-002 Process coordination](docs/adr/ADR-002-process-coordination.md) | Persistent Process Manager |
| [ADR-003 Reservation concurrency](docs/adr/ADR-003-reservation-concurrency.md) | Account as financial boundary |
| [ADR-004 Reliable messaging](docs/adr/ADR-004-reliable-messaging.md) | Transactional Outbox |
| [ADR-005 Legacy modernisation](docs/adr/ADR-005-legacy-modernisation.md) | Incremental Hybrid path |
| [Ubiquitous Language](docs/ubiquitous-language.md) | Domain vocabulary |
| [Event Storming summary](docs/event-storming-summary.md) | Domain events and flows |
| [Modernisation roadmap](docs/modernisation-roadmap.md) | Legacy → target evolution |
| [Runtime setup](docs/runtime-setup.md) | Detailed Docker and migration guide |
| [Engineering standards](docs/engineering-standards.md) | Coding and review rules |
| [Team engineering model](docs/team-engineering-model.md) | Ownership, DoR/DoD, and collaboration (§27) |
| [Technical debt prioritisation](docs/technical-debt-prioritisation.md) | Debt register and eight-week trade-off (§29) |
| [AI-assisted engineering](docs/ai-assisted-engineering.md) | Safe AI usage guardrails and §34 submission evidence |
| [Requirement-to-evidence matrix](docs/requirement-to-evidence.md) | Challenge compliance traceability |

### Diagrams (8 mandatory)

| Diagram | File |
| ------- | ---- |
| Context map | [docs/diagrams/context-map.drawio](docs/diagrams/context-map.drawio) |
| Target architecture | [docs/diagrams/target-architecture.drawio](docs/diagrams/target-architecture.drawio) |
| Deployment runtime | [docs/diagrams/deployment-runtime.drawio](docs/diagrams/deployment-runtime.drawio) |
| Transfer happy path | [docs/diagrams/transfer-happy-path.drawio](docs/diagrams/transfer-happy-path.drawio) |
| Transfer state diagram | [docs/diagrams/transfer-state-diagram.drawio](docs/diagrams/transfer-state-diagram.drawio) |
| Timeout reconciliation sequence | [docs/diagrams/timeout-reconciliation-sequence.drawio](docs/diagrams/timeout-reconciliation-sequence.drawio) |
| Event storming | [docs/diagrams/event-storming.drawio](docs/diagrams/event-storming.drawio) |
| Migration | [docs/diagrams/migration.drawio](docs/diagrams/migration.drawio) |

## Challenge compliance checklist

Detailed evidence lives in [docs/requirement-to-evidence.md](docs/requirement-to-evidence.md). Summary:

| Area | Status |
| ---- | ------ |
| Modular Monolith, one PostgreSQL, module schemas | Verified |
| Process Manager, state machine, reservations | Verified |
| HTTP idempotency, genuine PostgreSQL concurrency | Verified |
| Payment timeout ≠ rejection, no blind resubmission | Verified |
| Transactional Outbox, at-least-once, idempotent consumers | Verified |
| Reconciliation, manual review, audit | Verified |
| Security boundary (401/403/concealed 404) | Verified |
| ≥10 domain tests, ≥12 integration tests, architecture tests | Verified (81 / 210 / 12) |
| Five ADRs, eight diagrams | Verified |
| Docker Compose runtime, CI | Verified |
| Legacy runtime routing in production code | Partially verified (documented non-goal) |

## Prerequisites

- Git
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Docker and Docker Compose
- Bash or PowerShell for scripts

For non-Docker testing, PostgreSQL 16+ reachable from the host.

## Quick start with Docker Compose

```bash
git clone https://github.com/mohta1/transfer-orchestration-platform.git
cd transfer-orchestration-platform
cp .env.example .env
# Edit .env — set POSTGRES_PASSWORD and JWT_SIGNING_KEY (>= 32 chars). Never commit .env.

docker compose up --build -d
docker compose ps
```

Wait until `postgres` is healthy, `migrate` has exited 0, and `api` is healthy:

```bash
curl -fsS http://localhost:8080/health/live
curl -fsS http://localhost:8080/health/ready
```

Stop services:

```bash
docker compose down
```

Reset project-owned data only (destroys the named volume):

```bash
docker compose down -v
```

## Local non-Docker setup

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
```

Set PostgreSQL connection (example):

```bash
export ConnectionStrings__Database="Host=localhost;Port=5432;Database=transfer_orchestration;Username=transfer_app;Password=YOUR_PASSWORD"
export TEST_DATABASE_CONNECTION_STRING="$ConnectionStrings__Database"
```

Apply migrations:

```bash
./scripts/apply-database-migrations.sh
# Windows: ./scripts/apply-database-migrations.ps1
```

Run the API:

```bash
dotnet run --project src/TransferOrchestration.Api
```

Build and test:

```bash
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
```

## Configuration

| Variable / setting | Purpose | Secret? |
| ------------------ | ------- | ------- |
| `POSTGRES_PASSWORD` | Compose PostgreSQL password | Yes — keep in untracked `.env` |
| `JWT_SIGNING_KEY` | JWT HMAC signing key (≥ 32 chars) | Yes — keep in untracked `.env` |
| `ConnectionStrings__Database` | API and migration connection | Yes if it contains credentials |
| `Authentication__Jwt__Issuer` | Expected token issuer (`transfer-orchestration` in Compose) | No |
| `Authentication__Jwt__Audience` | Expected audience (`transfer-orchestration-api`) | No |
| `TEST_DATABASE_CONNECTION_STRING` | Integration test PostgreSQL | Yes if it contains credentials |

Files that must remain **untracked**: `.env`, `.env.*` (except `.env.example`), local secrets, build output.

Compose sets issuer/audience in `docker-compose.yml`. The API reads the signing key from `JWT_SIGNING_KEY` via environment variable substitution.

## Authentication and local demo approach

The API **validates** JWT bearer tokens but does **not** issue them. There is no login, registration, refresh-token, or identity-provider integration — this is deliberate challenge scope.

Validation requirements:

- Issuer: `transfer-orchestration` (Compose default)
- Audience: `transfer-orchestration-api`
- HMAC signature using your local `JWT_SIGNING_KEY`
- Valid lifetime (default 15 minutes for local tokens)
- **Customer** role + `account_id` claim matching the transfer source account
- **Operator** role + trusted `sub` claim for manual commands (client-supplied operator headers cannot impersonate the actor)

### Generate a local development token

Use the repository-owned helper (local development only):

```bash
export JWT_SIGNING_KEY='your-local-dev-only-32-byte-signing-key'
export JWT_ISSUER='transfer-orchestration'
export JWT_AUDIENCE='transfer-orchestration-api'

# Customer token for demo source account
CUSTOMER_TOKEN=$(dotnet run --project scripts/LocalDevToken -- --role customer --account-id 11111111-1111-1111-1111-111111111111)

# Operator token
OPERATOR_TOKEN=$(dotnet run --project scripts/LocalDevToken -- --role operator --sub demo-operator)
```

PowerShell:

```powershell
$env:JWT_SIGNING_KEY = 'your-local-dev-only-32-byte-signing-key'
$env:JWT_ISSUER = 'transfer-orchestration'
$env:JWT_AUDIENCE = 'transfer-orchestration-api'
$customerToken = dotnet run --project scripts/LocalDevToken -- --role customer --account-id 11111111-1111-1111-1111-111111111111
```

Never commit tokens or signing keys. Tokens must be generated from your untracked local signing key.

### Seed demo accounts

After Compose is running and migrations have applied:

```bash
./scripts/seed-local-demo-data.sh
# Windows PowerShell:
$env:ENV_FILE = '.env'
./scripts/seed-local-demo-data.ps1
```

Demo account IDs:

| Account | GUID | Purpose |
| ------- | ---- | ------- |
| Source | `11111111-1111-1111-1111-111111111111` | Customer demo transfers |
| Destination | `22222222-2222-2222-2222-222222222222` | Transfer destination |
| Other customer | `33333333-3333-3333-3333-333333333333` | Ownership concealment demo |

## API reference

All business endpoints require `Authorization: Bearer <token>` unless noted.

| Method | Path | Auth | Description |
| ------ | ---- | ---- | ----------- |
| POST | `/api/transfers` | Customer | Submit transfer (requires `Idempotency-Key`) |
| GET | `/api/transfers/{transferId}` | Customer | Read own transfer (cross-customer → concealed 404) |
| POST | `/api/transfers/{transferId}/manual/reject` | Operator | Reject from manual review |
| POST | `/api/transfers/{transferId}/manual/confirm-settlement` | Operator | Confirm settlement from manual review |
| GET | `/api/operations/stuck-transfers` | Operator | List transfers stuck beyond configured state-age threshold |
| GET | `/health/live` | Anonymous | Liveness |
| GET | `/health/ready` | Anonymous | Readiness (PostgreSQL) |

Optional headers: `X-Correlation-ID` (GUID), `Idempotency-Key` (manual commands).

`TransferType` values: `DomesticInterbank`, `InternalBank` (case-insensitive).

### POST /api/transfers

```bash
curl -sS -X POST http://localhost:8080/api/transfers \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-transfer-001" \
  -H "X-Correlation-ID: $(uuidgen)" \
  --data-binary @scripts/demo-transfer-payload.json
```

Expected: **202 Accepted** with body containing `transferId`, `state` (initially `PendingFraudScreening` while durable screening runs), `outcome` (`Accepted` or `Replay`). After the background worker completes fraud screening, state becomes `PendingBalanceReservation` when approved.

Errors: **400** validation/idempotency key, **401** missing/invalid auth, **403** customer not authorized for source account, **409** idempotency conflict, **422** daily limit rejection. Fraud rejection is applied asynchronously by the process worker and is visible via GET once screening completes.

### GET /api/transfers/{transferId}

```bash
curl -sS http://localhost:8080/api/transfers/{transferId} \
  -H "Authorization: Bearer $CUSTOMER_TOKEN"
```

Expected: **200 OK** for owner; **404** concealed for other customers or unknown ID; **401** without auth.

### Manual operations (operator)

```bash
curl -sS -X POST "http://localhost:8080/api/transfers/{transferId}/manual/reject" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: manual-reject-001" \
  -d '{"reason": "Demo manual rejection"}'
```

Expected: **403** for customer tokens; **200** for operator when transfer is in a valid manual-review state.

### Stuck transfers (operator)

On-demand query for non-terminal transfers whose last activity exceeds `TransferManagement:StuckTransfers:StateAgeThresholdSeconds` (default **600** seconds). Does not mutate workflow state; recovery remains via existing manual commands.

```bash
curl -sS "http://localhost:8080/api/operations/stuck-transfers?maxResults=20" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" \
  -H "X-Correlation-ID: $(uuidgen)"
```

Expected: **401** without auth; **403** for customer tokens; **200** with bounded safe projection (transfer/process metadata, age, category — no account IDs, idempotency keys, or tokens). Scheduled future process or reconciliation work is excluded until due.

Limitations: demo operational threshold only (not a production SLA); no Prometheus/Grafana stack; detection is query-based (no background alert worker).

Producing a transfer in `ManualReviewRequired` via the public API alone is not guaranteed — that state arises from reconciliation escalation in integration tests (see Timeout/reconciliation demo below).

## Demo scenarios

### Successful Transfer

1. Start Compose and wait for readiness.
2. `./scripts/seed-local-demo-data.sh`
3. Generate customer token for source account `11111111-...`.
4. POST transfer → capture `transferId` from **202** response.
5. GET transfer → **200** with matching source account.
6. Inspect workflow (optional):

```bash
docker compose exec postgres psql -U transfer_app -d transfer_orchestration -c \
  "SELECT id, state FROM transfer_management.transfers ORDER BY created_at_utc DESC LIMIT 1;"
```

Cross-customer GET with token for account `33333333-...` returns **404** (concealed).

PostgreSQL-backed proof: `TransferSubmissionApiTests.SuccessfulSubmissionPersistsOneTransferAndProcessAndPropagatesCorrelation`.

### Idempotency

```bash
# First request → Accepted
curl ... -H "Idempotency-Key: idem-demo-001" -d '{ same payload }'

# Replay same key + identical payload → Accepted, same transferId
curl ... -H "Idempotency-Key: idem-demo-001" -d '{ same payload }'

# Same key + different amount → 409 Conflict
curl ... -H "Idempotency-Key: idem-demo-001" -d '{ ..., "amount": 99.00 }'
```

PostgreSQL-backed proof: `TransferSubmissionApiTests.SameKeySamePayloadReplaysWithoutSideEffectsAndDifferentPayloadConflicts`.

### GET and ownership

| Scenario | Expected |
| -------- | -------- |
| Owner GET | 200 |
| Different customer GET | 404 concealed |
| No Authorization | 401 |
| Customer on manual route | 403 |

PostgreSQL-backed proof: `SecurityBoundaryTests.GetTransferCrossCustomerReturnsNotFound`, `TransferReadAndHealthApiTests.GetTransferOwnedByAnotherCustomerReturnsNotFound`.

### Manual-operation security

| Scenario | Expected |
| -------- | -------- |
| Customer POST manual/reject | 403 |
| Operator POST with valid state | 200 |
| Actor from JWT `sub`, not client header | Verified in tests |

PostgreSQL-backed proof: `SecurityBoundaryTests.ManualCommandOrdinaryUserForbidden`, `ManualOperationsTests.ManualRejectCreatesAuditRecordWithActorAndCorrelation`.

### Timeout and reconciliation

This scenario is most reliably demonstrated via focused integration tests (deterministic payment stub):

```bash
dotnet test tests/TransferOrchestration.IntegrationTests \
  --filter "FullyQualifiedName~PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit" \
  --no-build
```

Also run reconciliation suite:

```bash
dotnet test tests/TransferOrchestration.IntegrationTests \
  --filter "FullyQualifiedName~ReconciliationWorkflowTests" \
  --no-build
```

These prove: timeout is not rejection, no blind resubmission, same network reference on restart, reservation finalization, and manual-review escalation.

### Reliability demos (PostgreSQL integration tests)

| Scenario | Test class / method | Real PostgreSQL |
| -------- | ------------------- | --------------- |
| Outbox failure retains message | `TransactionalOutboxTests.FailedSaveCommitsNeitherCompletionNorOutboxAndPreservesMessageIdForRetry` | Yes |
| Outbox retry succeeds | `TransactionalOutboxTests.FailedSave...` (retry path in same test) | Yes |
| Duplicate consumer → one effect | `NotificationConsumerTests.DuplicateDeliveryCallsProviderOnceAndPersistsOneMarker` | Yes |
| Concurrent duplicate delivery | `NotificationConsumerTests.ConcurrentDuplicateDeliveryHasOneEffectAndOneMarker` | Yes |
| Restart recovery | `TransactionalOutboxTests.NewContextRediscoversPendingWork` | Yes |
| Poison message bounded retry | `TransactionalOutboxTests.PoisonMessageStopsAtConfiguredMaxAttempts` | Yes |
| Genuine concurrent reservations | `AccountReservationContractTests.ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser` | Yes |
| Concurrent duplicate HTTP | `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess` | Yes |

Run all focused reliability tests:

```bash
dotnet test tests/TransferOrchestration.IntegrationTests --no-build \
  --filter "FullyQualifiedName~TransactionalOutboxTests|FullyQualifiedName~NotificationConsumerTests|FullyQualifiedName~AccountReservationContractTests.ConcurrentReservations|FullyQualifiedName~TransferSubmissionApiTests.ConcurrentIdentical"
```

## Testing

### Environment

```bash
export TEST_DATABASE_CONNECTION_STRING="Host=localhost;Port=5432;Database=transfer_orchestration;Username=transfer_app;Password=YOUR_PASSWORD"
```

Integration tests use real PostgreSQL — not EF Core InMemory — for persistence, concurrency, Outbox, restart, and consumer deduplication.

### Full suite

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
```

Expected totals (counting `[Fact]`/`[Theory]` cases per project):

| Project | Tests |
| ------- | ----- |
| TransferOrchestration.Domain.Tests | 81 |
| TransferOrchestration.IntegrationTests | 210 |
| TransferOrchestration.ArchitectureTests | 12 |
| **Total** | **303** |

Counting method: CI `dotnet test` totals on branch SHA (TASK-22 PR #32 run 31686999881). Local `dotnet test --list-tests` on Windows may report 206 integration cases due to test-discovery differences; CI pass count is authoritative.

Run PostgreSQL integration suite twice in fresh processes to detect order dependence:

```bash
dotnet test tests/TransferOrchestration.IntegrationTests --no-build
dotnet test tests/TransferOrchestration.IntegrationTests --no-build
```

### Focused filters

```bash
# Domain
dotnet test tests/TransferOrchestration.Domain.Tests --no-build

# Architecture enforcement
dotnet test tests/TransferOrchestration.ArchitectureTests --no-build

# Security
dotnet test tests/TransferOrchestration.IntegrationTests --no-build \
  --filter "FullyQualifiedName~SecurityBoundaryTests"

# Health
dotnet test tests/TransferOrchestration.IntegrationTests --no-build \
  --filter "FullyQualifiedName~TransferReadAndHealthApiTests"
```

## Docker runtime verification

Repository-owned script (builds, health-checks, volume persistence, readiness failure/recovery):

```bash
./scripts/verify-compose-runtime.sh
# Windows: ./scripts/verify-compose-runtime.ps1
```

Manual checks:

```bash
docker compose config
docker compose build --no-cache
docker compose up -d
docker compose ps
curl -fsS http://localhost:8080/health/live
curl -fsS http://localhost:8080/health/ready
```

Runtime image: multi-stage build — no .NET SDK, no test binaries in the final image.

## Known limitations and non-goals

- No production identity provider or token issuance
- No complete Legacy runtime integration/routing (documented in ADR-005 and technical-debt register)
- Reconciliation orchestration lives in TransferManagement (DEBT-001)
- Challenge adapters/stubs for fraud screening and payment network in local runtime; fraud screening is durable and asynchronous (timeout/unavailability retry with bounded escalation to manual review)
- Not a full banking ledger or production SLA
- No Kafka/RabbitMQ, Kubernetes, or cloud deployment in scope
- No production secret manager
- **At-least-once** delivery — not exactly-once
- Manual-review transfers are most reliably produced via integration tests, not guaranteed through public API alone

## Clean-room validation

Reviewers can validate from a fresh clone:

```bash
git clone https://github.com/mohta1/transfer-orchestration-platform.git review-checkout
cd review-checkout
git checkout main

cp .env.example .env
# Set local-only POSTGRES_PASSWORD and JWT_SIGNING_KEY

dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore

export TEST_DATABASE_CONNECTION_STRING="..."   # real PostgreSQL required
dotnet test TransferOrchestrationPlatform.sln --no-build

docker compose up --build -d
./scripts/seed-local-demo-data.sh
# Generate token and run POST/GET demo commands above

./scripts/verify-compose-runtime.sh
docker compose down -v   # project-owned cleanup only
```

## License

Challenge implementation repository.
