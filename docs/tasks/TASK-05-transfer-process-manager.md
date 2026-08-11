# Transfer Process Manager and Durable Workflow State

**Task ID:** TASK-05
**Stage:** Stage 2 — Submission & Coordination
**Recommended branch:** `feature/transfer-process-manager`
**Depends on:** TASK-04
**Status:** Done

---

## 1. Objective

Implement durable process-coordination state recording orchestration progress and the next due action for each Transfer.

## 2. Why This Task Exists

ADR-002 selected a Persistent Process Manager. Workflow progress must survive restarts and cannot depend on in-memory queues.

## 3. Scope

### In Scope
- TransferProcessState model/mapping/migration.
- TransferId and CorrelationId linkage.
- Current step/status.
- Retry/NextAttempt metadata where needed.
- Durable due-work query.
- Application service for process progression.
- Restart recovery test.

### Out of Scope
- Payment Network.
- Outbox.
- Reconciliation implementation.
- Final external retry policies.

## 4. Required Deliverables

- TransferProcessState model/mapping/migration.
- TransferId and CorrelationId linkage.
- Current step/status.
- Retry/NextAttempt metadata where needed.
- Durable due-work query.
- Application service for process progression.
- Restart recovery test.

## 5. Implementation Requirements

- Process state must not duplicate the entire Transfer.
- Coordinator does not own Transfer/Account invariants.
- No long database transaction around external calls.
- No fire-and-forget work.
- Restart rediscovers due work.

## 6. Required Tests

- Process state created with Transfer.
- Next action persists.
- New scope/app instance rediscovers due work.
- Invalid process update rejected.
- Due-work query behavior is deterministic.

## 7. Verification Procedure

1. Persist a due process row.
2. Dispose scopes / simulate restart.
3. Recreate scopes and query due work.
4. Run build and tests.

## 8. Acceptance Criteria

- [x] Durable workflow survives restart.
- [x] No in-memory-only orchestration dependency.
- [x] Design remains consistent with ADR-002.

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

- Restart test output.
- Database row showing durable next action.

## 11. Handoff to the Next Task

TASK-06 builds the first real POST vertical slice.
