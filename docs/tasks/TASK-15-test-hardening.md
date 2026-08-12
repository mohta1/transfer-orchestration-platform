# Integration Test Suite and Architecture Enforcement

**Task ID:** TASK-15
**Stage:** Stage 4 — Verification & Delivery
**Recommended branch:** `feature/test-hardening`
**Depends on:** TASK-14
**Status:** Done

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

- [x] >=10 domain tests.
- [x] >=12 integration tests.
- [x] All tests pass.
- [x] Genuine concurrency uses real PostgreSQL.
- [x] Architecture violations are mechanically detectable.

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

- Final test counts.
- Requirement-to-test matrix.
- Full dotnet test summary.
- Negative architecture test proof.

### Captured TASK-15 Evidence

Captured on 2026-08-12 against PostgreSQL through `TEST_DATABASE_CONNECTION_STRING`.

#### Final test counts and counting method

| Project | Total cases | TASK-15 category |
|---------|-------------|------------------|
| `TransferOrchestration.Domain.Tests` | **51** | **46 meaningful domain** + 5 application-in-domain-project |
| `TransferOrchestration.IntegrationTests` | **177** | **169 meaningful integration** + 8 non-integration |
| `TransferOrchestration.ArchitectureTests` | **12** | **12 architecture** |
| **Solution total** | **240** | |

**Domain counting method:** Count each `[Fact]` and each `[Theory]` data row in `TransferOrchestration.Domain.Tests` that exercises domain or application invariants without PostgreSQL. Included: `AccountTests` (18), `TransferTests` (18), `TransferSubmissionStateTests` (1), `TransferProcessStateTests` (8), `TransferSubmissionFingerprintTests` (1 Fact + 5 Theory rows = 6). Excluded from the domain minimum: none required — all 51 cases are meaningful behavioral tests. **Pure domain aggregate (`.Domain.` namespace targets): 37 cases**, well above the challenge minimum of 10.

**Integration counting method:** Count integration-test-project cases that verify real component/infrastructure interaction. Included: all PostgreSQL-backed workflow/API/security/persistence tests (165+), plus focused non-PostgreSQL ACL/provider tests that prove required module behavior (`PaymentNetworkAclTests` 3, `LoggingNotificationProviderTests` 5). Excluded from integration minimum: `ReconciliationWorkflowTests.InvalidReconciliationConfigurationFailsFastOnStartup` (application options validation) and `PersistenceMappingTests.RepositoryAbstractionsDoNotExposeEntityFrameworkCoreTypes` (architecture — counted under architecture tests). **169 meaningful integration cases**, well above the minimum of 12.

#### Requirement-to-test matrix

| # | Required behavior | Test class / method | Real PostgreSQL |
|---|-------------------|---------------------|-----------------|
| 1 | Successful submission | `TransferSubmissionApiTests.SuccessfulSubmissionPersistsOneTransferAndProcessAndPropagatesCorrelation` | Yes |
| 2 | Idempotent replay | `TransferSubmissionApiTests.SameKeySamePayloadReplaysWithoutSideEffectsAndDifferentPayloadConflicts`; `PersistenceMappingTests.CompletedClaimReplaysOriginalTransferResult` | Yes |
| 3 | Same key / different payload conflict | Same as #2; `PersistenceMappingTests.SameKeyAndDifferentFingerprintReturnsConflictWithoutOverwrite` | Yes |
| 4 | Concurrent duplicate submission | `TransferSubmissionApiTests.ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess`; `PersistenceMappingTests.ConcurrentIdenticalClaimsProduceExactlyOneOwnerWithoutUniqueViolation` | Yes |
| 5 | Concurrent reservations | `AccountReservationContractTests.ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser` | Yes |
| 6 | Outbox retained on dispatch failure | `TransactionalOutboxTests.DispatchFailurePersistsRetryMetadata` | Yes |
| 7 | Outbox retry | `TransactionalOutboxTests.DurableNextAttemptBecomesEligibleWithoutSleeping` | Yes |
| 8 | Duplicate settlement idempotent | `ReconciliationWorkflowTests.DuplicateSettledStatusIsIdempotent` | Yes |
| 9 | Duplicate consumer one effect | `NotificationConsumerTests.DuplicateDeliveryCallsProviderOnceAndPersistsOneMarker` | Yes |
| 10 | Restart recovery | `AccountReservationContractTests.ProductionDispatcherExecutesPersistedReserveBalanceAndRestartRecoversDueWork`; `PersistenceMappingTests.DueProcessStateSurvivesNewApplicationScopeAndPreservesCoordinationMetadata`; `TransactionalOutboxTests.NewContextRediscoversPendingWork`; `ReconciliationWorkflowTests.RestartRediscoversDueReconciliationWork`; `PaymentSubmissionWorkflowTests.TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit` | Yes |
| 11 | Optimistic concurrency conflict | `PersistenceMappingTests.StaleAccountRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner`; `StaleTransferRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner` | Yes |
| 12 | Poison retry bounded | `TransactionalOutboxTests.PoisonMessageStopsAtConfiguredMaxAttempts` | Yes |
| 13 | Security tests green | `SecurityBoundaryTests` (16 cases) | Yes |
| 14 | Reconciliation tests green | `ReconciliationWorkflowTests` (15 cases) | Yes (14/15) |
| 15 | API tests green | `TransferSubmissionApiTests`, `TransferReadAndHealthApiTests`, `ManualOperationsTests` (37+ cases) | Yes |
| 16 | Architecture dependency rules | `TransferOrchestration.ArchitectureTests` (12 cases) | N/A |
| 17 | Negative architecture proof | Manual procedure below | N/A |

#### Architecture rules enforced (new in TASK-15)

| Rule | Test |
|------|------|
| Domain ↛ Infrastructure (TransferManagement) | `DomainLayerDependencyTests.TransferManagementDomainDoesNotReferenceInfrastructureTypes` |
| Domain ↛ Infrastructure (AccountBalance) | `DomainLayerDependencyTests.AccountBalanceDomainDoesNotReferenceInfrastructureTypes` |
| TransferManagement ↛ AccountBalance Infrastructure/DbContext | `TransferManagementAccountBalanceInfrastructureTests`; `AccountBalanceBoundaryTests` |
| TransferManagement ↛ PaymentNetwork beyond contracts | `PaymentNetworkBoundaryTests` |
| PaymentNetwork ↛ TransferManagement | `PaymentNetworkBoundaryTests.PaymentNetworkDoesNotReferenceTransferManagementAssembly` |
| Notification ↛ Transfer persistence | `NotificationBoundaryTests` (2 tests) |
| BuildingBlocks dependency-light | `BuildingBlocksDependencyTests` (2 tests) |
| API composition root | `ApiCompositionRootTests` (2 tests) |
| Repository abstractions hide EF Core | `PersistenceMappingTests.RepositoryAbstractionsDoNotExposeEntityFrameworkCoreTypes` |

#### Negative architecture proof

1. **Temporary violation:** Added `src/Modules/TransferManagement/ArchitectureNegativeProofFixture.cs` with a primary constructor parameter of type `AccountBalanceDbContext` (must compile under TreatWarningsAsErrors — unused fields/parameters fail the build).
2. **Focused command:** `dotnet test tests/TransferOrchestration.ArchitectureTests/TransferOrchestration.ArchitectureTests.csproj --filter "FullyQualifiedName~TransferManagementAccountBalanceInfrastructureTests" --verbosity normal`
3. **Expected failure:** `TransferOrchestration.TransferManagement.ArchitectureNegativeProofFixture must not reference TransferOrchestration.AccountBalance.Infrastructure.Persistence.AccountBalanceDbContext.`
4. **Reverted:** Temporary file deleted; `git status` clean of forbidden dependency.
5. **Final pass:** All 12 architecture tests passed after revert.

#### Build and test summary

```
dotnet restore TransferOrchestrationPlatform.sln   => succeeded
dotnet build TransferOrchestrationPlatform.sln --no-restore => 0 warnings, 0 errors
dotnet test TransferOrchestrationPlatform.sln --no-build => Passed: 240, Failed: 0, Skipped: 0
  Domain.Tests:        51 passed
  IntegrationTests:   177 passed
  ArchitectureTests:   12 passed
```

PostgreSQL integration suite repeated in fresh processes: **177 passed, 0 failed** on each of two consecutive runs.

#### TASK-15 code changes

- Added architecture enforcement tests under `tests/TransferOrchestration.ArchitectureTests/` (`ArchitectureTestHelpers`, `DomainLayerDependencyTests`, `NotificationBoundaryTests`, `BuildingBlocksDependencyTests`, `ApiCompositionRootTests`, `TransferManagementAccountBalanceInfrastructureTests`).
- Fixed `ApiCompositionRootTests` for Linux CI by resolving repository paths with `Path.Combine` segments and normalizing backslash `ProjectReference` include paths before extracting the project filename.
- Consolidated duplicate signature-scan helpers in `AccountBalanceBoundaryTests` and `PaymentNetworkBoundaryTests` onto `ArchitectureTestHelpers`.
- No production-code blocker fixes were required; behavioral coverage from TASK-01 through TASK-14 was already sufficient.

#### Self-review findings

| Finding | Classification |
|---------|----------------|
| `TransferProcessStateTests` and `TransferSubmissionFingerprintTests` live in Domain.Tests but target Application layer | Non-blocking improvement |
| Reconciliation placeholder assembly is referenced by API csproj but not loaded at runtime until code is added | Non-blocking improvement |
| Formal inventory could be extracted to a separate doc for TASK-18 submission gate | Preference |

**TASK-16 not implemented:** No Docker Compose, CI, or `.env.example` changes were made.

## 11. Handoff to the Next Task

TASK-16 hardens runtime reproducibility and CI.
