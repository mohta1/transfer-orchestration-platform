# Transactional Outbox and Integration Event Pipeline

**Task ID:** TASK-09
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/transactional-outbox`
**Depends on:** TASK-08
**Status:** Not Started

---

## 1. Objective

Implement a PostgreSQL Transactional Outbox and durable BackgroundService dispatcher with at-least-once semantics.

## 2. Why This Task Exists

The challenge mandates Transactional Outbox, durable background processing, and no exactly-once claims.

## 3. Scope

### In Scope
- OutboxMessage model/table.
- Domain-event to integration-event mapping.
- TransferCompleted integration event.
- Atomic business state + Outbox insert.
- BackgroundService dispatcher.
- Bounded polling.
- Safe row claiming / lease behavior.
- Retry/backoff metadata.
- Poison/dead-letter state.

### Out of Scope
- Kafka/RabbitMQ.
- Exactly-once delivery.
- Consumer dedupe.
- Notification implementation.

## 4. Required Deliverables

- OutboxMessage model/table.
- Domain-event to integration-event mapping.
- TransferCompleted integration event.
- Atomic business state + Outbox insert.
- BackgroundService dispatcher.
- Bounded polling.
- Safe row claiming / lease behavior.
- Retry/backoff metadata.
- Poison/dead-letter state.

## 5. Implementation Requirements

- Business state and Outbox share one local DB transaction.
- Dispatch failure leaves message recoverable.
- Multi-instance workers cannot simultaneously own the same lease.
- Duplicate delivery after crash is accepted by design.
- No fire-and-forget work.

## 6. Required Tests

- Completed Transfer creates Outbox row atomically.
- Dispatch failure retains retryable row.
- Retry eventually dispatches.
- Restart rediscovers pending Outbox.
- Two workers do not simultaneously process same lease.
- Poison message reaches configured terminal state.

## 7. Verification Procedure

1. Run PostgreSQL integration tests including restart and worker-concurrency scenarios.
2. Inspect Outbox row lifecycle.

## 8. Acceptance Criteria

- [ ] 0 lost committed events.
- [ ] At-least-once semantics are explicit.
- [ ] Failure/restart recovery proven.
- [ ] No broker introduced.

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

- Outbox lifecycle rows.
- Worker logs with MessageId, TransferId, CorrelationId.

## 11. Handoff to the Next Task

TASK-10 adds durable idempotent consumers and Notification.
