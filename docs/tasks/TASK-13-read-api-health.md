# Read API, Error Semantics, and Health/Readiness

**Task ID:** TASK-13
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/read-api-health`
**Depends on:** TASK-12
**Status:** Done

---

## 1. Objective

Complete the external API with Transfer reads, stable error contracts, and separate liveness/readiness health semantics.

## 2. Why This Task Exists

The challenge expects POST and GET behavior and an operable service with meaningful health semantics.

## 3. Scope

### In Scope
- GET /api/transfers/{id}.
- Read DTO.
- 404 behavior.
- Stable validation/conflict/domain error mapping.
- Liveness endpoint.
- Readiness endpoint with PostgreSQL dependency.
- API contract tests.

### Out of Scope
- CQRS read database.
- Search/list UI.
- Production monitoring platform.

## 4. Required Deliverables

- GET /api/transfers/{id}.
- Read DTO.
- 404 behavior.
- Stable validation/conflict/domain error mapping.
- Liveness endpoint.
- Readiness endpoint with PostgreSQL dependency.
- API contract tests.

## 5. Implementation Requirements

- Do not expose EF/domain entities directly.
- Idempotency payload mismatch => 409.
- Unknown Transfer => 404.
- Errors are machine-readable.
- Liveness is not tied to Payment Network.
- Readiness reflects database health.

## 6. Required Tests

- GET existing Transfer.
- GET unknown => 404.
- Invalid POST => 400.
- Idempotency conflict => 409.
- Liveness behavior with DB up/down.
- Readiness healthy with DB and unhealthy without DB.

## 7. Verification Procedure

1. Run API integration tests.
2. Stop PostgreSQL temporarily to verify liveness vs readiness behavior.

## 8. Acceptance Criteria

- [x] API behavior is deterministic and documented.
- [x] Health semantics are meaningful.
- [x] No internal model leakage.

## 9. Definition of Done

This task is **DONE only when all of the following are true**:

- [x] Every Acceptance Criterion above is checked.
- [x] Every Required Test exists and passes.
- [x] `dotnet build TransferOrchestrationPlatform.sln` finishes with **0 warnings and 0 errors**.
- [x] Existing tests have no regressions.
- [x] Work remains inside this task's Scope.
- [x] No locked ADR is contradicted.
- [x] No secret or local-only artifact is committed.
- [x] The requested Evidence is captured before merge.
- [x] The task branch is reviewable independently.
- [x] Any review finding is classified as Blocker, Non-blocking improvement, or Preference.

## 10. Evidence to Capture Before Moving On

### Example GET (200)

```json
{
  "transferId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sourceAccountId": "11111111-1111-1111-1111-111111111111",
  "destinationAccountId": "22222222-2222-2222-2222-222222222222",
  "amount": 10.0,
  "currency": "GBP",
  "transferType": "DomesticInterbank",
  "state": "PendingBalanceReservation",
  "correlationId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "createdAtUtc": "2026-08-12T12:00:00+00:00",
  "updatedAtUtc": "2026-08-12T12:00:00+00:00"
}
```

### Example 400 (validation)

```json
{
  "type": "https://transfer-orchestration/errors/validation_failed",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more request fields are invalid.",
  "code": "validation_failed",
  "errors": ["Transfer amount must be greater than zero."]
}
```

### Example 404 (unknown transfer)

```json
{
  "type": "https://transfer-orchestration/errors/transfer_not_found",
  "title": "Resource not found",
  "status": 404,
  "detail": "Transfer was not found.",
  "code": "transfer_not_found"
}
```

### Example 409 (idempotency conflict)

```json
{
  "type": "https://transfer-orchestration/errors/idempotency_conflict",
  "title": "Conflict",
  "status": 409,
  "detail": "Idempotency-Key was already used with a different semantic request.",
  "code": "idempotency_conflict"
}
```

### Liveness with DB unavailable (`GET /health/live` => 200)

```json
{
  "status": "Healthy",
  "totalDuration": 0.42,
  "entries": {
    "self": {
      "status": "Healthy",
      "description": "Application process is running."
    }
  }
}
```

### Readiness with DB available (`GET /health/ready` => 200)

```json
{
  "status": "Healthy",
  "totalDuration": 12.5,
  "entries": {
    "postgresql": {
      "status": "Healthy",
      "description": "PostgreSQL is reachable."
    }
  }
}
```

### Readiness with DB unavailable (`GET /health/ready` => 503)

```json
{
  "status": "Unhealthy",
  "totalDuration": 1001.2,
  "entries": {
    "postgresql": {
      "status": "Unhealthy",
      "description": "PostgreSQL is unavailable."
    }
  }
}
```

### Verification commands (2026-08-12)

```text
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
=> 0 Warning(s), 0 Error(s)

dotnet test TransferOrchestrationPlatform.sln --no-build
=> Passed: 212 (Domain 51, Integration 158, Architecture 3), Failed: 0, Skipped: 0

dotnet test --filter FullyQualifiedName~TransferReadAndHealthApiTests
=> Passed: 8, Failed: 0
```

DB-unavailable behavior verified in `TransferReadAndHealthApiTests` using an invalid PostgreSQL connection string (liveness remains Healthy, readiness returns Unhealthy/503 without leaking connection details).

## 11. Handoff to the Next Task

TASK-14 adds authentication and authorization.
