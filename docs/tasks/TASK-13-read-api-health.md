# Read API, Error Semantics, and Health/Readiness

**Task ID:** TASK-13
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/read-api-health`
**Depends on:** TASK-12
**Status:** Not Started

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

- [ ] API behavior is deterministic and documented.
- [ ] Health semantics are meaningful.
- [ ] No internal model leakage.

## 9. Definition of Done

This task is **DONE only when all of the following are true**:

- [ ] Every Acceptance Criterion above is checked.
- [ ] Every Required Test exists and passes.
- [ ] `dotnet build TransferOrchestrationPlatform.sln` finishes with **0 warnings and 0 errors**.
- [ ] Existing tests have no regressions.
- [ ] Work remains inside this task's Scope.
- [ ] No locked ADR is contradicted.
- [ ] No secret or local-only artifact is committed.
- [ ] The requested Evidence is captured before merge.
- [ ] The task branch is reviewable independently.
- [ ] Any review finding is classified as Blocker, Non-blocking improvement, or Preference.

## 10. Evidence to Capture Before Moving On

- Example GET/400/404/409 JSON.
- Liveness/readiness output with DB up/down.

## 11. Handoff to the Next Task

TASK-14 adds authentication and authorization.
