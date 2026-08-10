# Reconciliation, Unknown Outcomes, and Manual Review

**Task ID:** TASK-11
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/reconciliation`
**Depends on:** TASK-10
**Status:** Not Started

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

- [ ] All branches match timeout/reconciliation architecture.
- [ ] No duplicate financial effects.
- [ ] Retry metadata survives restart.

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

- Database timeline from timeout to final resolution.
- Provider call log proving no resubmit.

## 11. Handoff to the Next Task

TASK-12 adds auditability, manual commands, and correlation-rich observability.
