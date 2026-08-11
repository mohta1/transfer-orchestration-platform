# Payment Network ACL and External Submission Semantics

**Task ID:** TASK-08
**Stage:** Stage 2 — Submission & Coordination
**Recommended branch:** `feature/payment-network-acl`
**Depends on:** TASK-07
**Status:** Not Started

---

## 1. Objective

Implement the PaymentNetwork anti-corruption layer for Domestic Interbank submission with stable references and correct accepted/rejected/timeout semantics.

## 2. Why This Task Exists

Payment timeout must not be treated as rejection and must never trigger blind duplicate submission.

## 3. Scope

### In Scope
- PaymentNetwork public contract.
- ACL mapping.
- Stable external reference.
- Domestic Interbank submission only.
- Accepted => SettlementPending.
- Definitive rejected => Rejected + release path.
- Timeout => SubmissionStatusUnknown.
- No blind resubmit.
- Status enquiry by stable reference.

### Out of Scope
- Real bank network.
- Reconciliation scheduler.
- Outbox.
- Notification.

## 4. Required Deliverables

- PaymentNetwork public contract.
- ACL mapping.
- Stable external reference.
- Domestic Interbank submission only.
- Accepted => SettlementPending.
- Definitive rejected => Rejected + release path.
- Timeout => SubmissionStatusUnknown.
- No blind resubmit.
- Status enquiry by stable reference.

## 5. Implementation Requirements

- Internal Bank never calls external network.
- Timeout never directly rejects.
- Stable reference is reused for status enquiry.
- No new submit after unknown outcome.
- Reservation stays active while unknown.

## 6. Required Tests

- Accepted => SettlementPending.
- Rejected => Rejected + release requested.
- Timeout => SubmissionStatusUnknown.
- Timeout does not release reservation.
- Timeout submit call count remains 1.
- Internal transfer bypasses PaymentNetwork.
- Status enquiry uses same stable reference.

## 7. Verification Procedure

1. Use a controllable fake provider that records calls/references.
2. Run integration tests for accepted/rejected/timeout.

## 8. Acceptance Criteria

- [ ] Unknown-outcome semantics proven.
- [ ] No duplicate external submission path.
- [ ] Domestic/Internal routing correct.

## 9. Definition of Done

This task is **DONE only when all of the following are true**:

- [ ] Every Acceptance Criterion above is checked.
- [ ] Every Required Test exists and passes.
- [x] `dotnet build TransferOrchestrationPlatform.sln` finishes with **0 warnings and 0 errors**.
- [ ] Existing tests have no regressions.
- [x] Work remains inside this task's Scope.
- [x] No locked ADR is contradicted.
- [x] No secret or local-only artifact is committed.
- [ ] The requested Evidence is captured before merge.
- [x] The task branch is reviewable independently.
- [x] Any review finding is classified as Blocker, Non-blocking improvement, or Preference.

## 10. Evidence to Capture Before Moving On

- Provider call logs for accepted/rejected/timeout.
- Stable reference proof.

Captured by `PaymentSubmissionWorkflowTests` against PostgreSQL:

- accepted, rejected, timeout-result, and thrown-timeout outcomes record exactly one Submit call;
- timeout and thrown ambiguous exceptions reload as `SubmissionStatusUnknown` with an active Reservation;
- repeated dispatch after restart-style scope recreation does not Submit again;
- the persisted `NetworkSubmissionReference` equals both the submission and status-enquiry reference;
- Internal Bank reservation completes its TASK-08 handoff without creating external submission work.

Verification of the corrective cancellation and migration-upgrade scenarios is pending a
real PostgreSQL test run. The build completes with 0 warnings and 0 errors; TASK-08 must
not return to Done until the full required PostgreSQL suite passes without skips.

## 11. Handoff to the Next Task

TASK-09 implements the mandatory Transactional Outbox.
