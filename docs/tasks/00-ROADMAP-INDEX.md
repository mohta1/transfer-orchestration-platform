# Transfer Orchestration Platform — Execution Roadmap

This roadmap defines the remaining path from the current Transfer-domain baseline to a submission-ready backend engineering challenge.

> **Execution rule:** Do not start the next task until the current task passes its Required Tests, Verification Procedure, Acceptance Criteria, and Definition of Done.

## Status Model

Use only:
- Not Started
- In Progress
- Blocked
- Done

## Review Finding Classification

Every review finding must be classified as:
1. **Blocker** — an explicit challenge requirement is unmet, or implementation contradicts a locked architectural decision.
2. **Non-blocking improvement** — useful but not required to proceed.
3. **Preference** — stylistic or alternative design with no correctness impact.

Only a **Blocker** stops progression or reopens a completed baseline.

## Global Quality Gates

Every implementation task must preserve:

```text
dotnet build TransferOrchestrationPlatform.sln
=> 0 warnings
=> 0 errors
```

After affected tests exist:

```text
dotnet test TransferOrchestrationPlatform.sln
=> all tests pass
```

Concurrency, restart, migration, Outbox, database constraints, and consumer deduplication must use **real PostgreSQL**, not EF Core InMemory.

## Roadmap

| Task | Title | Stage | Recommended Branch | Depends On | File |
|---|---|---|---|---|---|
| TASK-01 | Account Balance Domain and Reservation | Stage 1 — Domain & Persistence | `feature/account-balance-domain` | Transfer domain model baseline | [Open](./TASK-01-account-balance-domain.md) |
| TASK-02 | EF Core Mapping, Database Constraints, and Initial Migrations | Stage 1 — Domain & Persistence | `feature/ef-mapping-migrations` | TASK-01 | [Open](./TASK-02-ef-mapping-migrations.md) |
| TASK-03 | Persistence Repositories and Optimistic Concurrency Handling | Stage 1 — Domain & Persistence | `feature/persistence-repositories` | TASK-02 | [Open](./TASK-03-persistence-repositories.md) |
| TASK-04 | HTTP Idempotency Foundation | Stage 2 — Submission & Coordination | `feature/http-idempotency` | TASK-03 | [Open](./TASK-04-http-idempotency.md) |
| TASK-05 | Transfer Process Manager and Durable Workflow State | Stage 2 — Submission & Coordination | `feature/transfer-process-manager` | TASK-04 | [Open](./TASK-05-transfer-process-manager.md) |
| TASK-06 | Submission Vertical Slice: Validation, Authorization, Daily Limit, and Fraud | Stage 2 — Submission & Coordination | `feature/submission-vertical-slice` | TASK-05 | [Open](./TASK-06-submission-vertical-slice.md) |
| TASK-07 | Account Reservation Module Contract and Genuine Concurrency | Stage 2 — Submission & Coordination | `feature/account-reservation-contract` | TASK-06 | [Open](./TASK-07-account-reservation-contract.md) |
| TASK-08 | Payment Network ACL and External Submission Semantics | Stage 2 — Submission & Coordination | `feature/payment-network-acl` | TASK-07 | [Open](./TASK-08-payment-network-acl.md) |
| TASK-09 | Transactional Outbox and Integration Event Pipeline | Stage 3 — Reliability & Operations | `feature/transactional-outbox` | TASK-08 | [Open](./TASK-09-transactional-outbox.md) |
| TASK-10 | Idempotent Consumers, Notification, and Processed Messages | Stage 3 — Reliability & Operations | `feature/idempotent-consumers` | TASK-09 | [Open](./TASK-10-idempotent-consumers.md) |
| TASK-11 | Reconciliation, Unknown Outcomes, and Manual Review | Stage 3 — Reliability & Operations | `feature/reconciliation` | TASK-10 | [Open](./TASK-11-reconciliation.md) |
| TASK-12 | Audit, Operations, Correlation, and Manual Commands | Stage 3 — Reliability & Operations | `feature/audit-operations` | TASK-11 | [Open](./TASK-12-audit-operations.md) |
| TASK-13 | Read API, Error Semantics, and Health/Readiness | Stage 3 — Reliability & Operations | `feature/read-api-health` | TASK-12 | [Open](./TASK-13-read-api-health.md) |
| TASK-14 | Security Boundary and Authorization Policy | Stage 3 — Reliability & Operations | `feature/security-boundary` | TASK-13 | [Open](./TASK-14-security-boundary.md) |
| TASK-15 | Integration Test Suite and Architecture Enforcement | Stage 4 — Verification & Delivery | `feature/test-hardening` | TASK-14 | [Open](./TASK-15-test-hardening.md) |
| TASK-16 | Runtime Hardening, Docker Compose, and CI | Stage 4 — Verification & Delivery | `feature/runtime-ci` | TASK-15 | [Open](./TASK-16-runtime-ci.md) |
| TASK-17 | Engineering Delivery Documentation | Stage 4 — Verification & Delivery | `docs/engineering-delivery` | TASK-16 | [Open](./TASK-17-engineering-delivery.md) |
| TASK-18 | Final Review, README, Demo Path, and Submission Gate | Stage 4 — Verification & Delivery | `release/final-challenge-review` | TASK-17 | [Open](./TASK-18-final-challenge-review.md) |

## Stage Summary

### Stage 1 — Domain & Persistence
TASK-01 to TASK-03 establish Account financial invariants, PostgreSQL mapping/constraints, repositories, and optimistic concurrency.

### Stage 2 — Submission & Coordination
TASK-04 to TASK-08 implement HTTP idempotency, durable Process Manager state, the POST vertical slice, safe Account reservation, and Payment Network timeout semantics.

### Stage 3 — Reliability & Operations
TASK-09 to TASK-14 implement Transactional Outbox, idempotent consumers, reconciliation, audit/operations, API read/health semantics, and security.

### Stage 4 — Verification & Delivery
TASK-15 to TASK-18 complete mandatory tests, architecture enforcement, CI/runtime reproducibility, engineering documentation, and final submission validation.

## Mandatory Challenge Evidence Covered by the Roadmap

By the end of TASK-18, the repository must demonstrate:

- Modular Monolith without unnecessary distribution.
- Persistent Process Manager.
- Transfer state machine with invalid-transition protection.
- Account balance reservation invariants.
- Genuine PostgreSQL optimistic-concurrency behavior.
- Durable HTTP idempotency.
- Payment timeout != rejection.
- No blind external resubmission.
- Transactional Outbox.
- Durable background processing.
- At-least-once delivery.
- Idempotent consumers.
- Reconciliation and Manual Review.
- Auditable manual operations.
- Correlation-aware observability.
- Authentication/authorization boundary.
- At least 10 meaningful domain tests.
- At least 12 meaningful integration tests.
- Exactly five ADRs.
- All eight mandatory diagrams.
- Legacy-modernization documentation.
- Docker-based local runtime.
- Clean CI/build/test evidence.
- Reviewer-friendly README and demo path.

## Execution Procedure for Every Task

1. Open the current task file.
2. Change Status to `In Progress`.
3. Create the recommended branch from updated `main`.
4. Implement only the In Scope section.
5. Add every Required Test during the task.
6. Run the Verification Procedure.
7. Check every Acceptance Criterion.
8. Capture the requested Evidence.
9. Mark the task `Done`.
10. Merge the branch.
11. Start the next task.
