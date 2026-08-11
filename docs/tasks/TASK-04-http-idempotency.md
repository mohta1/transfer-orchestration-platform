# HTTP Idempotency Foundation

**Task ID:** TASK-04
**Stage:** Stage 2 — Submission & Coordination
**Recommended branch:** `feature/http-idempotency`
**Depends on:** TASK-03
**Status:** Done

---

## 1. Objective

Implement durable transfer-submission idempotency using Idempotency-Key, a canonical fingerprint, database uniqueness, and replayable result metadata.

## 2. Why This Task Exists

The challenge requires the same request to create at most one Transfer and explicitly requires an Idempotency-Key.

## 3. Scope

### In Scope
- IdempotencyRecord persistence.
- Explicit submission scope.
- Canonical deterministic request fingerprint.
- Unique scope/key database constraint.
- Processing/completed state.
- Replay metadata.
- Same-key same-payload replay.
- Same-key different-payload conflict.
- Concurrent claim behavior.

### Out of Scope
- Full transfer workflow.
- Fraud/payment.
- Outbox.
- Notification.

## 4. Required Deliverables

- IdempotencyRecord persistence.
- Explicit submission scope.
- Canonical deterministic request fingerprint.
- Unique scope/key database constraint.
- Processing/completed state.
- Replay metadata.
- Same-key same-payload replay.
- Same-key different-payload conflict.
- Concurrent claim behavior.

## 5. Implementation Requirements

- Fingerprint includes all semantically relevant submission fields.
- Use SHA-256 or equivalent deterministic hash.
- Database uniqueness is mandatory.
- Same key + same fingerprint + completed returns same logical result.
- Same key + different fingerprint results in conflict.
- Concurrent claims produce one owner.

## 6. Required Tests

- Same key/same body does not create second record.
- Same key/different body fails.
- Concurrent claims produce one owner.
- Fingerprint is deterministic.
- Completed record can replay Transfer result metadata.

## 7. Verification Procedure

1. Apply idempotency migration to PostgreSQL.
2. Run idempotency unit/integration tests.
3. Inspect the unique database constraint.

## 8. Acceptance Criteria

- [x] Durable uniqueness enforced in PostgreSQL.
- [x] Sequential and concurrent duplicate behavior is deterministic.
- [x] Different-payload conflict is proven.

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

- Unique constraint definition.
- Concurrent claim test output.
- Example canonical fingerprint.

## 11. Handoff to the Next Task

TASK-05 introduces the durable Persistent Process Manager state.
