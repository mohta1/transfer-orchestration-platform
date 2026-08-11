# Persistence Repositories and Optimistic Concurrency Handling

**Task ID:** TASK-03
**Stage:** Stage 1 — Domain & Persistence
**Recommended branch:** `feature/persistence-repositories`
**Depends on:** TASK-02
**Status:** Done

---

## 1. Objective

Create module-local persistence abstractions and implementations, and make optimistic concurrency conflicts explicit rather than silently overwriting financial state.

## 2. Why This Task Exists

A Version column alone does not satisfy the concurrency requirement. The loser of a race must be detected and business invariants re-evaluated.

## 3. Scope

### In Scope
- Transfer repository abstraction/implementation.
- Account repository abstraction/implementation.
- Async operations with CancellationToken.
- Explicit application-facing concurrency conflict outcome/exception.
- Short local transaction semantics.
- Real PostgreSQL stale-write integration tests.

### Out of Scope
- Cross-module Account contract.
- HTTP idempotency.
- External calls.
- Process Manager.
- Outbox.

## 4. Required Deliverables

- Transfer repository abstraction/implementation.
- Account repository abstraction/implementation.
- Async operations with CancellationToken.
- Explicit application-facing concurrency conflict outcome/exception.
- Short local transaction semantics.
- Real PostgreSQL stale-write integration tests.

## 5. Implementation Requirements

- DbContext never leaks outside Infrastructure.
- No generic repository.
- No global shared UnitOfWork.
- A concurrency conflict is never converted into success.
- Retry logic must reload the Account and re-evaluate invariants.

## 6. Required Tests

- Load/persist Transfer.
- Load/persist Account with reservations.
- Two contexts load same Account version; first save succeeds, second conflicts.
- Reload after conflict returns winner state.
- No last-write-wins financial overwrite.

## 7. Verification Procedure

1. dotnet build TransferOrchestrationPlatform.sln
2. Run targeted persistence/concurrency integration tests against real PostgreSQL.

## 8. Acceptance Criteria

- [x] Repository consumers do not reference EF types.
- [x] Genuine optimistic concurrency conflict reproduced.
- [x] Winner state preserved.
- [x] Loser receives explicit conflict outcome.
- [x] 0 warnings / 0 errors.

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

- Concurrency test output.
- Before/after Account versions and balances.
- Repository dependency review.

## 11. Handoff to the Next Task

TASK-04 implements durable HTTP idempotency for transfer submission.
