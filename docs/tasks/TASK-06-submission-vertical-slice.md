# Submission Vertical Slice: Validation, Authorization, Daily Limit, and Fraud

**Task ID:** TASK-06
**Stage:** Stage 2 — Submission & Coordination
**Recommended branch:** `feature/submission-vertical-slice`
**Depends on:** TASK-05
**Status:** Not Started

---

## 1. Objective

Implement POST /api/transfers through validation, idempotency, Transfer creation, authorization, daily limit, fraud screening, persistence, and durable progression up to balance reservation.

## 2. Why This Task Exists

The challenge prioritizes a demonstrable vertical slice from HTTP to domain and persistence.

## 3. Scope

### In Scope
- POST /api/transfers.
- Required Idempotency-Key.
- X-Correlation-ID acceptance/generation.
- Request validation.
- Customer authorization port.
- Daily-limit capability inside TransferManagement.
- Fraud-screening port.
- Transfer state progression.
- Transfer/process persistence.
- 202 Accepted response.

### Out of Scope
- Real identity/fraud provider.
- Account reservation.
- Payment Network.
- Outbox dispatch.
- Settlement.

## 4. Required Deliverables

- POST /api/transfers.
- Required Idempotency-Key.
- X-Correlation-ID acceptance/generation.
- Request validation.
- Customer authorization port.
- Daily-limit capability inside TransferManagement.
- Fraud-screening port.
- Transfer state progression.
- Transfer/process persistence.
- 202 Accepted response.

## 5. Implementation Requirements

- API contains no business rules.
- Aggregate controls transitions.
- Daily Limit is not a separate service/aggregate.
- Fraud runs before reservation.
- Reuse TASK-04 idempotency.

## 6. Required Tests

- Successful accepted submission.
- Missing Idempotency-Key.
- Invalid amount.
- Same source/destination.
- Authorization rejected.
- Daily limit exceeded.
- Fraud rejected.
- Same key/same payload replay.
- Same key/different payload => conflict.
- CorrelationId propagated.

## 7. Verification Procedure

1. Run API against PostgreSQL.
2. Exercise success and duplicate cases with curl.
3. Inspect persisted Transfer and ProcessState.
4. Run full affected tests.

## 8. Acceptance Criteria

- [ ] Slice reaches PendingBalanceReservation.
- [ ] Duplicate semantics are correct.
- [ ] Fraud occurs before any reservation.
- [ ] Transfer/process state is durable.

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

- Example curl request/response.
- Persisted state for success.
- Persisted/replayed result for duplicate.

## 11. Handoff to the Next Task

TASK-07 introduces the AccountBalance module contract and genuine reservation concurrency.
