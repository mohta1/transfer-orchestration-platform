# Security Boundary and Authorization Policy

**Task ID:** TASK-14
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/security-boundary`
**Depends on:** TASK-13
**Status:** Done

---

## 1. Objective

Add a pragmatic authentication/authorization boundary proving financial and manual commands reject unauthorized actors.

## 2. Why This Task Exists

The architecture quality target requires unauthorized financial/manual commands to fail with 401/403.

## 3. Scope

### In Scope
- ASP.NET Core authentication suitable for challenge/demo.
- Authorization policies.
- Transfer submission protection.
- Manual-command protection.
- Actor propagation to audit.
- Security integration tests.
- Secret/log review.

### Out of Scope
- Production OAuth server.
- MFA.
- HSM/KMS.

## 4. Required Deliverables

- ASP.NET Core authentication suitable for challenge/demo.
- Authorization policies.
- Transfer submission protection.
- Manual-command protection.
- Actor propagation to audit.
- Security integration tests.
- Secret/log review.

## 5. Implementation Requirements

- Unauthenticated request => 401.
- Authenticated but unauthorized manual request => 403.
- Authorized actor succeeds.
- No secrets committed.
- Bearer tokens are not logged.

## 6. Required Tests

- POST transfer unauthenticated rejected.
- POST transfer authorized succeeds.
- Manual command ordinary user forbidden.
- Manual operator succeeds.
- Actor reaches audit.
- Token/secret redaction checked.

## 7. Verification Procedure

1. Run security integration tests.
2. Review repository and logs for accidental secrets.

## 8. Acceptance Criteria

- [x] Required commands enforce 401/403 correctly.
- [x] Actor identity reaches audit.
- [x] No secret leakage.

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

### Security test summary

`dotnet test TransferOrchestrationPlatform.sln --filter "FullyQualifiedName~SecurityBoundaryTests"` => **13/13 passed** (run twice in fresh processes).

Full suite: **227/227 passed** (51 domain + 3 architecture + 173 integration).

Build: **0 warnings, 0 errors**.

### Sample audit actor identity

`SecurityBoundaryTests.ActorReachesAudit` persists `audit.ActorId == "operator-audit-actor"` from the authenticated JWT `sub` claim (not `X-Operator-ID`).

`SecurityBoundaryTests.ClientSuppliedOperatorHeaderCannotImpersonateAuditActor` confirms a forged header cannot override the trusted principal.

### Secret/log review result

- `StructuredLogsDoNotContainSensitiveValues` (ManualOperationsTests) and `TokenSecretRedactionChecked` (SecurityBoundaryTests) assert bearer tokens are absent from structured logs.
- JWT validation does not expose signing material in Problem Details responses.
- `appsettings.json` leaves `SigningKey` empty; development key is in `appsettings.Development.json` only.
- No `.env`, private keys, or real production secrets committed.

## 11. Handoff to the Next Task

TASK-15 closes mandatory test and architecture-enforcement gaps.
