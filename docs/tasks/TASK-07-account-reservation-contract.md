# Account Reservation Module Contract and Genuine Concurrency

**Task ID:** TASK-07
**Stage:** Stage 2 — Submission & Coordination
**Recommended branch:** `feature/account-reservation-contract`
**Depends on:** TASK-06
**Status:** Not Started

---

## 1. Objective

Connect TransferManagement to AccountBalance through an explicit module contract and perform safe reservation under real PostgreSQL optimistic concurrency.

## 2. Why This Task Exists

This is the central financial correctness proof from ADR-003.

## 3. Scope

### In Scope
- Minimal public AccountBalance module contract.
- TransferManagement adapter/client.
- Active-account and currency validation.
- Reservation execution.
- Bounded concurrency retry.
- Reload/re-evaluate after conflict.
- Transfer progression to BalanceReserved.
- Genuine concurrent integration test.

### Out of Scope
- Distributed lock.
- Cross-module DbContext access.
- Distributed transaction.
- Payment Network.

## 4. Required Deliverables

- Minimal public AccountBalance module contract.
- TransferManagement adapter/client.
- Active-account and currency validation.
- Reservation execution.
- Bounded concurrency retry.
- Reload/re-evaluate after conflict.
- Transfer progression to BalanceReserved.
- Genuine concurrent integration test.

## 5. Implementation Requirements

- TransferManagement never touches AccountBalanceDbContext.
- Retry reloads Account and re-evaluates AvailableBalance.
- Database uniqueness still protects duplicate reservation.
- No distributed lock.
- Transactions remain short and local.

## 6. Required Tests

- Starting balance 1000; concurrent 750 and 600 => only one succeeds.
- Available balance never negative.
- Duplicate same Transfer does not reserve twice.
- Inactive source rejected.
- Currency mismatch rejected.
- Successful reservation advances Transfer exactly once.

## 7. Verification Procedure

1. Run genuine concurrency test against PostgreSQL repeatedly (recommended: 10 runs).
2. Inspect final Account and Reservation rows.

## 8. Acceptance Criteria

- [ ] One winner / one business failure after re-evaluation.
- [ ] No negative balance.
- [ ] No cross-module DB access.
- [ ] Correct Transfer state progression.

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

- Repeated concurrency test output.
- Final Account/Reservation rows.
- Dependency review.

## 11. Handoff to the Next Task

TASK-08 implements Payment Network ACL and timeout/unknown semantics.
