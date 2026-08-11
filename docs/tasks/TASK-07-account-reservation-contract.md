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

- [x] One winner / one business failure after re-evaluation.
- [x] No negative balance.
- [x] No cross-module DB access.
- [x] Correct Transfer state progression.

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

- Repeated concurrency test output.
- Final Account/Reservation rows.
- Dependency review.

### Captured TASK-07 Evidence

- The genuine PostgreSQL `1000 / 750 / 600` test was executed ten times with a
  two-party gate after both independent DbContexts loaded their first Account
  version. Every run reported `winners=1`, `businessLosers=1`, `version=1`, and
  `reservations=1`; five runs ended at available/reserved `400/600` and five at
  `250/750`. All ten invocations exited `0` with one passing test.
- The final row from run 10 was Account available/reserved `250.0000/750.0000`,
  Version `1`, with exactly one Reservation row for `750.0000`. Other runs show
  either contender may win while preserving the same invariants.
- The still-valid contention test finished at available/reserved `700/300`,
  Account Version `2`, and two distinct Reservation rows after reload/retry.
- The crash-window test committed Account available/reserved `400/100` and one
  Reservation, disposed that scope while Transfer remained
  `PendingBalanceReservation` with `ReserveBalance`, then used a new scope. The
  contract returned equivalent-reservation success and the process step persisted
  `BalanceReserved` with the process parked as `Waiting` / `None`; the Account
  remained changed once.
- Dependency inspection shows TransferManagement has one project reference to the
  AccountBalance assembly and imports only `TransferOrchestration.AccountBalance.Contracts`.
  AccountBalance Domain, Infrastructure, DbContext, EF types, and private tables
  are not referenced by TransferManagement. The contract exposes only
  `IAccountBalanceReservations`, `ReserveFundsRequest`, `ReserveFundsResult`, and
  `ReserveFundsOutcome`.
- No schema change or migration was required. Existing non-negative balance,
  positive amount, monetary precision, Account Version, and unique Transfer
  reservation constraints remain unchanged.
- Due-work discovery now captures the Process `Version`, and claiming requires
  that exact candidate version. A successful claim returns the claimed Version,
  persisted contention `AttemptCount`, and lease expiry; retry-budget decisions
  use only that claimed snapshot. PostgreSQL regressions cover stale-candidate
  rejection and many stale dispatchers attempting to bypass the durable budget.
- Lease expiry does not define the financial correctness boundary. If an ACTIVE
  reservation commits after its worker loses Process ownership, the step reloads
  Transfer and Process state with bounded optimistic-concurrency repair: completed
  progression is accepted, existing Active/ReserveBalance work is preserved, and
  Waiting/None is re-armed as due ReserveBalance work. PostgreSQL gate tests cover
  both a newer owner parking before the stale commit and a newer owner completing
  first; the existing expired-claim crash recovery remains in place.

## 11. Handoff to the Next Task

TASK-08 implements Payment Network ACL and timeout/unknown semantics.
