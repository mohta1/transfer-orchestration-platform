# Idempotent Consumers, Notification, and Processed Messages

**Task ID:** TASK-10
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/idempotent-consumers`
**Depends on:** TASK-09
**Status:** Done

---

## 1. Objective

Implement durable consumer-side deduplication and a Notification consumer so duplicate Outbox delivery cannot duplicate downstream effects.

## 2. Why This Task Exists

At-least-once delivery necessarily permits duplicates; consumers must therefore be idempotent.

## 3. Scope

### In Scope
- ProcessedMessage model/migration.
- Unique (MessageId, ConsumerName).
- Consumer dedupe transaction pattern.
- TransferCompleted Notification consumer.
- Notification provider abstraction/fake.
- Concurrent duplicate handling.

### Out of Scope
- Real email/SMS provider.
- Broker.
- Reconciliation.

## 4. Required Deliverables

- ProcessedMessage model/migration.
- Unique (MessageId, ConsumerName).
- Consumer dedupe transaction pattern.
- TransferCompleted Notification consumer.
- Notification provider abstraction/fake.
- Concurrent duplicate handling.

## 5. Implementation Requirements

- Dedupe is durable, not in-memory.
- Same event produces one effect per consumer.
- Different consumer names process independently.
- Failure before completion permits retry.

## 6. Required Tests

- Duplicate MessageId => one notification.
- Same MessageId different consumer name can process.
- Provider failure permits later retry.
- Successful processing records ProcessedMessage.
- Concurrent duplicate delivery => one effect.

## 7. Verification Procedure

1. Run tests against PostgreSQL.
2. Assert fake Notification provider call count.

## 8. Acceptance Criteria

- [x] Duplicate delivery has one effect.
- [x] Durable processed marker exists.
- [x] Retry after failure works.

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

- ProcessedMessages row.
- Fake provider call count for duplicate delivery.

Captured on 2026-08-12 against PostgreSQL through `TEST_DATABASE_CONNECTION_STRING`:

- Sequential and concurrent duplicate tests each passed with one actual provider invocation, one durable
  provider effect, and one `ProcessedMessage` row.
- Provider-success/local-save-failure, provider-success/local-commit-failure, and post-provider-cancellation
  tests passed; retries reused the provider key and retained one durable effect before committing one marker.
- `NotificationConsumerTests` passed: 11 passed, 0 failed, 0 skipped.
- The complete PostgreSQL integration suite passed twice in fresh test processes: 119 passed, 0 failed,
  0 skipped on each run.
- `dotnet restore TransferOrchestrationPlatform.sln`,
  `dotnet build TransferOrchestrationPlatform.sln --no-restore`, and
  `dotnet test TransferOrchestrationPlatform.sln --no-build` passed; the build reported 0 warnings and
  0 errors, and the solution reported 169 passed, 0 failed, 0 skipped.
- Reliability follow-up validation added a bounded logging-provider cache (10,000-entry/one-hour defaults)
  and leased durable consumer claims so provider I/O runs outside database transactions. The Notification
  tests passed (11 PostgreSQL consumer tests and 5 bounded-provider tests), the PostgreSQL suite passed
  twice with 124 tests, and the full solution passed 174 tests with 0 failures or skips.

## 11. Handoff to the Next Task

TASK-11 implements Reconciliation and ManualReview escalation.
