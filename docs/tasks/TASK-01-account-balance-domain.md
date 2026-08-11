# Account Balance Domain and Reservation

**Task ID:** TASK-01
**Stage:** Stage 1 — Domain & Persistence
**Recommended branch:** `feature/account-balance-domain`
**Depends on:** Transfer domain model baseline
**Status:** Done

---

## 1. Objective

Implement the Account aggregate and BalanceReservation child entity so the core financial invariants are enforced in the domain before persistence.

## 2. Why This Task Exists

The challenge explicitly requires safe reservation, non-negative available balance, idempotent financial effects, and correct consume/release behavior.

## 3. Scope

### In Scope
- AccountId and AccountStatus.
- BalanceReservationId and BalanceReservationStatus.
- Account aggregate root.
- BalanceReservation child entity owned by Account.
- Reserve, consume, and release behavior.
- Version property for later optimistic-concurrency mapping.
- Domain tests and InternalsVisibleTo for tests.

### Out of Scope
- EF Core mappings and migrations.
- Repositories.
- Cross-module contracts.
- API endpoints.
- Real concurrency tests.

## 4. Required Deliverables

- AccountId and AccountStatus.
- BalanceReservationId and BalanceReservationStatus.
- Account aggregate root.
- BalanceReservation child entity owned by Account.
- Reserve, consume, and release behavior.
- Version property for later optimistic-concurrency mapping.
- Domain tests and InternalsVisibleTo for tests.

## 5. Implementation Requirements

- AvailableBalance and ReservedBalance never become negative.
- Only active accounts can reserve.
- Reservation amount must be positive.
- One reservation per Transfer; duplicate same amount is idempotent; different amount fails.
- Consume and release are idempotent.
- Consumed reservation cannot be released; released reservation cannot be consumed.
- BalanceReservation is not an aggregate root.

## 6. Required Tests

- Reserve moves funds from available to reserved.
- Insufficient balance fails without changing balances.
- Inactive account fails.
- Duplicate same reservation does not reserve twice.
- Duplicate reservation with different amount fails.
- Consume applies financial effect exactly once.
- Release applies financial effect exactly once.
- Consumed cannot be released; released cannot be consumed.
- Negative opening balance fails.

## 7. Verification Procedure

1. dotnet build TransferOrchestrationPlatform.sln
2. dotnet test tests/TransferOrchestration.Domain.Tests/TransferOrchestration.Domain.Tests.csproj --no-build

## 8. Acceptance Criteria

- [ ] Build has 0 warnings and 0 errors.
- [ ] All Account domain tests pass.
- [ ] Existing Transfer tests remain green.
- [ ] No caller can directly mutate financial state.

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

- Final domain test count.
- Build summary.
- Test names proving duplicate reserve/consume/release behavior.
- git diff --stat main...HEAD

## 11. Handoff to the Next Task

TASK-02 maps Transfer and Account to PostgreSQL and adds database-level safeguards.
