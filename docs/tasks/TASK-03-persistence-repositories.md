# Persistence Repositories and Optimistic Concurrency Handling

**Task ID:** TASK-03
**Stage:** Stage 1 — Domain & Persistence
**Recommended branch:** `feature/persistence-repositories`
**Depends on:** TASK-02
**Status:** Not Started

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

- [ ] Repository consumers do not reference EF types.
- [ ] Genuine optimistic concurrency conflict reproduced.
- [ ] Winner state preserved.
- [ ] Loser receives explicit conflict outcome.
- [ ] 0 warnings / 0 errors.

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

- Concurrency test output.
- Before/after Account versions and balances.
- Repository dependency review.

## 11. Handoff to the Next Task

TASK-04 implements durable HTTP idempotency for transfer submission.
