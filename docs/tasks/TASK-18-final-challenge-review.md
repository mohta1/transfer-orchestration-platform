# Final Review, README, Demo Path, and Submission Gate

**Task ID:** TASK-18
**Stage:** Stage 4 — Verification & Delivery
**Recommended branch:** `release/final-challenge-review`
**Depends on:** TASK-17
**Status:** Not Started

---

## 1. Objective

Perform a strict final challenge audit, make the repository easy to evaluate, and permit submission only when every blocker requirement has evidence.

## 2. Why This Task Exists

A strong implementation can still fail review because of missing instructions, stale docs, secret leakage, or an unproven mandatory scenario.

## 3. Scope

### In Scope
- Final README.
- Prerequisites and local/Docker run paths.
- Migration/setup instructions.
- POST/GET examples.
- Idempotency demo.
- Timeout/reconciliation demo.
- Test commands.
- Known limitations/non-goals.
- Challenge compliance checklist.
- Clean-room validation.
- Git hygiene and final consistency audit.

### Out of Scope
- New features.
- Optional polish that risks destabilizing the baseline.
- Architecture redesign unless a blocker is found.

## 4. Required Deliverables

- Final README.
- Prerequisites and local/Docker run paths.
- Migration/setup instructions.
- POST/GET examples.
- Idempotency demo.
- Timeout/reconciliation demo.
- Test commands.
- Known limitations/non-goals.
- Challenge compliance checklist.
- Clean-room validation.
- Git hygiene and final consistency audit.

## 5. Implementation Requirements

- README covers architecture, setup, Docker, migrations, auth/demo approach, API examples, tests, ADR/diagram locations, and limitations.
- No blocker requirement remains without evidence.
- Main branch must match the exact submission state.
- No secret or generated local artifact is tracked.

## 6. Required Tests

- Clean restore.
- Warning-free build.
- All tests.
- Docker Compose startup.
- Liveness/readiness.
- Successful transfer scenario.
- Idempotent duplicate scenario.
- Concurrent reservation scenario.
- Timeout/reconciliation scenario.
- Outbox failure/retry scenario.
- Duplicate consumer scenario.
- Security rejection scenario.

## 7. Verification Procedure

1. Perform all tests from a clean local state.
2. Verify Docker runtime.
3. Review challenge compliance matrix line-by-line.
4. Check git status and tracked artifacts.
5. Confirm final PR merged to main.

## 8. Acceptance Criteria

- [ ] 0 build warnings/errors.
- [ ] All tests pass.
- [ ] >=10 domain and >=12 integration tests.
- [ ] Genuine concurrency passes.
- [ ] Exactly five ADRs.
- [ ] All eight mandatory diagrams.
- [ ] All required docs complete.
- [ ] Docker runtime works.
- [ ] README works from clean state.
- [ ] No blocker contradiction remains.

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

- Final build/test output.
- Docker health output.
- Compliance matrix.
- git status.
- Final main commit SHA.

## 11. Handoff to the Next Task

Terminal task. When TASK-18 is Done, the repository is ready to submit.
