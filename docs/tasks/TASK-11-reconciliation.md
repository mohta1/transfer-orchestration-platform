# Reconciliation, Unknown Outcomes, and Manual Review

**Task ID:** TASK-11
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/reconciliation`
**Depends on:** TASK-10
**Status:** Done

---

## 1. Objective

Implement durable reconciliation for SubmissionStatusUnknown, including enquiry, final resolution, repeated unknown persistence, and escalation to ManualReviewRequired.

## 2. Why This Task Exists

Timeout ambiguity is a central challenge scenario and must be recovered through reconciliation rather than resubmission.

## 3. Scope

### In Scope
- ReconciliationRecord persistence.
- Due-work scheduling.
- Payment status enquiry.
- Settled => consume reservation + complete Transfer.
- Definitive rejected => reject + release reservation.
- Still unknown => persist attempt + NextAttemptAt.
- Configurable escalation threshold.
- Restart recovery.

### Out of Scope
- Hard-coded retry count in domain.
- Operator UI.
- Real payment network.

## 4. Required Deliverables

- ReconciliationRecord persistence.
- Due-work scheduling.
- Payment status enquiry.
- Settled => consume reservation + complete Transfer.
- Definitive rejected => reject + release reservation.
- Still unknown => persist attempt + NextAttemptAt.
- Configurable escalation threshold.
- Restart recovery.

## 5. Implementation Requirements

- Never resubmit payment.
- Reservation stays active while unknown.
- Settlement consumes reservation.
- Rejection releases reservation.
- Attempts are durable.
- ManualReview transition is explicit.

## 6. Required Tests

- Unknown -> settled.
- Unknown -> definitive rejected.
- Unknown -> unknown again.
- Threshold -> ManualReviewRequired.
- Restart rediscovers reconciliation work.
- Duplicate status result is idempotent.

## 7. Verification Procedure

1. Use fake status-enquiry provider with real PostgreSQL.
2. Run all outcome-branch tests.

## 8. Acceptance Criteria

- [x] All branches match timeout/reconciliation architecture.
- [x] No duplicate financial effects.
- [x] Retry metadata survives restart.

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

### Database timeline (timeout → resolution)

Verified in `ReconciliationWorkflowTests` against PostgreSQL:

1. Payment submission timeout persists `SubmissionStatusUnknown` and creates `reconciliation_records` row (`status=Active`, `attempt_count=0`, `next_attempt_at_utc=now`).
2. Status enquiry `Unknown` increments `attempt_count`, sets `last_enquiry_result`, advances `next_attempt_at_utc` deterministically (`RetryDelaySeconds * attempt`), keeps reservation `Active`.
3. Status enquiry `Settled` consumes reservation once, completes Transfer, closes reconciliation (`status=Closed`), persists Outbox atomically.
4. Status enquiry `Rejected` releases reservation once, rejects Transfer, closes reconciliation.
5. Escalation at configured threshold transitions Transfer to `ManualReviewRequired` and reconciliation to `ManualReviewRequired` without releasing reservation.

### Provider call log (no resubmit)

`RecordingGateway` in integration tests records:

- Exactly one `SubmitAsync` call per unknown transfer.
- Only `GetStatusAsync` calls during reconciliation dispatch (never a second submission).
- Duplicate settled enquiries after closure perform zero additional financial effects.

### Verification commands and results (2026-08-12)

```text
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
=> 0 warnings, 0 errors

dotnet test TransferOrchestrationPlatform.sln --no-build
=> Passed: 187 (Domain 47, Architecture 3, Integration 137), Failed: 0

dotnet test ... --filter FullyQualifiedName~ReconciliationWorkflowTests (run twice)
=> Passed: 13/13 each run
```

Migration: `20260812120000_AddReconciliationRecords` verified on PostgreSQL.

## 11. Handoff to the Next Task

TASK-12 adds auditability, manual commands, and correlation-rich observability.
