# Requirement-to-Evidence Matrix

**Status:** TASK-22 final strict compliance audit complete (branch verification)
**Baseline main SHA:** `d7833b1be63045b4f03f0828a5daabb22e634b1d` (TASK-21 merged)
**TASK-22 branch SHA:** `140f3ef` (`release/challenge-compliance-final`)
**CI SHA:** `140f3ef` — [PR #32 run 31687556055](https://github.com/mohta1/transfer-orchestration-platform/actions/runs/31687556055) (Build and Test + Runtime Verification passed)
**Last verified:** 2026-08-13 (TASK-22 clean-room audit)

This matrix maps mandatory challenge evidence to **verified** implementation, test, and documentation paths. Status meanings:

| Status | Meaning |
| ------ | ------- |
| **Verified** | Evidence inspected in repository; tests/docs exist and pass where applicable |
| **Partially verified** | Documented or structurally present; full production/Legacy path not implemented |
| **Not verified** | Required evidence missing or contradicted |

TASK-18 performed the initial submission gate. TASK-19 (fraud resilience), TASK-20 (stuck operations), and TASK-21 (leadership deliverables) merged to `main`. TASK-22 re-audited all requirements line-by-line and refreshed evidence counts.

---

## Summary Counts (TASK-22 audit, 2026-08-13)

| Status | Count |
| ------ | ----- |
| Verified | 62 |
| Partially verified | 2 |
| Not verified | 0 |
| **Total requirements** | **64** |

Partially verified (intentional non-goals): REQ-001 (Legacy coexistence runtime), REQ-049 (Legacy routing code).

---

## Test Totals (REQ-041–044, REQ-058) — TASK-22

**Counting method:** CI `dotnet test TransferOrchestrationPlatform.sln` on TASK-22 PR #32 (run 31686999881).

| Project | Passed |
| ------- | ------ |
| TransferOrchestration.Domain.Tests | 81 |
| TransferOrchestration.IntegrationTests | 210 |
| TransferOrchestration.ArchitectureTests | 12 |
| **Total** | **303** |

**Local verification (2026-08-13):** Domain 81/81 passed; Architecture 12/12 passed. Integration 210/210 passed on CI with PostgreSQL 16 service.

**Build (TASK-22):** **0 warnings**, **0 errors**.

---

## Matrix

| Requirement ID | Source | Requirement | Implementation evidence | Test evidence | Documentation / ADR / diagram | Verification status | Notes / gap |
| -------------- | ------ | ----------- | ----------------------- | ------------- | ------------------------------ | ------------------- | ----------- |
| REQ-001 | Roadmap §Mandatory | Incremental Hybrid architecture at system level | New Modular Monolith in `src/`; Legacy described as coexistence target | N/A (architecture) | [ADR-001](./adr/ADR-001-architecture-style.md), [architecture.md](./architecture.md) §11, [diagrams/migration.drawio](./diagrams/migration.drawio) | **Partially verified** | Hybrid **documented**; Legacy runtime routing not implemented in code (DEBT-007) |
| REQ-002 | Roadmap | Modular Monolith for new capability | Single deployable `TransferOrchestration.Api`; modules under `src/Modules/` | `ApiCompositionRootTests.ApiProjectReferencesEveryModuleProject` | [ADR-001](./adr/ADR-001-architecture-style.md), [diagrams/target-architecture.drawio](./diagrams/target-architecture.drawio) | **Verified** | |
| REQ-003 | Roadmap | No unnecessary Microservices | One API host; no second deployable service | `ArchitectureTests` (no extra service projects) | [ADR-001](./adr/ADR-001-architecture-style.md) §rejected alternatives | **Verified** | |
| REQ-004 | Roadmap | One PostgreSQL database, module-owned schemas | Compose `postgres` service; schemas `account_balance`, `transfer_management`, `notification`, `audit_operations` | `PersistenceMappingTests.MigrationsCreateBothModuleOwnedSchemasAndTables` | [runtime-setup.md](./runtime-setup.md), [diagrams/deployment-runtime.drawio](./diagrams/deployment-runtime.drawio) | **Verified** | Real PostgreSQL |
| REQ-005 | Roadmap | Module-specific DbContexts | `TransferManagementDbContext`, `AccountBalanceDbContext`, `NotificationDbContext`, `AuditOperationsDbContext` | Migration tests per module | [runtime-setup.md](./runtime-setup.md) §Database migration | **Verified** | |
| REQ-006 | Roadmap | No global AppDbContext | No `AppDbContext` type in solution | `ArchitectureTests` + grep | [AGENTS.md](../AGENTS.md) | **Verified** | |
| REQ-007 | Roadmap | Domain does not depend on Infrastructure | Domain projects reference no EF/Npgsql | `DomainLayerDependencyTests.*` | [engineering-standards.md](./engineering-standards.md) §2.3 | **Verified** | |
| REQ-008 | Roadmap | API remains composition root | `src/TransferOrchestration.Api/Program.cs` registers modules | `ApiCompositionRootTests.*`, `ModuleAssembliesDoNotReferenceApiAssembly` | [ADR-001](./adr/ADR-001-architecture-style.md) | **Verified** | |
| REQ-009 | Roadmap | TransferManagement owns Transfer workflow state | `Transfer`, `TransferProcessState`, `TransferProcessManager` in TransferManagement | `TransferTests`, `TransferProcessStateTests`, `PersistenceMappingTests` | [ADR-002](./adr/ADR-002-process-coordination.md), [diagrams/transfer-state-diagram.drawio](./diagrams/transfer-state-diagram.drawio) | **Verified** | |
| REQ-010 | Roadmap | Account is financial concurrency boundary | `Account.Reserve/ConsumeReservation/ReleaseReservation` | `AccountTests`, `AccountReservationContractTests` | [ADR-003](./adr/ADR-003-reservation-concurrency.md), [ubiquitous-language.md](./ubiquitous-language.md) §9 | **Verified** | Real PostgreSQL concurrency |
| REQ-011 | Roadmap | BalanceReservation owned by Account, not AR | `BalanceReservation` entity under AccountBalance Domain | `AccountTests.DuplicateReservationDoesNotReserveFundsTwice` | [AGENTS.md](../AGENTS.md), [architecture.md](./architecture.md) §8 | **Verified** | |
| REQ-012 | Roadmap | Transfer state machine + invalid-transition protection | `Transfer.Transition`, `ThrowInvalidTransition` | `TransferTests.*`, `TransferTests.RejectManuallyFromInvalidStateThrowsDomainException` | [diagrams/transfer-state-diagram.drawio](./diagrams/transfer-state-diagram.drawio) | **Verified** | |
| REQ-013 | Roadmap | Available-balance and reservation invariants | `Account` domain + DB CHECK constraints | `AccountTests`, `PersistenceMappingTests.NegativeBalanceIsRejectedByDatabase`, `DuplicateReservationTransferIdentifierIsRejectedByDatabase` | [ADR-003](./adr/ADR-003-reservation-concurrency.md) | **Verified** | Real PostgreSQL |
| REQ-014 | Roadmap | Real PostgreSQL optimistic concurrency | `AccountRepository`, `TransferRepository` conflict handling | `PersistenceMappingTests.StaleAccountRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner`, `StaleTransferRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner` | [ADR-003](./adr/ADR-003-reservation-concurrency.md) | **Verified** | |
| REQ-015 | Roadmap | HTTP idempotency using Idempotency-Key | `TransferSubmissionEndpoints`, `TransferSubmissionIdempotencyStore` | `TransferSubmissionApiTests.MissingIdempotencyKeyIsRejectedWithoutTransfer` | [architecture.md](./architecture.md) §13 | **Verified** | |
| REQ-016 | Roadmap | Same key / same payload replay | `TryClaimAsync`, `CompleteAsync` replay | `TransferSubmissionApiTests.SameKeySamePayloadReplaysWithoutSideEffectsAndDifferentPayloadConflicts`, `PersistenceMappingTests.CompletedClaimReplaysOriginalTransferResult` | [architecture.md](./architecture.md) §13 | **Verified** | Real PostgreSQL |
| REQ-017 | Roadmap | Same key / different payload conflict | Fingerprint mismatch → conflict | `TransferSubmissionApiTests.SameKeySamePayloadReplays...`, `PersistenceMappingTests.SameKeyAndDifferentFingerprintReturnsConflictWithoutOverwrite` | [ubiquitous-language.md](./ubiquitous-language.md) §8 | **Verified** | |
| REQ-018 | Roadmap | Concurrent duplicate-request safety | DB unique on idempotency scope/key | `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess`, `PersistenceMappingTests.ConcurrentIdenticalClaimsProduceExactlyOneOwnerWithoutUniqueViolation` | [ADR-003](./adr/ADR-003-reservation-concurrency.md) | **Verified** | Real PostgreSQL |
| REQ-019 | Roadmap | Persistent Process Manager | `TransferProcessManager`, `TransferProcessWorker`, durable state | `TransferProcessStateTests`, `AccountReservationContractTests.ProductionDispatcherExecutesPersistedReserveBalanceAndRestartRecoversDueWork` | [ADR-002](./adr/ADR-002-process-coordination.md) | **Verified** | |
| REQ-020 | Roadmap | Restart recovery | Workers + due-work queries survive new scope | `PersistenceMappingTests.DueProcessStateSurvivesNewApplicationScopeAndPreservesCoordinationMetadata`, `ReconciliationWorkflowTests.RestartRediscoversDueReconciliationWork`, `TransactionalOutboxTests.NewContextRediscoversPendingWork` | [ADR-002](./adr/ADR-002-process-coordination.md) | **Verified** | Real PostgreSQL |
| REQ-021 | Roadmap | Payment Network ACL | `PaymentNetworkGateway`, `IPaymentNetworkGateway` | `PaymentNetworkAclTests.*`, `PaymentNetworkBoundaryTests.*` | [diagrams/context-map.drawio](./diagrams/context-map.drawio) | **Verified** | |
| REQ-022 | Roadmap | Payment timeout is not rejection | `PaymentSubmissionResult.Timeout` → `MarkSubmissionStatusUnknown` | `PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit`, `TransferTests.PaymentTimeoutMovesTransferToSubmissionStatusUnknown` | [ADR-002](./adr/ADR-002-process-coordination.md), [diagrams/timeout-reconciliation-sequence.drawio](./diagrams/timeout-reconciliation-sequence.drawio) | **Verified** | |
| REQ-023 | Roadmap | No blind resubmission after ambiguous result | `BeginExternalSubmission` fencing; reconciliation enquiry only | `PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit` | [ubiquitous-language.md](./ubiquitous-language.md) §4 Blind Resubmission | **Verified** | |
| REQ-024 | Roadmap | Transactional Outbox | `TransferManagementDbContext.AddOutboxMessage`, `OutboxStore` | `TransactionalOutboxTests.CompletedTransferAndOutboxCommitAtomically` | [ADR-004](./adr/ADR-004-reliable-messaging.md) | **Verified** | Real PostgreSQL |
| REQ-025 | Roadmap | Atomic business-state and Outbox persistence | Same DbContext transaction | `TransactionalOutboxTests.FailedSaveCommitsNeitherCompletionNorOutboxAndPreservesMessageIdForRetry` | [ADR-004](./adr/ADR-004-reliable-messaging.md) | **Verified** | |
| REQ-026 | Roadmap | At-least-once delivery | Outbox redispatch; no exactly-once claims | `TransactionalOutboxTests.CrashAfterDeliveryPermitsSameMessageIdToBeDeliveredAgain` | [ADR-004](./adr/ADR-004-reliable-messaging.md), [ubiquitous-language.md](./ubiquitous-language.md) §7 | **Verified** | |
| REQ-027 | Roadmap | Idempotent consumers + Processed Messages | `TransferCompletedNotificationConsumer`, `ProcessedMessage` | `NotificationConsumerTests.DuplicateDeliveryCallsProviderOnceAndPersistsOneMarker` | [ADR-004](./adr/ADR-004-reliable-messaging.md) | **Verified** | Real PostgreSQL |
| REQ-028 | Roadmap | Duplicate delivery → one durable effect | Composite PK + consumer claim | `NotificationConsumerTests.ConcurrentDuplicateDeliveryHasOneEffectAndOneMarker` | [ADR-004](./adr/ADR-004-reliable-messaging.md) | **Verified** | |
| REQ-029 | Roadmap | Bounded poison-message retry | `OutboxStore.MarkFailureAsync`, max attempts | `TransactionalOutboxTests.PoisonMessageStopsAtConfiguredMaxAttempts` | [ADR-004](./adr/ADR-004-reliable-messaging.md) | **Verified** | |
| REQ-030 | Roadmap | Reconciliation and Manual Review | `ReconciliationProcessStep`, escalation to `ManualReviewRequired` | `ReconciliationWorkflowTests.ThresholdEscalatesToManualReviewRequiredAndKeepsReservationActive` | [diagrams/timeout-reconciliation-sequence.drawio](./diagrams/timeout-reconciliation-sequence.drawio) | **Verified** | Logic in TransferManagement (DEBT-001) |
| REQ-031 | Roadmap | Auditable manual operations | `OperationsAuditWriter`, `OperationsAuditRecord` | `ManualOperationsTests.ManualRejectCreatesAuditRecordWithActorAndCorrelation` | [architecture.md](./architecture.md) §16 | **Verified** | Real PostgreSQL |
| REQ-032 | Roadmap | Trusted authenticated actor identity | JWT claims; reject client operator header | `SecurityBoundaryTests.ActorReachesAudit`, `ClientSuppliedOperatorHeaderCannotImpersonateAuditActor` | [engineering-standards.md](./engineering-standards.md) §10.2 | **Verified** | |
| REQ-033 | Roadmap | Correlation and causation | `CorrelationMiddleware`, headers | `ManualOperationsTests.CorrelationFromHeaderPropagatesToAuditAndStructuredLogs`, `TransferSubmissionApiTests.SuccessfulSubmissionPersistsOneTransferAndProcessAndPropagatesCorrelation` | [ubiquitous-language.md](./ubiquitous-language.md) §10 | **Verified** | |
| REQ-034 | Roadmap | POST transfer behaviour | `TransferSubmissionEndpoints`, `TransferSubmissionService` | `TransferSubmissionApiTests.SuccessfulSubmissionPersistsOneTransferAndProcessAndPropagatesCorrelation` | [diagrams/transfer-happy-path.drawio](./diagrams/transfer-happy-path.drawio) | **Verified** | |
| REQ-035 | Roadmap | GET / read behaviour | `TransferReadEndpoints` | `TransferReadAndHealthApiTests.GetExistingTransferReturnsMappedDetailsAndCorrelationHeader` | [architecture.md](./architecture.md) | **Verified** | |
| REQ-036 | Roadmap | Safe Problem Details / error semantics | `ApiProblemResults` | `TransferReadAndHealthApiTests.InternalExceptionDoesNotLeakImplementationDetails`, `InvalidPostReturnsBadRequestProblemDetails` | [engineering-standards.md](./engineering-standards.md) §10.5 | **Verified** | |
| REQ-037 | Roadmap | Authentication and authorization | JWT in `SecurityServiceCollectionExtensions` | `SecurityBoundaryTests.PostTransferUnauthenticatedRejected`, `PostTransferAuthorizedSucceeds` | [architecture.md](./architecture.md) §18 | **Verified** | |
| REQ-038 | Roadmap | Customer resource-ownership concealment | Cross-customer → 404 | `SecurityBoundaryTests.GetTransferCrossCustomerReturnsNotFound`, `TransferReadAndHealthApiTests.GetTransferOwnedByAnotherCustomerReturnsNotFound` | [engineering-standards.md](./engineering-standards.md) §10.1 | **Verified** | |
| REQ-039 | Roadmap | Operator-only manual commands | Role policy on manual endpoints | `SecurityBoundaryTests.ManualCommandOrdinaryUserForbidden`, `ManualOperatorSucceeds` | [team-engineering-model.md](./team-engineering-model.md) §10 | **Verified** | |
| REQ-040 | Roadmap | Liveness and readiness | `/health/live`, `/health/ready`, `PostgreSqlHealthCheck` | `TransferReadAndHealthApiTests.LivenessRemainsHealthyWhenDatabaseIsUnavailable`, `ReadinessIsHealthyWhenDatabaseIsReachable`, `ReadinessIsUnhealthyWhenDatabaseIsUnavailable` | [runtime-setup.md](./runtime-setup.md) | **Verified** | |
| REQ-041 | Roadmap | ≥10 meaningful domain tests | Domain test project | **81** tests in `TransferOrchestration.Domain.Tests` (TASK-21 recount) | [TASK-15](./tasks/TASK-15-test-hardening.md) §10 | **Verified** | Exceeds minimum |
| REQ-042 | Roadmap | ≥12 meaningful integration tests | Integration test project | **210** passed on CI (TASK-22 PR #32) | [TASK-15](./tasks/TASK-15-test-hardening.md) §10 | **Verified** | Real PostgreSQL |
| REQ-043 | Roadmap | Genuine PostgreSQL concurrency tests | Concurrent reservation/idempotency tests | `AccountReservationContractTests.ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser`, `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess` | [TASK-15](./tasks/TASK-15-test-hardening.md) | **Verified** | |
| REQ-044 | Roadmap | Restart, Outbox, duplicate-delivery, security coverage | Multiple test classes | `TransactionalOutboxTests`, `NotificationConsumerTests`, `ReconciliationWorkflowTests`, `SecurityBoundaryTests` | [TASK-15](./tasks/TASK-15-test-hardening.md) §requirement matrix | **Verified** | |
| REQ-045 | Roadmap | Mechanical architecture enforcement | Architecture test project | **12** tests in `TransferOrchestration.ArchitectureTests` | [TASK-15](./tasks/TASK-15-test-hardening.md) | **Verified** | |
| REQ-046 | Roadmap | Negative architecture proof | Documented procedure | TASK-15 evidence: temporary forbidden dependency fails then reverts | [TASK-15](./tasks/TASK-15-test-hardening.md) §10 | **Verified** | Procedure documented |
| REQ-047 | Roadmap | Exactly five ADRs | `docs/adr/ADR-001` … `ADR-005` | N/A | Five files only | **Verified** | |
| REQ-048 | Roadmap | All eight mandatory diagrams | `docs/diagrams/*.drawio` (8 files) | N/A | Listed in §Diagram inventory below | **Verified** | Valid Draw.io XML |
| REQ-049 | Roadmap | Incremental legacy-modernisation documentation | N/A in New code | N/A | [modernisation-roadmap.md](./modernisation-roadmap.md), [ADR-005](./adr/ADR-005-legacy-modernisation.md), [diagrams/migration.drawio](./diagrams/migration.drawio) | **Partially verified** | Documentation complete; Legacy routing ACL not coded |
| REQ-050 | Roadmap | Engineering standards | N/A | N/A | [engineering-standards.md](./engineering-standards.md) | **Verified** | TASK-17 |
| REQ-051 | Roadmap §27 | Team engineering model (3 BE + QA + PO + DevOps) | N/A | N/A | [team-engineering-model.md](./team-engineering-model.md) — 14 checklist sections | **Verified** | TASK-21 §27 complete |
| REQ-052 | Roadmap | AI-assisted engineering safeguards | N/A | N/A | [ai-assisted-engineering.md](./ai-assisted-engineering.md) | **Verified** | TASK-17 |
| REQ-053 | Roadmap §29 | Technical-debt prioritisation + eight-week trade-off | N/A | N/A | [technical-debt-prioritisation.md](./technical-debt-prioritisation.md) §11 — six concerns | **Verified** | TASK-21 §29 exercise |
| REQ-054 | Roadmap | Docker-based local runtime | `docker-compose.yml`, `Dockerfile` | `scripts/verify-compose-runtime.ps1` | [runtime-setup.md](./runtime-setup.md), TASK-16 evidence | **Verified** | |
| REQ-055 | Roadmap | Deterministic migration/setup | `migrate` service, `scripts/apply-database-migrations.*` | Migrations apply in CI/local | [runtime-setup.md](./runtime-setup.md) | **Verified** | |
| REQ-056 | Roadmap | Persistent PostgreSQL volume | `transfer_postgres_data` volume | TASK-16 compose verification script | [runtime-setup.md](./runtime-setup.md), TASK-16 | **Verified** | |
| REQ-057 | Roadmap | Clean runtime image | Multi-stage `Dockerfile` | CI runtime-verification job inspects image | [.github/workflows/ci.yml](../.github/workflows/ci.yml), TASK-16 | **Verified** | No Tests.dll, no SDK |
| REQ-058 | Roadmap | Clean restore/build/test | Solution build + README | TASK-22: 0 warnings, 0 errors, **303** tests (81/210/12) | [README.md](../README.md), [runtime-setup.md](./runtime-setup.md) | **Verified** | CI run 31686999881 |
| REQ-059 | Roadmap | GitHub Actions CI | `.github/workflows/ci.yml` | CI passed on TASK-22 SHA `140f3ef` (run 31687556055) | TASK-22 PR #32 | **Verified** | Final main SHA confirmation pending TASK-22 merge |
| REQ-060 | Roadmap | Reviewer README, demo path, secret hygiene | [README.md](../README.md), `scripts/seed-local-demo-data.*`, `scripts/LocalDevToken/`, `scripts/demo-transfer-payload.json` | Live Compose demo: POST 202, GET 200/404/401, idempotency replay/conflict | [README.md](../README.md), TASK-18 evidence | **Verified** | No committed secrets; token helper reads env only |
| REQ-061 | Roadmap §30 | Architecture Review Simulation | N/A | N/A | [architecture-review-simulation.md](./architecture-review-simulation.md) — 12 headings | **Verified** | TASK-21; aligns ADR-001–005 |
| REQ-062 | Roadmap §34 | AI-Assisted Engineering Report (candidate) | N/A | N/A | [ai-assisted-engineering-report.md](./ai-assisted-engineering-report.md) (~1,050 words) | **Verified** | TASK-21; policy remains in ai-assisted-engineering.md |

---

## Diagram Inventory (REQ-048)

| File | Referenced by |
| ---- | ------------- |
| [context-map.drawio](./diagrams/context-map.drawio) | architecture.md §7 |
| [deployment-runtime.drawio](./diagrams/deployment-runtime.drawio) | architecture.md, runtime-setup.md |
| [event-storming.drawio](./diagrams/event-storming.drawio) | event-storming-summary.md |
| [migration.drawio](./diagrams/migration.drawio) | modernisation-roadmap.md |
| [target-architecture.drawio](./diagrams/target-architecture.drawio) | architecture.md §11 |
| [timeout-reconciliation-sequence.drawio](./diagrams/timeout-reconciliation-sequence.drawio) | architecture.md §16 |
| [transfer-happy-path.drawio](./diagrams/transfer-happy-path.drawio) | architecture.md |
| [transfer-state-diagram.drawio](./diagrams/transfer-state-diagram.drawio) | architecture.md §8 |

---

## Test Totals (historical — TASK-21)

Verified TASK-21 (2026-08-13). Domain/architecture tests executed locally; integration totals from CI PR #31.

| Project | Passed / counted |
| ------- | ---------------- |
| TransferOrchestration.Domain.Tests | 81 |
| TransferOrchestration.IntegrationTests | 210 |
| TransferOrchestration.ArchitectureTests | 12 |
| **Total** | **303** |

Build (TASK-21): **0 warnings**, **0 errors**.

---

## TASK-19 Gap Remediation — Fraud Screening (Scenario E, Domain Test #5)

Verified TASK-19 (2026-08-13) on branch `feature/fraud-screening-resilience` with `TEST_DATABASE_CONNECTION_STRING` → local PostgreSQL 16.

| Evidence | Requirement | Implementation | Tests |
| -------- | ----------- | -------------- | ----- |
| Scenario E | Fraud timeout/unavailability must not silently proceed; workflow remains recoverable with bounded retry and Manual Review escalation | Durable `RequestFraudScreening` process action; `FraudScreeningResult` outcomes; `FraudScreeningProcessStep` (external call outside DB, outcome in short transaction); `FraudScreeningRetryPolicy` | `FraudScreeningWorkflowTests.TimeoutLeavesDurableRecoverableWork`, `TemporarilyUnavailableLeavesDurableRecoverableWork`, `MaximumAttemptsEscalateToManualReview`, `RestartRediscoversPendingFraudWork`, `ConcurrentDuplicateClaimsProduceOneFraudTransition` |
| Domain Test #5 | Fraud-rejected Transfer cannot continue | `Transfer.RejectForFraud`; guards on reservation/submission/settlement/completion | `TransferFraudScreeningTests.*` (6 tests) |
| HTTP idempotency during pending fraud | Same key replay while screening pending creates no duplicate work | Submission persists `PendingFraudScreening` before worker runs | `TransferSubmissionApiTests.SameKeySamePayloadReplaysWithoutSideEffectsAndDifferentPayloadConflicts`, `SameKeyReplayDuringPendingFraudScreeningCreatesNoDuplicateWork` |
| Fraud before reservation | No balance reservation without fraud approval | Approved fraud schedules exactly one `ReserveBalance` action | `FraudScreeningWorkflowTests.ApprovedFraudSchedulesExactlyOneReserveBalanceAction`, `TransferSubmissionApiTests.FraudRejectionCannotReachBalanceReservation` |

Updated test totals (TASK-19):

| Project | Passed |
| ------- | ------ |
| TransferOrchestration.Domain.Tests | 66 |
| TransferOrchestration.IntegrationTests | 189 |
| TransferOrchestration.ArchitectureTests | 12 |
| **Total** | **267** |

Build: **0 warnings**, **0 errors**. Integration suite run twice in fresh processes: both passed 189/189.

---

## TASK-20 Gap Remediation — Stuck Operations and Observability (Scenario K, §24)

Verified TASK-20 (2026-08-13) on branch `feature/operations-observability`. Requires `TEST_DATABASE_CONNECTION_STRING` → PostgreSQL 16.

| Evidence | Requirement | Implementation | Tests |
| -------- | ----------- | -------------- | ----- |
| Scenario K | Stuck transfers detectable, visible, investigable, recoverable via auditable actions | `IStuckTransferQueries`, `StuckTransferClassifier`, `GET /api/operations/stuck-transfers`, `StuckTransferOperationsOptions` | `StuckTransferOperationsTests.*`, `StuckTransferClassifierTests.*` |
| §24 structured baseline | Correlation, causation, transfer ID, safe fingerprints, external duration/outcome, retries, reconciliation, outbox, concurrency, manual actions, unknown submission, stuck query | `OperationalTelemetry`, workflow step logging, `CorrelationMiddleware` | `ObservabilityTelemetryTests.*`, existing Outbox/correlation tests |
| Security | Operator-only; no sensitive projection | `OperationsEndpoints` + `AuthorizationPolicies.Operator` | `StuckTransferOperationsTests` 401/403/200, projection redaction |
| Recovery | Existing manual reject/confirm only | Unchanged `ManualOperationsEndpoints` | `DiscoveredTransferSupportsManualRecoveryWithAudit` |

**Stuck definition assumptions:** UTC age from `max(transfer.UpdatedAtUtc, process.UpdatedAtUtc)`; default threshold 600s; excludes terminal states and future scheduled process/reconciliation work; eligible workflow states only (excludes `Draft`, `ValidationFailed`).

**§24 field matrix (summary):**

| Field | Status |
| ----- | ------ |
| CorrelationId / CausationId / TransferId | Implemented structured log + HTTP scope |
| Safe AccountId / Idempotency key | Fingerprint helpers (`OperationalTelemetry`) |
| State transitions | Submission + fraud screening logs |
| External call duration/outcome | Fraud, payment, reconciliation logs |
| Retry / reconciliation | RetryScheduled, ReconciliationOutcome logs |
| Outbox status | Existing OutboxBatchDispatcher logs |
| Concurrency conflicts | Payment submission lost-claim log |
| Manual actions | ManualAction log + audit persistence |
| SubmissionStatusUnknown | Dedicated warning log |
| Stuck transfers | Query log + operator endpoint (persisted evidence) |
| Identified metrics only | Submission outcomes, latency percentiles, backlog counts per architecture §19 (no Prometheus stack) |

Updated test totals (TASK-20 merged, TASK-21 recount):

| Project | Passed |
| ------- | ------ |
| TransferOrchestration.Domain.Tests | 81 |
| TransferOrchestration.IntegrationTests | 210 |
| TransferOrchestration.ArchitectureTests | 12 |
| **Total** | **303** |

Build: **0 warnings**, **0 errors**.

---

## TASK-21 Gap Remediation — Leadership Deliverables (§§27, 29, 30, 34)

Verified TASK-21 (2026-08-13) on branch `docs/challenge-leadership-compliance`.

| Deliverable | Source | Evidence file | Validation |
| ----------- | ------ | ------------- | ---------- |
| §27 Team model | Exactly 3 BE + QA + PO + DevOps | [team-engineering-model.md](./team-engineering-model.md) | 14 required sections present |
| §29 Eight-week trade-off | Six named concerns | [technical-debt-prioritisation.md](./technical-debt-prioritisation.md) §11 | Exact classifications + phase table |
| §30 Architecture review | Microservice proposal simulation | [architecture-review-simulation.md](./architecture-review-simulation.md) | 12 headings; ADR-aligned |
| §34 AI report | Candidate report (≤2 pages) | [ai-assisted-engineering-report.md](./ai-assisted-engineering-report.md) | 14 items; ~1,050 words |
| Stale REMAINING-TASKS | Archived notice | [tasks/REMAINING-TASKS.md](./tasks/REMAINING-TASKS.md) | Points to 00-ROADMAP-INDEX |
| Diagram corrections | Implemented vs target labels | [diagrams/deployment-runtime.drawio](./diagrams/deployment-runtime.drawio), [event-storming.drawio](./diagrams/event-storming.drawio) | Valid Draw.io XML |

Updated test totals (TASK-20 merged):

| Project | Passed |
| ------- | ------ |
| TransferOrchestration.Domain.Tests | 81 |
| TransferOrchestration.IntegrationTests | 210 |
| TransferOrchestration.ArchitectureTests | 12 |
| **Total** | **303** |

Build: **0 warnings**, **0 errors**.

---

## TASK-22 — Final Strict Compliance Audit

Verified 2026-08-13 on branch `release/challenge-compliance-final` SHA `140f3ef` (from main `d7833b1`).

### Audit outcome

| Area | Result |
| ---- | ------ |
| 27 §37 deliverables | All present with evidence paths |
| 22 §6 business rules | Mapped to code + tests (see matrix below) |
| Failure scenarios A–K | Verified (E fraud timeout, K stuck operations closed in TASK-19/20) |
| §23 domain tests (10) | All covered (81 domain cases) |
| §23 integration tests (12) | All covered (210 integration cases on CI) |
| §24 observability baseline | Structured logs implemented; metrics identified only (no false Prometheus claim) |
| §25 security | JWT auth, masking, audit actor, operator separation verified |
| §§27–34 leadership docs | Complete (TASK-21) |
| Five ADRs / eight diagrams | Verified |
| Build | 0 warnings, 0 errors |
| Blockers | **None** |

**Final main verification pending user merge of TASK-22 PR.**

### §37 deliverable inventory (27 items)

| # | Deliverable | Evidence path | Status |
| - | ----------- | ------------- | ------ |
| 1 | Source code | `src/` | Verified |
| 2 | Tests | `tests/` | Verified |
| 3 | README | [README.md](../README.md) | Verified |
| 4 | Architecture document | [architecture.md](./architecture.md) | Verified |
| 5 | Ubiquitous Language | [ubiquitous-language.md](./ubiquitous-language.md) | Verified |
| 6 | Event Storming diagram | [diagrams/event-storming.drawio](./diagrams/event-storming.drawio) | Verified |
| 7 | Event Storming summary | [event-storming-summary.md](./event-storming-summary.md) | Verified |
| 8 | Context Map | [diagrams/context-map.drawio](./diagrams/context-map.drawio) | Verified |
| 9 | Target architecture diagram | [diagrams/target-architecture.drawio](./diagrams/target-architecture.drawio) | Verified |
| 10 | Migration diagram | [diagrams/migration.drawio](./diagrams/migration.drawio) | Verified |
| 11 | Happy path diagram | [diagrams/transfer-happy-path.drawio](./diagrams/transfer-happy-path.drawio) | Verified |
| 12 | Timeout/state diagram | [diagrams/transfer-state-diagram.drawio](./diagrams/transfer-state-diagram.drawio), [timeout-reconciliation-sequence.drawio](./diagrams/timeout-reconciliation-sequence.drawio) | Verified |
| 13 | Deployment diagram | [diagrams/deployment-runtime.drawio](./diagrams/deployment-runtime.drawio) | Verified |
| 14–18 | ADR-001 … ADR-005 | [adr/](./adr/) (exactly 5 files) | Verified |
| 19 | Team model (§27) | [team-engineering-model.md](./team-engineering-model.md) | Verified |
| 20 | Engineering standards (§28) | [engineering-standards.md](./engineering-standards.md) | Verified |
| 21 | Technical debt + §29 | [technical-debt-prioritisation.md](./technical-debt-prioritisation.md) | Verified |
| 22 | AI report (§34) | [ai-assisted-engineering-report.md](./ai-assisted-engineering-report.md) | Verified |
| 23 | Transfer aggregate | `TransferManagement/Domain/Transfer.cs` | Verified |
| 24 | Reservation/idempotency/concurrency | Account + idempotency store | Verified |
| 25 | Outbox/durable processing | Outbox + workers | Verified |
| 26 | Idempotent consumer | Notification consumer + ProcessedMessage | Verified |
| 27 | Requirement traceability | This document | Verified |
| — | Architecture Review Simulation (§30) | [architecture-review-simulation.md](./architecture-review-simulation.md) | Verified |

### §6 business rules matrix (22 rules)

| Rule | Implementation | Test evidence |
| ---- | -------------- | ------------- |
| Same currency | `Account.Reserve` currency check | `AccountReservationContractTests.ProcessStepCannotAdvanceWhenReservationContractReportsCurrencyMismatch` |
| Active account only | `Account` status guard | `AccountTests.ReserveThrowsWhenAccountInactive` |
| Customer authorized for source | `AuthenticatedCustomerAuthorization` | `SecurityBoundaryTests.PostTransferWrongAccountForbidden` |
| Daily limit | `DailyTransferLimitService` | `TransferSubmissionApiTests.CumulativeDailyLimitRejectionPreventsFraud` |
| One Transfer per request | Idempotency + submission service | `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess` |
| One reservation per Transfer | DB unique + domain guard | `AccountTests.DuplicateReservationDoesNotReserveFundsTwice`, `PersistenceMappingTests.DuplicateReservationTransferIdentifierIsRejectedByDatabase` |
| Fraud before reservation | Durable `RequestFraudScreening` step | `FraudScreeningWorkflowTests.ApprovedFraudSchedulesExactlyOneReserveBalanceAction` |
| Completed/rejected/cancelled terminal | `Transfer.Transition` guards | `TransferTests.*`, `StuckTransferClassifierTests.TerminalStatesAreNotEligible` |
| Release/consume exactly once | Account domain + DB constraints | `AccountTests.ConsumeReservation*`, `AccountTests.ReleaseReservation*` |
| Payment timeout ≠ rejection | `MarkSubmissionStatusUnknown` | `PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit` |
| No duplicate financial effects | Optimistic concurrency + idempotency | `AccountReservationContractTests.ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser` |
| Outbox event eventual | Transactional Outbox | `TransactionalOutboxTests.CompletedTransferAndOutboxCommitAtomically` |
| Duplicate delivery safe | ProcessedMessage dedup | `NotificationConsumerTests.DuplicateDeliveryCallsProviderOnceAndPersistsOneMarker` |
| Invalid transitions rejected | `ThrowInvalidTransition` | `TransferTests.RejectManuallyFromInvalidStateThrowsDomainException` |
| Concurrency on Account | Row version + short transactions | `PersistenceMappingTests.StaleAccountRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner` |
| Manual operations audited | `OperationsAuditWriter` | `ManualOperationsTests.ManualRejectCreatesAuditRecordWithActorAndCorrelation` |
| Internal transfer not external | Payment ACL routing | `PaymentNetworkAclTests.InternalBankTransferIsNotSentToExternalNetwork` |
| No blind resubmission | Submission fencing | `PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit` |
| Fraud rejection stops workflow | `RejectForFraud` | `TransferFraudScreeningTests.FraudRejectedTransferCannotRequestBalanceReservation` |
| Fraud timeout recoverable | Retry policy + Manual Review | `FraudScreeningWorkflowTests.MaximumAttemptsEscalateToManualReview` |
| Reconciliation bounded retry | Reconciliation process step | `ReconciliationWorkflowTests.ThresholdEscalatesToManualReviewRequiredAndKeepsReservationActive` |
| Stuck transfer detectable | Stuck query endpoint | `StuckTransferOperationsTests.OldEligibleProcessAppearsInOperatorQuery` |

### Failure scenarios A–K

| Scenario | Code path | Test evidence | PostgreSQL |
| -------- | --------- | ------------- | ---------- |
| A Concurrent reservation | `Account.Reserve` | `AccountReservationContractTests.ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser` | Yes |
| B Duplicate HTTP submission | Idempotency store | `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess` | Yes |
| C Payment timeout | Payment ACL + reconciliation | `PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit` | Yes |
| D Outbox failure/retry | `OutboxStore` | `TransactionalOutboxTests.FailedSaveCommitsNeitherCompletionNorOutboxAndPreservesMessageIdForRetry` | Yes |
| E Fraud timeout | `FraudScreeningProcessStep` | `FraudScreeningWorkflowTests.TimeoutLeavesDurableRecoverableWork`, `MaximumAttemptsEscalateToManualReview` | Yes |
| F Duplicate settlement | Reconciliation idempotency | `ReconciliationWorkflowTests.DuplicateSettledStatusIsIdempotent` | Yes |
| G Duplicate consumer | Notification consumer | `NotificationConsumerTests.ConcurrentDuplicateDeliveryHasOneEffectAndOneMarker` | Yes |
| H Restart recovery | Workers + due-work queries | `TransactionalOutboxTests.NewContextRediscoversPendingWork`, `FraudScreeningWorkflowTests.RestartRediscoversPendingFraudWork` | Yes |
| I Optimistic concurrency | Repository conflict handling | `PersistenceMappingTests.StaleTransferRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner` | Yes |
| J Poison message | Outbox max attempts | `TransactionalOutboxTests.PoisonMessageStopsAtConfiguredMaxAttempts` | Yes |
| K Stuck transfer | `IStuckTransferQueries` + operator endpoint | `StuckTransferOperationsTests.DiscoveredTransferSupportsManualRecoveryWithAudit` | Yes |

### §23 domain test scenarios (10 minimum)

| # | Scenario | Test class/method |
| - | -------- | ----------------- |
| 1 | Successful reservation | `AccountTests.ReserveReducesAvailableBalance` |
| 2 | Insufficient balance | `AccountTests.ReserveThrowsWhenInsufficientBalance` |
| 3 | Duplicate reservation | `AccountTests.DuplicateReservationDoesNotReserveFundsTwice` |
| 4 | Invalid transition | `TransferTests.RejectManuallyFromInvalidStateThrowsDomainException` |
| 5 | Fraud-rejected cannot continue | `TransferFraudScreeningTests.*` (6 methods) |
| 6 | Payment timeout state | `TransferTests.PaymentTimeoutMovesTransferToSubmissionStatusUnknown` |
| 7 | Consume reservation | `AccountTests.ConsumeReservationReducesReservedAndTotalBalance` |
| 8 | Release reservation | `AccountTests.ReleaseReservationRestoresAvailableBalance` |
| 9 | Process state transitions | `TransferProcessStateTests.*` |
| 10 | Fingerprint/idempotency | `TransferSubmissionFingerprintTests.*` |

### §23 integration test scenarios (12 minimum)

| # | Scenario | Test class |
| - | -------- | ---------- |
| 1 | Successful submission | `TransferSubmissionApiTests.SuccessfulSubmissionPersistsOneTransferAndProcessAndPropagatesCorrelation` |
| 2 | Idempotent replay | `TransferSubmissionApiTests.SameKeySamePayloadReplaysWithoutSideEffectsAndDifferentPayloadConflicts` |
| 3 | Idempotency conflict | Same as #2 (409 path) |
| 4 | Concurrent duplicate HTTP | `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess` |
| 5 | Concurrent reservations | `AccountReservationContractTests.ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser` |
| 6 | Outbox atomic commit | `TransactionalOutboxTests.CompletedTransferAndOutboxCommitAtomically` |
| 7 | Outbox retry | `TransactionalOutboxTests.FailedSaveCommitsNeitherCompletionNorOutboxAndPreservesMessageIdForRetry` |
| 8 | Duplicate consumer | `NotificationConsumerTests.DuplicateDeliveryCallsProviderOnceAndPersistsOneMarker` |
| 9 | Restart recovery | `TransactionalOutboxTests.NewContextRediscoversPendingWork` |
| 10 | Optimistic concurrency | `PersistenceMappingTests.StaleAccountRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner` |
| 11 | Security boundary | `SecurityBoundaryTests.*` |
| 12 | Reconciliation | `ReconciliationWorkflowTests.*` |

### Observability matrix (§24)

| Field | Structured log | Persisted/queryable | Metric | Notes |
| ----- | -------------- | ------------------- | ------ | ----- |
| CorrelationId / CausationId | Yes | Audit/outbox records | Identified | `CorrelationMiddleware` |
| TransferId | Yes | DB | Identified | |
| Safe AccountId fingerprint | Yes | No raw values | Identified | `OperationalTelemetry` |
| Safe idempotency fingerprint | Yes | No raw keys | Identified | `ObservabilityTelemetryTests` |
| State transitions | Yes | Transfer table | Identified | |
| External call duration/outcome | Yes | Process metadata | Identified | Fraud/payment/reconciliation |
| Retry/reconciliation attempts | Yes | Process/reconciliation records | Identified | |
| Outbox status | Yes | Outbox table | Identified | |
| Concurrency conflicts | Yes | — | Identified | |
| Manual actions | Yes | Audit table | Identified | |
| SubmissionStatusUnknown | Yes | Transfer state | Identified | |
| Stuck transfers | Yes | Query endpoint | Identified | No background alert worker |

Full monitoring infrastructure (Prometheus/Grafana) is **not** deployed or claimed.

### Security audit (§25)

| Control | Evidence |
| ------- | -------- |
| Authorization at application layer | `AuthenticatedCustomerAuthorization`, policy handlers |
| Customer/operator permissions | JWT role claims; `SecurityBoundaryTests` |
| Service auth non-goal | Documented in README — no token issuance |
| Replay/idempotency | HTTP idempotency store + consumer dedup |
| Masking | `ObservabilityTelemetryTests.IdempotencyFingerprintNeverContainsRawKey` |
| Secrets not committed | `.env` gitignored; `.env.example` placeholders only |
| Audit actor from JWT `sub` | `ManualOperationsTests`, `SecurityBoundaryTests.ActorReachesAudit` |
| Operator/customer separation | 401/403/404 tests across endpoints |
| Manual recovery protection | Operator-only manual routes |

### Leadership documentation audit (§§27–34)

| Section | Requirement | Status |
| ------- | ----------- | ------ |
| §27 | 3 BE + QA + PO + DevOps; 13 model items | Verified — 14 sections in team-engineering-model.md |
| §28 | Concrete engineering standards | Verified — engineering-standards.md |
| §29 | Six concerns, four classifications each | Verified — technical-debt-prioritisation.md §11 |
| §30 | Architecture review simulation | Verified — architecture-review-simulation.md (12 headings) |
| §31 | 24 architecture topics | Verified — architecture.md |
| §34 | AI report ≤2 pages | Verified — ai-assisted-engineering-report.md (~1,050 words) |

### Clean-room validation

| Step | Result |
| ---- | ------ |
| `dotnet tool restore` | Passed |
| `dotnet restore` | Passed |
| `dotnet build --no-restore` | 0 warnings, 0 errors |
| `dotnet test` domain | 81/81 passed |
| `dotnet test` architecture | 12/12 passed |
| `dotnet test` integration (CI) | 210/210 passed (run 31686999881) |
| Docker Compose config/build/runtime | CI Runtime Verification passed (run 31686999881; initial Docker Hub 500 transient, rerun succeeded) |
| Secret/git scan | No committed secrets, `.env`, bin/obj, or credentials found |
| Link/placeholder scan | No TODO/TBD in required docs; REMAINING-TASKS archived |
| Draw.io XML | 8 diagram files present; valid XML structure |

### Findings classification

| ID | Finding | Classification |
| -- | ------- | -------------- |
| F-01 | Legacy runtime routing not implemented | Non-blocking (documented non-goal per ADR-005) |
| F-02 | Reconciliation logic in TransferManagement (DEBT-001) | Non-blocking improvement |
| F-03 | Windows `list-tests` reports 206 integration vs CI 210 passed | Preference — CI pass count authoritative |
| F-04 | Local Docker unavailable during TASK-22 audit | Non-blocking — CI runtime verification passed on PR #32 |
| F-05 | No dedicated GET-without-token 401 test | Preference — covered via POST/manual/stuck paths |

**Blockers: 0**

---

## Related Documents

- [00-ROADMAP-INDEX.md](./tasks/00-ROADMAP-INDEX.md)
- [TASK-17-engineering-delivery.md](./tasks/TASK-17-engineering-delivery.md)
- [TASK-18-final-challenge-review.md](./tasks/TASK-18-final-challenge-review.md) *(submission gate — branch verified, main merge pending)*
