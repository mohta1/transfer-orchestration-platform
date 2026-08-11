# Submission Vertical Slice: Validation, Authorization, Daily Limit, and Fraud

**Task ID:** TASK-06
**Stage:** Stage 2 — Submission & Coordination
**Recommended branch:** `feature/submission-vertical-slice`
**Depends on:** TASK-05
**Status:** Done

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

- [x] Slice reaches PendingBalanceReservation.
- [x] Duplicate semantics are correct.
- [x] Fraud occurs before any reservation.
- [x] Transfer/process state is durable.

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

- Example curl request/response.
- Persisted state for success.
- Persisted/replayed result for duplicate.

### Captured TASK-06 Evidence

The API was run against PostgreSQL 16 using the repository's
`TEST_DATABASE_CONNECTION_STRING` convention. No connection string or secret is
stored in the repository.

Successful request (headers abbreviated):

```bash
curl -i -X POST http://localhost:5015/api/transfers \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: curl-task06' \
  -H 'X-Correlation-ID: cccccccc-cccc-cccc-cccc-cccccccccccc' \
  -d '{"sourceAccountId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","destinationAccountId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","amount":125.2500,"currency":"GBP","transferType":"DomesticInterbank"}'
```

Result: `202 Accepted`, `X-Correlation-ID` echoed, and body:

```json
{"transferId":"1b47c086-4838-4fb5-9854-87cc69b92b20","correlationId":"cccccccc-cccc-cccc-cccc-cccccccccccc","state":"PendingBalanceReservation","outcome":"Accepted"}
```

Repeating the same request and key returned `202 Accepted` with the same
Transfer/correlation identifiers and `"outcome":"Replay"`. Changing only the
amount with the same key returned `409 Conflict`. Omitting the key returned
`400 Bad Request`.

PostgreSQL inspection for the successful request showed one joined result:

```text
Transfer state:       PendingBalanceReservation
Process status/step: Active / ActionScheduled
Process next action: ReserveBalance
CorrelationId:       cccccccc-cccc-cccc-cccc-cccccccccccc
Idempotency status:  Completed
Idempotency outcome: Accepted
```

The PostgreSQL-backed concurrent HTTP test issued eight identical requests and
asserted exactly one Transfer and exactly one TransferProcessState. The complete
test run passed 44 domain tests and 28 PostgreSQL integration tests. The build
completed with 0 warnings and 0 errors.

## 11. Handoff to the Next Task

TASK-07 introduces the AccountBalance module contract and genuine reservation concurrency.
