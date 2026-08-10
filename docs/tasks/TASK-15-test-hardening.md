# Integration Test Suite and Architecture Enforcement

**Task ID:** TASK-15
**Stage:** Stage 4 — Verification & Delivery
**Recommended branch:** `feature/test-hardening`
**Depends on:** TASK-14
**Status:** Not Started

---

## 1. Objective

Close all mandatory coverage gaps and mechanically enforce Modular Monolith dependency rules.

## 2. Why This Task Exists

The challenge explicitly requires at least 10 domain tests, 12 integration tests, and a genuine concurrent test.

## 3. Scope

### In Scope
- Test inventory and requirement-to-test matrix.
- >=10 meaningful domain tests.
- >=12 meaningful integration tests.
- Genuine PostgreSQL concurrency test.
- Restart recovery tests.
- Duplicate delivery/idempotency tests.
- Architecture dependency tests.
- Negative architecture test.

### Out of Scope
- New features except blocker fixes.
- Load testing beyond documented target scenarios.

## 4. Required Deliverables

- Test inventory and requirement-to-test matrix.
- >=10 meaningful domain tests.
- >=12 meaningful integration tests.
- Genuine PostgreSQL concurrency test.
- Restart recovery tests.
- Duplicate delivery/idempotency tests.
- Architecture dependency tests.
- Negative architecture test.

## 5. Implementation Requirements

- Domain must not depend on Infrastructure.
- TransferManagement must not depend on AccountBalance Infrastructure/DbContext.
- Notification must not query Transfer tables directly.
- API remains composition root.
- BuildingBlocks remains dependency-light.
- Forbidden dependencies must fail architecture tests.

## 6. Required Tests

- Successful submission.
- Idempotent replay.
- Same key/different payload conflict.
- Concurrent duplicate submission.
- Concurrent reservations.
- Outbox retained on dispatch failure.
- Outbox retry.
- Duplicate settlement idempotent.
- Duplicate consumer one effect.
- Restart recovery.
- Optimistic concurrency conflict.
- Poison retry bounded.
- All security/reconciliation/API tests remain green.

## 7. Verification Procedure

1. dotnet test TransferOrchestrationPlatform.sln
2. Temporarily introduce one forbidden dependency locally, confirm architecture test fails, then revert.

## 8. Acceptance Criteria

- [ ] >=10 domain tests.
- [ ] >=12 integration tests.
- [ ] All tests pass.
- [ ] Genuine concurrency uses real PostgreSQL.
- [ ] Architecture violations are mechanically detectable.

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

- Final test counts.
- Requirement-to-test matrix.
- Full dotnet test summary.
- Negative architecture test proof.

## 11. Handoff to the Next Task

TASK-16 hardens runtime reproducibility and CI.
