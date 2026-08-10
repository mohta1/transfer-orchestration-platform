# Runtime Hardening, Docker Compose, and CI

**Task ID:** TASK-16
**Stage:** Stage 4 — Verification & Delivery
**Recommended branch:** `feature/runtime-ci`
**Depends on:** TASK-15
**Status:** Not Started

---

## 1. Objective

Make the repository reproducibly buildable/testable from a clean checkout and runnable through Docker Compose, then encode the same gates in CI.

## 2. Why This Task Exists

Reviewers should be able to validate the repository without relying on local machine state.

## 3. Scope

### In Scope
- Dockerfile review.
- Docker Compose API + PostgreSQL.
- Health/readiness wiring.
- Migration/setup strategy documentation.
- .env.example.
- Local tool restore.
- GitHub Actions CI.
- Restore/build/test gates.
- PostgreSQL integration tests in CI where practical.

### Out of Scope
- Kubernetes.
- Cloud deployment.
- Broker infrastructure.
- Production secret manager.

## 4. Required Deliverables

- Dockerfile review.
- Docker Compose API + PostgreSQL.
- Health/readiness wiring.
- Migration/setup strategy documentation.
- .env.example.
- Local tool restore.
- GitHub Actions CI.
- Restore/build/test gates.
- PostgreSQL integration tests in CI where practical.

## 5. Implementation Requirements

- CI runs from fresh checkout.
- dotnet tool restore.
- dotnet restore.
- warning-free build.
- all tests.
- PostgreSQL integration tests.
- no real secrets.
- runtime image excludes test binaries.

## 6. Required Tests

- Clean local build from deleted bin/obj.
- docker compose up --build starts API/PostgreSQL.
- Readiness becomes healthy.
- Persistent DB volume survives restart.
- CI completes successfully.

## 7. Verification Procedure

1. Delete local bin/obj and restore/build/test from scratch.
2. Rebuild Docker without stale artifacts.
3. Run CI.

## 8. Acceptance Criteria

- [ ] Clean environment is reproducible.
- [ ] CI is green.
- [ ] Compose runtime is healthy.
- [ ] No warning/secret leakage.

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

- CI run summary/link.
- docker compose ps.
- Health/readiness output.
- Clean build/test summary.

## 11. Handoff to the Next Task

TASK-17 completes the remaining engineering-delivery documentation.
