# ADR-004: Reliable Messaging and Transactional Outbox Strategy

- **Status:** Accepted
- **Decision Date:** 2026-08-07
- **Decision Owners:** Backend Engineering / Architecture
- **Scope:** Reliable Integration Event publication and durable asynchronous processing

## Context

The platform must guarantee that committed financial/business outcomes are not lost merely because asynchronous publication fails.

A critical example is Transfer completion.

The system must not perform:

1. commit `Transfer = Completed`;
2. attempt to publish `TransferCompleted`;
3. lose the event forever if the application crashes or the messaging dependency is unavailable between those steps.

The Event Storming and domain baseline establish that:

- completed Transfers must eventually emit an Integration Event;
- failed publication must not lose an already committed business result;
- delivery is at-least-once;
- downstream consumers must tolerate duplicate delivery;
- duplicate messages must not create duplicate downstream effects;
- Notification is asynchronous and outside the critical financial transaction;
- durable work must survive application restart;
- fire-and-forget in-memory processing is insufficient.

The initial architecture is a Modular Monolith backed by PostgreSQL. Multiple production-ready Microservices and a dedicated message broker are not required for the challenge.

The reliability mechanism should therefore solve the actual failure modes while avoiding infrastructure that does not yet provide enough business value.

## Decision

Use a **PostgreSQL-backed Transactional Outbox** with **durable database-backed background processing** implemented using .NET hosted background workers.

Initial delivery topology:

- no Kafka;
- no RabbitMQ;
- no Redis-backed queue;
- no Hangfire;
- no MassTransit requirement;
- no claim of end-to-end exactly-once delivery.

The system uses:

- local database transactions;
- Outbox records;
- durable polling/claiming;
- bounded retry with backoff and jitter;
- dead-letter/failed state;
- at-least-once delivery semantics;
- idempotent consumers;
- durable processed-message deduplication.

A broker may be introduced later without removing the Transactional Outbox if independent deployment or scaling requirements justify it.

## Reliability Model

The reliable publication flow is:

1. perform the relevant local business state change;
2. create the corresponding Outbox Message in the same database transaction;
3. commit both atomically;
4. a durable Outbox worker discovers pending messages;
5. the worker safely claims a batch;
6. the Integration Event is dispatched;
7. successful dispatch updates Outbox delivery state;
8. failure is persisted and retried later according to policy.

This provides atomicity between the committed local result and the durable intent to publish.

It does not provide exactly-once delivery.

## Transaction Boundary

The critical rule is:

> The committed business-state change and the Outbox Message representing the resulting Integration Event must be persisted in the same local database transaction.

Example:

For Transfer completion, one transaction persists:

- Transfer state/result required for `Completed`;
- the `TransferCompleted` Outbox Message.

If the transaction rolls back:

- neither the business-state change nor the Outbox Message becomes visible.

If the transaction commits:

- both are durable.

Publication occurs only after commit.

This prevents the "business commit succeeded but event disappeared" failure mode.

## Domain Event vs Integration Event

Domain Events and Integration Events remain separate concepts.

### Domain Event

Represents a fact inside the owning Bounded Context.

Examples may include:

- `TransferSettled`
- `BalanceReserved`
- `ReservationConsumed`

### Integration Event

Represents a stable event intended to cross the owning Context boundary.

Example:

- `TransferCompleted`

A Domain Event may cause creation of an Integration Event, but not every Domain Event must leave the Context.

The Outbox persists Integration Events or durable publication envelopes, not arbitrary internal object graphs.

## Outbox Message Model

Conceptually, an Outbox record contains:

- `Id`
- `MessageId`
- `Type`
- `Payload`
- `OccurredAt`
- `Status`
- `Attempts`
- `NextAttemptAt`
- `LockedBy`
- `LockedUntil`
- `PublishedAt`
- `LastError`
- `CorrelationId`
- `CausationId`

Exact naming may be refined during implementation.

### Message Identifier

Every Outbox Message has a stable unique `MessageId`.

The same logical Integration Event must retain the same `MessageId` across retries.

A retry is another delivery attempt of the same message, not creation of a new logical event.

## Message Payload

Integration Event payloads must:

- contain only information needed by consumers;
- avoid leaking sensitive customer/account data unnecessarily;
- use stable contract-oriented fields;
- be serialisable independently of internal EF/domain object shape;
- include stable identifiers needed for correlation;
- support version evolution when required.

For example, a `TransferCompleted` event may include:

- MessageId
- TransferId
- completed timestamp
- transfer type
- safe correlation metadata

It should not include secrets, credentials, raw authentication tokens, or unnecessary PII.

## Durable Background Worker

A .NET hosted `BackgroundService` is used to process pending Outbox records.

The worker is considered durable because the work itself is stored in PostgreSQL before execution.

Durability does not come from keeping work in memory.

If the application stops:

- pending Outbox rows remain in the database;
- failed attempts remain recorded;
- `NextAttemptAt` remains persisted;
- work can resume after restart.

The worker must not depend on an in-memory queue as the only source of pending work.

## Polling

The worker periodically queries for Outbox Messages that are eligible for processing.

Eligible messages are conceptually:

- `Pending`, or retryable `Failed`;
- `NextAttemptAt <= now`;
- not currently held by a valid lease/claim.

Polling frequency is configuration, not a business invariant.

The implementation must avoid:

- tight polling loops;
- busy waiting;
- unbounded batch sizes.

## Safe Message Claiming

Multiple application instances may run simultaneously.

Therefore multiple Outbox workers may poll the same database.

They must not intentionally process the same row concurrently.

For PostgreSQL, the preferred initial claiming mechanism is a short database transaction using row-level locking semantics such as:

`FOR UPDATE SKIP LOCKED`

Conceptual flow:

1. begin short claim transaction;
2. select a bounded batch of eligible rows;
3. skip rows already locked by another worker;
4. mark/lease selected rows to the current worker;
5. commit the claim;
6. perform publication outside the long-held database row lock.

The database claim must remain short.

Slow external/provider work must not happen while a database row lock is unnecessarily held.

## Claim / Lease Semantics

Outbox ownership is temporary.

A claim may record:

- `LockedBy`
- `LockedUntil`

If a worker crashes after claiming but before completion:

- the lease eventually expires;
- another worker can reclaim the message;
- the message may therefore be delivered more than once.

This is expected and reinforces the at-least-once model.

Exact lease duration is configuration and must exceed normal expected dispatch time while still allowing timely recovery from crashed workers.

## Publication Semantics

Initial publication may dispatch to an in-process integration-event dispatcher / consumer boundary because the initial architecture is one Modular Monolith.

This does not mean publication is fire-and-forget.

The Outbox row is still the durable source of pending asynchronous work.

The implementation must preserve the abstraction that an Integration Event crosses an explicit contract boundary.

A future broker transport can replace the in-process transport while preserving:

- Outbox persistence;
- MessageId;
- retry semantics;
- consumer idempotency.

## At-Least-Once Delivery

The system explicitly guarantees **at-least-once delivery attempts**, not end-to-end exactly-once processing.

Example failure:

1. worker dispatches `TransferCompleted`;
2. consumer/provider successfully performs its effect;
3. application crashes before Outbox row is marked `Published`;
4. after restart the same message becomes eligible again;
5. the same message may be delivered again.

Therefore duplicate delivery is a normal operating condition.

Exactly-once must not be claimed.

## Idempotent Consumers

Consumers must prevent duplicate messages from creating duplicate business effects.

Each consumer maintains durable processed-message state.

Conceptually:

`ProcessedMessages`

contains:

- `MessageId`
- `ConsumerName`
- `ProcessedAt`

The database enforces uniqueness on:

`(MessageId, ConsumerName)`

### Consumer Flow

A consumer:

1. begins its local transaction where appropriate;
2. attempts to register `(MessageId, ConsumerName)`;
3. if already present, treats the message as duplicate and does not repeat the downstream business effect;
4. if new, performs the consumer operation;
5. commits processed-message state together with the local consumer-side state change when atomicity is required.

The consumer must not rely only on "current status looks completed" as the sole deduplication mechanism.

## Notification Consumer

Notification is the canonical non-critical downstream example.

Flow:

`TransferCompleted -> Notification Consumer -> Notification Provider`

If Notification processing fails:

- Transfer remains `Completed`;
- notification work is retried independently;
- the Transfer financial result is not rolled back;
- duplicate event delivery must not send duplicate notifications for the same logical consumer operation.

Provider-side idempotency should be used if supported, but internal consumer deduplication remains required.

## Retry Strategy

Retries are durable and operation-specific.

For Outbox dispatch failure:

- increment `Attempts`;
- store failure classification / safe error information;
- compute `NextAttemptAt`;
- release/expire the worker claim;
- retry later.

Retry uses bounded exponential backoff with jitter.

Conceptually:

- early failures retry relatively soon;
- repeated failures retry less frequently;
- jitter prevents multiple instances from retrying at exactly the same instant.

Exact values remain configuration.

The implementation must avoid tight retry loops.

## Retryable vs Non-Retryable Failure

Not every failure should be retried identically.

Examples of retryable failures may include:

- temporary dependency/network unavailability;
- transient provider failure;
- temporary dispatch infrastructure failure.

Examples that may be non-retryable without human/configuration intervention:

- invalid message contract;
- permanently malformed payload;
- unsupported event type;
- deterministic validation failure caused by corrupt data.

Failure classification should be observable and should influence Dead Letter behaviour.

## Poison Messages and Dead Letter State

A message that repeatedly fails and exceeds the configured retry policy becomes a failed/dead-letter item.

Conceptual terminal status:

- `DeadLetter`
- or `FailedPermanent`

The record remains durable.

It must retain sufficient diagnostic data, including:

- MessageId
- event type
- safe payload/reference
- Attempts
- LastError
- first/last failure timestamp
- CorrelationId

Operations must be able to identify these records.

A poison message must not be silently discarded.

## Manual Recovery

Operations may need to retry or resolve a Dead Letter item.

Manual recovery must:

- be explicit;
- be authorised;
- be auditable;
- avoid changing the original logical `MessageId` merely to bypass deduplication;
- preserve failure history.

Exact operational UI/endpoints are outside the initial slice unless needed, but the persistence model must support investigation and recovery.

## Broker Outage

No external broker exists in the initial implementation.

However, the architecture is deliberately compatible with a future broker.

If a broker is added later and becomes unavailable:

- local business transaction can still commit with its Outbox Message;
- the Outbox backlog grows;
- the publisher retries later;
- committed financial results remain durable;
- backlog size/age is observable.

This behaviour is one reason the Transactional Outbox remains valuable even after a broker is introduced.

## Application Restart

Restart recovery is automatic from persisted state.

After restart:

- pending Outbox rows remain pending;
- expired claims become reclaimable;
- retry counters and `NextAttemptAt` remain available;
- processed-message deduplication state remains available.

The application must not recreate Integration Events from guesswork when durable Outbox state already exists.

## Multiple Workers

The system supports more than one worker/application instance.

Workers:

- select bounded batches;
- skip already claimed rows;
- avoid holding long database locks;
- independently dispatch different messages;
- may reprocess a message after claim expiration/crash.

At-least-once semantics and consumer idempotency make worker restart/reclaim safe.

## Ordering

Global ordering is not guaranteed.

The system should avoid designing consumers that require global ordering of all Integration Events.

If ordering for one Transfer becomes necessary:

- use Transfer-specific sequencing/version metadata;
- enforce consumer logic for that Aggregate/process;
- avoid assuming database polling order equals business ordering.

For the initial `TransferCompleted` notification scenario, global ordering is unnecessary.

## Transaction Size and Batch Size

Outbox creation happens inside the business transaction, but publication does not.

Business transactions remain short.

Worker claim batches must be bounded.

Large batches must not:

- hold excessive locks;
- monopolise worker execution;
- create very large transactions.

Batch size is operational configuration.

## Observability

Reliable messaging must be diagnosable.

Structured logs should include safe fields such as:

- MessageId
- event type
- TransferId where applicable
- CorrelationId
- CausationId
- attempt number
- worker instance
- current status
- NextAttemptAt
- failure category
- processing duration

Suggested metrics:

- pending Outbox count;
- oldest pending Outbox age;
- publication success rate;
- publication failure rate;
- retry count;
- Dead Letter count;
- claim duration;
- consumer duplicate count;
- consumer processing failures.

Alerts should consider:

- rapidly growing backlog;
- old pending messages;
- repeated publication failure;
- Dead Letter growth.

Sensitive payload content must not be logged by default.

## Why No Message Broker Initially

A broker is not required to meet the initial challenge requirements.

The architecture is a Modular Monolith and the primary asynchronous consumer is inside the same deployment boundary.

Adding RabbitMQ or Kafka initially would add:

- another runtime dependency;
- connection/recovery configuration;
- broker deployment/health concerns;
- credentials and network security;
- queue/topic topology;
- broker-specific retry/dead-letter design;
- local-development complexity;
- additional failure modes.

These costs do not currently provide enough business value.

PostgreSQL is already required for durable business state and can also durably represent pending Outbox work.

## Why RabbitMQ Was Not Selected Initially

RabbitMQ would be reasonable if the architecture required:

- independently deployed consumers;
- queue-oriented routing;
- service isolation;
- independent consumer scaling;
- broker-level delivery topology.

Those requirements are not currently strong enough.

RabbitMQ remains a future transport option.

## Why Kafka Was Not Selected Initially

Kafka is strongest for:

- high-throughput event streaming;
- durable event-log retention;
- replay;
- many independent consumer groups;
- streaming/data-platform use cases.

The challenge's initial critical slice does not justify this complexity.

Kafka remains unnecessary unless future event-streaming requirements emerge.

## Why Hangfire / External Job Framework Was Not Selected

A job framework could provide useful scheduling/monitoring features but would introduce another abstraction and persistence model.

The challenge specifically requires us to demonstrate understanding of:

- durability;
- claiming;
- retry;
- restart recovery;
- idempotency.

A small explicit PostgreSQL-backed worker makes these engineering choices visible and reviewable.

A job framework may be reconsidered if general-purpose job scheduling becomes a broader platform requirement.

## Relationship to Process Manager

The Process Manager selected in ADR-002 owns workflow coordination.

The Outbox does not decide the next Transfer business step.

The Outbox only solves durable asynchronous publication/dispatch.

Responsibilities remain separate:

- Process Manager -> workflow next action;
- Transfer Aggregate -> lifecycle invariants;
- Account Aggregate -> financial invariants;
- Outbox -> reliable async publication;
- consumer -> idempotent downstream effect.

## Relationship to Concurrency Strategy

ADR-003 selects optimistic concurrency for business Aggregate mutation.

Outbox row claiming is intentionally different.

Short database row locking / `SKIP LOCKED` is used for worker coordination because:

- workers are competing for queue-like records;
- the lock is short;
- the operation is infrastructure claiming, not a long financial transaction.

This does not change the default Aggregate concurrency strategy.

## Testing Implications

### Integration Test — Business Commit + Outbox Atomicity

Given a Transfer reaches completion:

Assert that:

- Transfer completion state is persisted;
- exactly one logical Outbox Message exists for `TransferCompleted`;
- both are committed atomically.

A forced transaction failure must persist neither partial outcome.

### Integration Test — Publisher Failure

Simulate dispatch failure.

Assert:

- Outbox Message remains durable;
- Attempts increases;
- LastError/failure data is persisted;
- NextAttemptAt is scheduled;
- business state remains committed.

### Integration Test — Restart Recovery

Create a pending Outbox Message.

Restart/recreate application processing.

Assert the pending record is rediscovered and processed without manual recreation.

### Integration Test — Duplicate Consumer Delivery

Deliver the same `MessageId` twice to the same consumer.

Assert:

- only one downstream business effect occurs;
- Processed Message uniqueness prevents duplicate effect.

### Integration Test — Competing Workers

Run two worker instances/processors concurrently against a set of pending Outbox Messages.

Assert:

- work is claimed safely;
- one row is not intentionally processed concurrently by both workers;
- all eligible messages eventually become processed/published.

### Integration Test — Claim Crash / Lease Recovery

Claim a message and simulate worker failure before completion.

After lease expiry:

Assert another worker can reclaim the message.

Duplicate delivery remains safe due to consumer idempotency.

### Integration Test — Poison Message

Cause deterministic repeated processing failure.

Assert:

- retries are bounded;
- attempts/backoff are persisted;
- message eventually moves to Dead Letter/terminal failure state;
- it remains visible for operations.

## Consequences

### Positive Consequences

- committed business results cannot lose their publication intent;
- asynchronous work survives application restart;
- no external broker is required initially;
- worker behaviour is explicit and testable;
- multiple workers can safely process the backlog;
- the design naturally supports at-least-once delivery;
- consumer idempotency is explicit;
- future broker introduction remains possible;
- failure/retry state is operationally visible.

### Negative Consequences

- PostgreSQL also carries queue-like polling workload;
- custom worker/claim/retry code must be implemented correctly;
- polling introduces some delivery latency;
- operational tooling is less feature-rich than mature broker/job platforms;
- duplicate delivery remains possible by design;
- Dead Letter investigation tooling must be provided at least minimally.

## Risks

### Database Polling Load

Excessive polling may create unnecessary database load.

**Mitigation:** bounded batches, appropriate polling interval, indexes, `NextAttemptAt`, and measurement.

### Duplicate Delivery

Worker crash after successful dispatch may cause redispatch.

**Mitigation:** stable MessageId and idempotent consumers.

### Stuck Claims

A crashed worker may leave rows apparently owned.

**Mitigation:** lease expiry via `LockedUntil`.

### Poison Message Retry Loop

Malformed messages may retry forever.

**Mitigation:** bounded retry and Dead Letter state.

### Outbox Backlog Growth

Dependency outage may cause backlog to grow.

**Mitigation:** backlog metrics/alerts, retry/backoff, operational inspection.

### Payload Contract Evolution

Stored messages may outlive the producing application version.

**Mitigation:** version event contracts when necessary and maintain backwards-compatible deserialisation for in-flight records.

### Shared Database Coupling

Other modules may be tempted to read Outbox tables directly.

**Mitigation:** Outbox ownership remains infrastructure/internal to the publishing boundary; consumers receive explicit Integration Event contracts.

## Revisit Conditions

Revisit the no-broker decision when:

- Notification or another consumer becomes independently deployed;
- multiple independent services require the same Integration Events;
- independent consumer scaling becomes material;
- database polling load becomes operationally expensive;
- routing/fan-out requirements become complex;
- cross-system delivery guarantees require a broker;
- platform standards mandate an approved broker;
- replay/stream-retention requirements emerge;
- event throughput materially exceeds the comfortable database-backed worker model.

Possible future transports include RabbitMQ or Kafka depending on the actual requirements.

Introducing a broker does not remove the need for the Transactional Outbox unless an alternative atomic business-state/publication mechanism is demonstrated.

## Related Decisions

- **ADR-001:** Architecture Style
- **ADR-002:** Transfer Process Coordination Strategy
- **ADR-003:** Account Reservation and Concurrency Strategy
- **ADR-005:** Incremental Legacy Modernisation Strategy
