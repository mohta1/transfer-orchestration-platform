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

- [ ] Duplicate delivery has one effect.
- [ ] Durable processed marker exists.
- [ ] Retry after failure works.

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

- ProcessedMessages row.
- Fake provider call count for duplicate delivery.

## 11. Handoff to the Next Task

TASK-11 implements Reconciliation and ManualReview escalation.
