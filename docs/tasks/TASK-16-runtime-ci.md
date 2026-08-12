# Runtime Hardening, Docker Compose, and CI

**Task ID:** TASK-16
**Stage:** Stage 4 — Verification & Delivery
**Recommended branch:** `feature/runtime-ci`
**Depends on:** TASK-15
**Status:** Done

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

- [x] Clean environment is reproducible.
- [x] CI is green.
- [x] Compose runtime is healthy.
- [x] No warning/secret leakage.

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

- CI run summary/link: https://github.com/mohta1/transfer-orchestration-platform/actions/runs/31643000229 (workflow `CI`, head SHA `892796f04d3db45fb491613a1efdf9a5a6d1ae29`, jobs `Build and Test` and `Runtime Verification` passed).
- docker compose ps: captured by `scripts/verify-compose-runtime.ps1` (postgres healthy, api running, migrate exited 0).
- Health/readiness output: liveness HTTP 200 `{"status":"Healthy",...}`; readiness HTTP 200 with PostgreSQL healthy; readiness HTTP 503 when PostgreSQL stopped; recovery HTTP 200 after restart.
- Clean build/test summary: SDK 8.0.412; 0 warnings / 0 errors; 240 tests passed (51 domain, 12 architecture, 177 integration with real PostgreSQL).

Additional local evidence (2026-08-12):

- Baseline SHA: `00ccd199c97d064fcad65f10c2c001bb4898aba8`
- Runtime image: `transfer-orchestration-platform-api:latest` (~120MB), no `*Tests.dll`, no SDK
- Persistent volume marker survived container recreate without volume deletion
- Migration/setup: one-shot `migrate` Compose service applies all module DbContext migrations via repository-owned `dotnet-ef`
- Documentation: `docs/runtime-setup.md`

## 11. Handoff to the Next Task

TASK-17 completes the remaining engineering-delivery documentation.
