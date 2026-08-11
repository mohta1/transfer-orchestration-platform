# Transactional Outbox and Integration Event Pipeline

**Task ID:** TASK-09
**Stage:** Stage 3 — Reliability & Operations
**Recommended branch:** `feature/transactional-outbox`
**Depends on:** TASK-08
**Status:** Done

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

- [x] 0 lost committed events.
- [x] At-least-once semantics are explicit.
- [x] Failure/restart recovery proven.
- [x] No broker introduced.

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

- Outbox lifecycle rows.
- Worker logs with MessageId, TransferId, CorrelationId.

Implementation evidence: 19 focused PostgreSQL tests cover atomic capture, retry,
restart, competing claims, stale-owner fencing, the expected at-least-once crash
window, poison work, and safe migration downgrade. TASK-08 made the InternalBank
completion path reachable before TASK-09, so the forward migration safely creates
pending `transfer.completed.v1` rows for any historical Completed transfers.
CorrelationId is not present on the Transfer aggregate and is therefore not
invented in the event contract or logs.

## 11. Handoff to the Next Task

TASK-10 adds durable idempotent consumers and Notification.
