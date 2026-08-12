# Audit, Operations, Correlation, and Manual Commands

**Task ID:** TASK-12
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/audit-operations`
**Depends on:** TASK-11
**Status:** Done

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

- [x] Manual actions are reconstructable from audit records.
- [x] Correlation propagation is consistent.
- [x] No direct state bypass.

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

### Sample audit row

Verified in `ManualOperationsTests.ManualRejectCreatesAuditRecordWithActorAndCorrelation` against PostgreSQL (`audit_operations.operations_audit_records`):

| column | sample value |
|---|---|
| command_id | `manual-reject-1` |
| actor_id | `operator-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa` |
| action | `RejectFromManualReview` |
| transfer_id | seeded transfer GUID |
| previous_state | `ManualReviewRequired` |
| new_state | `Rejected` |
| reason | `Customer confirmed cancellation` |
| correlation_id | `cccccccc-cccc-cccc-cccc-cccccccccccc` |
| occurred_at_utc | controlled test clock |

### Representative structured logs

`CorrelationMiddleware` emits `RequestCorrelated` entries containing `CorrelationId`, optional `TransferId`, and `OperatorId` without authorization tokens or raw account numbers. Verified in `ManualOperationsTests.CorrelationFromHeaderPropagatesToAuditAndStructuredLogs` and `StructuredLogsDoNotContainSensitiveValues`.

### Verification commands and results (2026-08-12)

```text
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
=> 0 warnings, 0 errors

dotnet test TransferOrchestrationPlatform.sln --no-build
=> Passed: 201 (Domain 50, Architecture 3, Integration 148), Failed: 0

dotnet test ... --filter FullyQualifiedName~ManualOperationsTests (run twice)
=> Passed: 9/9 each run
```

Migration: `20260812153931_AddOperationsAuditRecords` verified on PostgreSQL.

## 11. Handoff to the Next Task

TASK-13 completes GET/error/health semantics.
