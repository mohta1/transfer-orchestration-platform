# Audit, Operations, Correlation, and Manual Commands

**Task ID:** TASK-12
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/audit-operations`
**Depends on:** TASK-11
**Status:** Not Started

---

## 1. Objective

Make manual/operational actions auditable and propagate correlation identifiers through API, workflow, Outbox, reconciliation, and logs.

## 2. Why This Task Exists

The challenge requires manual actions to be auditable and emphasizes observability.

## 3. Scope

### In Scope
- OperationsAuditRecord persistence.
- Actor/action/Transfer/timestamp/reason/correlation fields.
- Manual application command(s).
- Audit writer.
- Correlation middleware/service.
- Structured logging enrichment.

### Out of Scope
- Full IAM.
- Operator UI.
- SIEM integration.

## 4. Required Deliverables

- OperationsAuditRecord persistence.
- Actor/action/Transfer/timestamp/reason/correlation fields.
- Manual application command(s).
- Audit writer.
- Correlation middleware/service.
- Structured logging enrichment.

## 5. Implementation Requirements

- Manual actions require a reason.
- Audit behaves append-only.
- No raw credentials/tokens/account numbers in structured logs.
- Transfer logs include CorrelationId and TransferId where known.
- Manual actions still respect domain transitions.

## 6. Required Tests

- Manual action creates audit record.
- Missing reason rejected.
- Actor identity recorded.
- Correlation propagates into durable metadata/logs.
- Sensitive values are not logged.

## 7. Verification Procedure

1. Run integration tests.
2. Inspect structured logs for a sample lifecycle.

## 8. Acceptance Criteria

- [ ] Manual actions are reconstructable from audit records.
- [ ] Correlation propagation is consistent.
- [ ] No direct state bypass.

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

- Sample audit row.
- Representative structured logs.

## 11. Handoff to the Next Task

TASK-13 completes GET/error/health semantics.
