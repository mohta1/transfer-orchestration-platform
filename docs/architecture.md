# Architecture Document — Resilient Interbank Transfer Orchestration Platform

**Status:** Architecture baseline  
**Date:** 2026-08-07  
**Target stack:** .NET 8, ASP.NET Core, EF Core, PostgreSQL, Docker Compose  
**Primary implementation focus:** Domestic Interbank Transfer vertical slice

This document is the concise architecture overview required by the challenge. Detailed decisions remain in the five ADRs; detailed domain vocabulary, Event Storming findings, migration roadmap, and editable diagrams remain in their dedicated files.

Architecture targets in this document are **challenge assumptions/engineering targets**, not claimed production banking SLAs. Production values require agreement with Product, Operations, Security, and the actual external-system owners.

---

## 1. Business Understanding

A Transfer is a long-running financial workflow, not a CRUD record.

The business flow coordinates request validation, customer/account authorisation, Daily Transfer Limit evaluation, Fraud Screening, Balance Reservation, routing by Transfer Type, external Payment Network submission for Domestic Interbank Transfers, Settlement tracking, Reservation consumption or release, customer Notification, and Reconciliation/manual Operations when automated processing cannot safely determine an outcome.

Two Transfer Types matter:

- **Internal Bank Transfer** — destination is another account in the same bank; it shares validation, authorisation, limit, fraud, and reservation rules but bypasses the external domestic Payment Network.
- **Domestic Interbank Transfer** — destination is another domestic bank and therefore requires external submission, ambiguous-timeout handling, Settlement, and Reconciliation.

The architecture optimises first for financial correctness, recoverability, clear ownership, explicit failure handling, and incremental Legacy migration.

---

## 2. Assumptions

The following are implementation assumptions rather than confirmed banking policies:

- PostgreSQL is the relational persistence store.
- .NET 8, ASP.NET Core, EF Core, and Docker Compose are used.
- The initial new capability is one deployable Modular Monolith.
- Customer Authorisation, Fraud, Payment Network, Notification Provider, and complete authentication can be represented by explicit adapters/stubs for the challenge.
- Optimistic concurrency is the default for Transfer and Account Aggregate updates.
- Database constraints provide final protection for critical financial/idempotency uniqueness rules.
- A stable immutable `NetworkSubmissionReference` is available in the challenge Payment Network adapter.
- Payment submission is not blindly retried after an ambiguous timeout.
- Fraud technical retry is bounded and configurable.
- Reservation expiry exists as a model concept, but its exact lifetime is not assumed.
- Reconciliation retry count/backoff/escalation thresholds are configurable.
- Stuck-Transfer thresholds are configurable.
- Idempotency, Outbox, Reconciliation, and Audit retention periods require operational/business confirmation.
- Notification is asynchronous.
- Outbox delivery is at-least-once.
- Consumers maintain durable processed-message state.

The architecture does not pretend to solve a full banking ledger, complete fraud engine, real banking-network integration, complete authentication platform, Kubernetes production platform, or full Legacy replacement.

---

## 3. Event Storming Discoveries

Event Storming materially changed the design.

Key discoveries:

- Transfer Management owns the Transfer lifecycle and persisted end-to-end process state.
- Account and Balance Management owns the financial consistency/concurrency boundary for Reservation.
- Fraud Screening occurs before Balance Reservation.
- Fraud approval does not guarantee funds are still available; availability is checked atomically at reservation time.
- One Transfer must not create multiple financial holds.
- `Accepted` is not `Settled`.
- `Timeout` is not `Rejected`.
- An ambiguous Payment Network timeout produces `SubmissionStatusUnknown`.
- Blind external resubmission after an ambiguous timeout is prohibited.
- Reconciliation owns recovery from ambiguous/delayed external outcomes.
- Rejected/Cancelled releases an active Reservation.
- Settled consumes the Reservation.
- Transfer completion must eventually produce an Integration Event.
- committed business state and its Outbox Message must be persisted atomically.
- Integration Event delivery is at-least-once; downstream consumers must be idempotent.
- Notification is outside the critical financial transaction.
- stuck/ambiguous Transfers need durable recovery, Operations escalation, and auditable manual intervention.
- Bounded Context does not imply Microservice.

Important unresolved domain questions remain documented in `event-storming-summary.md`; they are not silently converted into permanent rules.

---

## 4. Ubiquitous Language

The detailed glossary is maintained in `docs/ubiquitous-language.md`.

Key domain terms include `Transfer`, `Transfer Type`, `Transfer State`, `Source Account`, `Destination Account`, `Available Balance`, `Reserved Balance`, `Balance Reservation`, `Reserve`, `Release`, `Consume`, `Fraud Screening`, `Daily Transfer Limit`, `External Submission`, `Network Submission Reference`, `SubmissionStatusUnknown`, `Settlement`, `Reconciliation`, `Compensation`, `Stuck Transfer`, and `Manual Recovery Action`.

Key engineering terms include `Idempotency Key`, `Payload Fingerprint`, `Correlation ID`, `Causation ID`, `Domain Event`, `Integration Event`, `Transactional Outbox`, `Outbox Message`, `At-Least-Once Delivery`, `Idempotent Consumer`, `Processed Message`, `Poison Message`, and `Dead Letter`.

Language rules:

- never use `Accepted` and `Settled` as synonyms;
- never use `Timeout` and `Rejected` as synonyms;
- never use Reservation as a synonym for Debit/Settlement;
- never claim end-to-end exactly-once delivery;
- never treat Aggregate, Bounded Context, Module, Microservice, Database Boundary, and Deployment Boundary as interchangeable concepts.

---

## 5. Subdomains

### Core Domain

**Transfer Management** — Transfer lifecycle, workflow ownership, transfer-type routing, external-submission eligibility, idempotent Transfer creation, and long-running coordination.

### Supporting Subdomains

- Account and Balance Management
- Customer Authorisation
- Fraud and Compliance
- Reconciliation
- Audit and Operations
- Daily Limit as a logical supporting capability in the current model

### Generic / Integration Capabilities

- Notification
- Payment Network Integration / Anti-Corruption Layer

Classification can be revisited if business ownership or organisational boundaries change.

---

## 6. Bounded Contexts

### Transfer Management — Core
Owns `Transfer`, lifecycle, process state, routing, HTTP idempotency, and external-submission eligibility.

### Account and Balance Management — Supporting
Owns `Account`, Available/Reserved Balance, Balance Reservations, Reserve/Release/Consume, and financial concurrency.

### Customer Authorisation — Supporting
Owns the decision whether the caller/customer may use the Source Account.

### Fraud and Compliance — Supporting
Owns Fraud Screening outcomes and fraud-specific decision semantics.

### Payment Network Integration — Integration Context
Owns external request/response mapping, `NetworkSubmissionReference`, timeout classification, status enquiry, and domain protection from external protocol details.

### Reconciliation — Supporting
Owns investigation/resolution of ambiguous, delayed, or conflicting external outcomes.

### Notification — Generic / Supporting
Consumes Integration Events and produces non-critical customer notifications.

### Audit and Operations — Supporting
Owns operational visibility, stuck-work queues, escalation, and immutable manual-action audit.

### Daily Limit
Currently a logical capability inside/behind Transfer Management rather than an independently deployed service. Exact long-term data ownership remains revisitable.

---

## 7. Context Map

Main relationships:

- Transfer Management → Account and Balance Management: explicit Reserve/Release/Consume contract.
- Transfer Management → Customer Authorisation: synchronous authorisation decision.
- Transfer Management → Fraud and Compliance: synchronous/bounded-retry screening decision.
- Transfer Management → Payment Network Integration: external submission/status contract.
- Payment Network Integration → Domestic Payment Network: Anti-Corruption Layer.
- Transfer Management → Reconciliation: durable investigation for ambiguous outcome.
- Reconciliation → Payment Network Integration: status enquiry.
- Reconciliation → Transfer Management: resolved outcome.
- Transfer Management → Notification: asynchronous `TransferCompleted` Integration Event.
- Transfer Management ↔ Audit and Operations: operational state and authorised manual commands.
- Legacy integration is isolated through explicit routing/ACL boundaries during migration.

Initial deployment uses one PostgreSQL instance, but module-owned tables/schemas remain explicit and arbitrary cross-module database access is prohibited.

See `docs/diagrams/context-map.drawio`.

---

## 8. Aggregate Design

### Transfer Aggregate Root

Responsibilities:

- own legal Transfer state transitions;
- reject invalid transitions;
- prevent progression after definitive Fraud rejection;
- prevent external submission before Fraud approval;
- prevent external submission before successful Reservation;
- distinguish ambiguous timeout from definitive rejection;
- prevent re-submission/completion after `Completed`;
- raise Domain Events for significant lifecycle facts.

The Aggregate owns business lifecycle truth, not technical retry schedules.

### Account Aggregate Root

Responsibilities:

- own financial consistency for Available/Reserved Balance;
- enforce active Account/currency rules relevant to Reservation;
- prevent Available Balance becoming negative;
- protect concurrent Reserve operations;
- prevent duplicate financial hold for one Transfer;
- safely Release or Consume an active Reservation.

### Persisted Supporting Concepts

These are persisted but are not automatically independent Aggregate Roots:

- Balance Reservation
- Idempotency Record
- Transfer Process State
- Outbox Message
- Processed Message
- Reconciliation Record
- Operations Audit Record

Reservation history is not loaded as an unbounded Account Aggregate collection.

---

## 9. Invariants

### Transfer

- Amount must be greater than zero.
- Source and Destination must differ.
- Source Account currency must match Transfer currency.
- Source Account must be active.
- Customer must be authorised for Source Account.
- Daily Limit must permit the Transfer.
- Fraud approval is required before Balance Reservation/external submission.
- External submission requires successful Balance Reservation.
- A definitively Fraud-rejected Transfer cannot continue.
- `SubmissionStatusUnknown` represents ambiguity, not rejection.
- `Completed` cannot be completed/submitted again.
- invalid state transitions are rejected.

### Account / Reservation

- Available Balance must never be negative.
- Reserved Balance must never be negative.
- a Transfer must create at most one financial Reservation.
- duplicate Reserve must not move money twice.
- Release must not move money twice.
- Consume must not move money twice.
- Consumed Reservation cannot be Released.
- Rejected/Cancelled releases an active Reservation.
- Settled consumes an active Reservation.

### Reliability

- the same logical HTTP request creates at most one Transfer;
- committed business result cannot be lost because publication failed;
- duplicate asynchronous delivery must not create duplicate downstream effects;
- manual actions must be authorised and auditable.

---

## 10. Quality-Attribute Scenarios

The following are measurable **challenge architecture targets**, not claimed contractual production SLAs.

### QA-01 — Performance
- **Source:** Mobile Banking Client
- **Stimulus:** submits a valid Transfer
- **Environment:** peak-hour target load
- **Artifact:** `POST /api/transfers`
- **Response:** validates and durably accepts the request without waiting for long-running external Settlement
- **Response Measure:** p95 API response time `< 500 ms`

### QA-02 — Availability
- **Source:** Domestic Payment Network
- **Stimulus:** becomes unavailable for five minutes
- **Environment:** normal production operation
- **Artifact:** Transfer Platform API and persisted workflow
- **Response:** Transfer submission/status API remains available when API/PostgreSQL are healthy; accepted work is persisted for later recovery
- **Response Measure:** `0` accepted Transfers lost because the Payment Network is unavailable

### QA-03 — Recoverability
- **Source:** application/runtime failure
- **Stimulus:** application crashes with pending Process, Reconciliation, or Outbox work
- **Environment:** restart using the same durable database
- **Artifact:** persisted workflow/background work
- **Response:** pending work is rediscovered and processing resumes safely
- **Response Measure:** durable pending work is rediscovered within `60 seconds` after application readiness; `0` lost committed results and `0` duplicate financial effects

### QA-04 — Scalability
- **Source:** increased workload
- **Stimulus:** application/background processing scales from one to two runtime instances
- **Environment:** shared PostgreSQL with concurrent Transfers
- **Artifact:** API, Aggregate persistence, durable workers
- **Response:** unrelated Accounts process concurrently and competing workers coordinate safely
- **Response Measure:** with two instances, financial invariants remain `100%` preserved and duplicate financial effects remain `0`

### QA-05 — Security
- **Source:** unauthenticated/unauthorised caller
- **Stimulus:** submits a financial or manual-recovery command
- **Environment:** normal runtime
- **Artifact:** HTTP/operations boundary
- **Response:** reject before financial mutation; log only safe identifiers
- **Response Measure:** `100%` unauthorised financial/manual commands rejected with appropriate `401/403`; `0` raw credentials/tokens/account numbers intentionally emitted in structured logs

### QA-06 — Modifiability
- **Source:** engineering/product change
- **Stimulus:** replace Notification Provider or Payment integration adapter
- **Environment:** normal development/release cycle
- **Artifact:** module/adapters
- **Response:** change remains inside the owning module/adapter plus composition/configuration
- **Response Measure:** `0` new direct cross-module database dependencies; architecture/dependency tests remain green

### QA-07 — Observability
- **Source:** Operations
- **Stimulus:** a Transfer exceeds configured state-age threshold or enters `SubmissionStatusUnknown`
- **Environment:** production-like runtime
- **Artifact:** logs/operational monitoring
- **Response:** workflow is traceable by `TransferId` and `CorrelationId`, with failure/retry/reconciliation context
- **Response Measure:** `100%` Transfer-processing log scopes contain correlation identifiers; stuck work is surfaced within `60 seconds` after crossing the configured threshold

### Trade-offs

- Correctness over marginal latency: concurrency checks, DB constraints, and durable persistence remain mandatory.
- Availability over immediate external consistency: external outcomes may be resolved later through Reconciliation.
- Operational simplicity over independent module scaling: Modular Monolith modules scale together initially.
- Recoverability over implementation simplicity: Process State, Reconciliation, Outbox, and retry state are persisted.
- Security constrains observability: use stable IDs instead of raw sensitive values.
- Modifiability constrains convenience: direct cross-module table access is prohibited.

---

## 11. Architecture Style

**Selected:** Incremental Hybrid at system level + Modular Monolith for the new Transfer capability.

During migration, Legacy continues to operate and controlled routing determines ownership for new Transfers.

Inside the new platform:

- one deployable .NET application;
- explicit domain modules;
- controlled dependencies;
- module-owned persistence;
- no service-per-domain-noun design.

Priorities: financial correctness, recoverability, modifiability, testability, operational simplicity, migration safety, observability.

Accepted compromises: independent module deployment/scaling, fine-grained process failure isolation, and technology heterogeneity.

See `docs/adr/ADR-001-architecture-style.md`.

---

## 12. Process Coordination

The selected strategy is a **Persistent Process Manager** inside Transfer Management.

It owns durable coordination state, determines the next workflow command, maintains correlation/recovery metadata, schedules safe retries/Reconciliation, and coordinates Compensation/manual escalation.

It does not own Account balance rules, Transfer lifecycle invariants, Fraud scoring, or Payment protocol details.

The Transfer Aggregate provides the explicit business state machine. Local transactions, Outbox, and durable workers are supporting mechanisms.

For ambiguous Payment Network timeout:

`PendingExternalSubmission -> SubmissionStatusUnknown -> Reconciliation`

No blind resubmission is permitted.

Persisted state supports restart, delayed responses, repeated Reconciliation, and Operations escalation.

See `docs/adr/ADR-002-process-coordination.md`.

---

## 13. Idempotency

`POST /api/transfers` requires `Idempotency-Key`.

The implementation baseline uses an `IdempotencyRecord` containing:

- server-defined submission scope;
- Idempotency Key;
- canonical request fingerprint;
- in-progress/completed/failed processing state;
- replayable response/result metadata;
- timestamps.

The canonical fingerprint uses a deterministic representation of semantically relevant request fields and SHA-256.

Expected behaviour:

- same scope/key + same fingerprint + completed result → return the stored logical result;
- same scope/key + same fingerprint + in-progress → return a consistent in-progress/accepted response rather than start a second Transfer;
- same scope/key + different fingerprint → reject as Idempotency conflict (`409`);
- concurrent same-key requests → database uniqueness allows only one logical owner;
- transient failure before a durable business result is not confused with a completed duplicate.

### Scope and Retention

The final production namespace and retention window depend on authenticated-client/customer identity and operational retention policy. They must be explicit configuration before production; the challenge implementation must document the concrete configured scope/TTL used in tests.

Other duplicate classes are separate:

- duplicate business command → Aggregate/lifecycle idempotency;
- duplicate external response → Transfer/process concurrency + state rules;
- duplicate Outbox publication → stable `MessageId` + at-least-once semantics;
- duplicate consumer processing → unique `(MessageId, ConsumerName)`.

Status checks alone are not the idempotency mechanism.

---

## 14. Concurrency

Default strategy: **Optimistic Concurrency + Database Constraints + Short Local Transactions**.

### Account Reservation

If two Transfers read the same balance concurrently, only one stale version may commit.

On conflict:

1. reload current Account;
2. re-evaluate domain rules;
3. return `InsufficientBalance` if funds are no longer sufficient;
4. otherwise a bounded retry may occur.

No unbounded retry is allowed.

### Database Protection

- `CHECK AvailableBalance >= 0`
- `CHECK ReservedBalance >= 0`
- unique Reservation by `TransferId` for the challenge model
- unique Idempotency scope/key
- concurrency token/version on Aggregate rows

Protected races also include Transfer state transitions, duplicate settlement/external results, Idempotency Record creation, Reservation Release vs Consume, and Outbox claim.

Outbox claiming may use short PostgreSQL `FOR UPDATE SKIP LOCKED`.

Distributed locking is rejected initially because it adds infrastructure/failure modes and cannot replace database constraints.

See `docs/adr/ADR-003-reservation-concurrency.md`.

---

## 15. Reliable Messaging

Use the **Transactional Outbox Pattern**.

In one local transaction:

- persist committed business-state change;
- persist its Outbox Message.

After commit a durable .NET `BackgroundService`:

- finds eligible messages;
- claims a bounded batch safely;
- dispatches/publishes;
- records success/failure;
- retries transient failures with bounded exponential backoff + jitter.

Delivery is **at-least-once**, not exactly-once.

Consumers use durable processed-message state with unique `(MessageId, ConsumerName)`.

Repeated deterministic failures move work to a terminal failed/Dead Letter state for operational investigation/manual retry.

No Kafka/RabbitMQ is required initially. A broker can be introduced later without removing the Outbox if independent consumers justify it.

See `docs/adr/ADR-004-reliable-messaging.md`.

---

## 16. Reconciliation

Reconciliation is first-class recovery for ambiguous external outcomes.

Trigger:

`PaymentNetworkTimedOut -> SubmissionStatusUnknown`

Rules:

- preserve stable `NetworkSubmissionReference`;
- do not blindly re-submit;
- keep active Reservation controlled while outcome remains unknown;
- persist attempt count/outcome/`NextAttemptAt`;
- use durable status enquiry.

Outcomes:

- Settled → Consume → Complete;
- definitively Rejected → Release → Reject;
- still Unknown → persist next attempt;
- conflicting/unresolvable → `ManualReviewRequired`.

Manual recovery is explicit, authorised, audited, and executed through domain/application commands rather than direct DB editing.

Exact retry/escalation timing remains configurable.

---

## 17. Legacy Modernisation

Migration strategy: **Strangler Fig**.

First migrated capability: **New Domestic Interbank Transfer Submission and orchestration**.

Rules:

- one workflow owner per Transfer;
- no real financial dual-write;
- in-flight ownership is not casually moved;
- rollback affects future routing only;
- feature toggles/canary cohorts control rollout;
- parallel run is shadow/comparison only;
- New domain is protected by Anti-Corruption Layer;
- Legacy Transactional Outbox preferred where feasible;
- CDC is fallback;
- polling is temporary last resort;
- historical migration is phased/restartable;
- Reconciliation compares authoritative ownership/outcomes;
- decommission only after explicit evidence.

Six phases:

1. Domain Discovery and Observability
2. Boundary Isolation
3. New Transfer Submission
4. External Integration Migration
5. Reconciliation and Operations Migration
6. Legacy Decommissioning

See `docs/modernisation-roadmap.md` and `docs/adr/ADR-005-legacy-modernisation.md`.

---

## 18. Security

A complete authentication platform is outside challenge scope, but security boundaries remain explicit.

- unauthenticated requests → `401`;
- authenticated but unauthorised financial/manual commands → `403`;
- Source Account authorisation is checked before financial processing;
- manual Operations commands require authorisation;
- integration contracts expose minimum required data.

Do not log raw authentication tokens, credentials/secrets, unnecessary raw account numbers, unnecessary PII, or full raw Idempotency Keys when sensitive.

Prefer `TransferId`, `CorrelationId`, `CausationId`, safe Account identifier representation, safe Idempotency-Key representation, and policy-approved external reference.

Manual Audit Records include operator, reason, prior state, resulting action/state, timestamp, Transfer ID, and correlation data.

---

## 19. Observability

Runtime baseline (TASK-20): `OperationalTelemetry` uses `LoggerMessage` templates across submission, fraud screening, payment submission, reconciliation, manual operations, Outbox dispatch, HTTP correlation middleware, and stuck-transfer queries. Logging failures are swallowed so business behavior is unchanged. Sensitive values are never logged — idempotency keys and account identifiers use SHA-256 fingerprints when referenced.

Operator stuck-work discovery: `GET /api/operations/stuck-transfers` (Operator policy) backed by `IStuckTransferQueries` and durable `Transfer` + `TransferProcessState` (+ optional reconciliation schedule) projections.

Baseline fields:

- structured `ILogger`;
- `CorrelationId`;
- `CausationId`;
- `TransferId`;
- safe `AccountId`;
- safe Idempotency-Key representation;
- state-transition logs;
- external-call duration;
- retry attempt;
- Reconciliation attempt/outcome;
- Outbox publication status;
- concurrency conflicts;
- `SubmissionStatusUnknown`;
- manual actions.

Suggested metrics:

- Transfer submissions by outcome;
- p50/p95/p99 submission latency;
- Fraud dependency failure rate;
- Payment Network timeout rate;
- number/age of `SubmissionStatusUnknown`;
- Reconciliation backlog/oldest age;
- Reservation conflict rate;
- Outbox pending/oldest age;
- Outbox retry/dead-letter count;
- duplicate consumer detections;
- stuck Transfer count;
- manual-review backlog.

A Transfer should be diagnosable using IDs/correlation without sensitive values.

---

## 20. Testing

### Domain Tests — minimum 10

1. amount must be positive;
2. source/destination differ;
3. Completed cannot complete again;
4. cannot submit externally before Reservation;
5. Fraud-rejected Transfer cannot continue;
6. timeout does not produce definitive rejection;
7. Account cannot be over-reserved;
8. duplicate Reservation is idempotent or rejected;
9. Consumed Reservation cannot be Released;
10. invalid Transfer transitions are rejected.

### Integration Tests — minimum 12

11. successful Transfer submission;
12. duplicate idempotent submission returns same result;
13. reused key with different payload rejected;
14. concurrent duplicate submissions create one Transfer;
15. concurrent Reservations do not over-reserve Account;
16. publication/broker-equivalent failure leaves Outbox record durable;
17. Outbox retry eventually dispatches;
18. duplicate settlement confirmation is idempotent;
19. duplicate consumer delivery creates one downstream effect;
20. workflow recovers after application restart;
21. optimistic concurrency conflict handled correctly;
22. poison-message retry bounded.

At least one test uses genuinely concurrent operations, not sequential calls.

Primary tools: xUnit, EF Core, PostgreSQL, Testcontainers.

Tests verify persisted final state and DB constraints, not only HTTP status.

---

## 21. Team Delivery Model

The detailed model belongs in `docs/team-engineering-model.md`.

Architecture-level expectations:

- Tech Lead facilitates decisions and ADR discipline without becoming sole technical owner.
- Three Backend Developers own modules/slices, tests, and operational concerns with rotating architecture ownership.
- QA owns risky scenario design, genuine concurrency/restart/failure testing, and release evidence alongside developers.
- Product Owner owns unresolved business policy and acceptance decisions.
- shared DevOps/Platform supports runtime/database/container/operability.

Working model:

- lightweight design review for high-impact change;
- PR evidence and reviewer expectations;
- Definition of Ready includes business/failure clarity;
- Definition of Done includes tests, observability, migration compatibility, and docs/ADR updates where needed;
- incident learning produces concrete actions;
- knowledge spreads through pairing/review/rotation.

---

## 22. Main Trade-offs

- **Modular Monolith vs Microservices:** local consistency and operational simplicity over independent deployment/scaling.
- **Persistent Process State vs simpler stateless code:** recoverability/observability over fewer persistence concepts.
- **Optimistic vs pessimistic locking:** lower normal blocking over simpler serialisation under contention.
- **Database-backed Outbox worker vs broker-first:** minimal infrastructure over richer broker features.
- **At-least-once vs exactly-once claim:** realistic duplicates + idempotency over false guarantees.
- **Fraud before Reservation:** avoid unnecessary holds; re-check balance atomically at Reservation time.
- **Reconciliation vs blind retry:** financial safety over superficially simpler resubmission.
- **Strangler vs Big-Bang:** incremental migration/rollback evidence over immediate architectural purity.

---

## 23. Known Limitations

Not enough information exists to finalise:

- Reservation expiry policy/lifetime;
- Daily Limit calculation window/timezone and long-term ownership;
- real Payment Network native idempotency guarantee;
- whether real network accepts client-generated immutable reference;
- authoritative settlement rule under conflicting evidence;
- exact Reconciliation retry/escalation timing;
- exact manual Operations roles/dual-control requirements;
- exact fields allowed in Integration Events;
- Idempotency/Outbox/Reconciliation/Audit retention windows;
- real Legacy modifiability and Outbox/CDC feasibility;
- production Account/Ledger source-of-truth cutover;
- production capacity targets;
- production identity platform;
- production regulatory requirements.

Challenge adapters/stubs demonstrate architecture, not complete banking integrations.

---

## 24. Future Evolution

Evolution is evidence-driven.

### Candidate extractions

- **Payment Network Integration:** when provider failure isolation, team ownership, release cadence, or scaling justify it.
- **Reconciliation:** when investigation workload/ownership materially diverges.
- **Notification:** when multiple providers/consumers or independent scaling/deployment become useful.

### Message Broker

Introduce RabbitMQ/Kafka only when independently deployed consumers, complex fan-out/routing, throughput, replay, or consumer-scaling needs justify it. The Outbox remains valuable.

### Account Concurrency

Revisit targeted pessimistic locking, atomic conditional updates, ledger architecture, or account-owner partitioning only if measured contention justifies it.

### Workflow Engine

Consider a dedicated workflow engine only if workflow variants/versioning/scheduling complexity outgrows the explicit Process Manager.

### End of Hybrid Architecture

After Legacy Transfer decommissioning, revisit ADR-001 because system-level Hybrid classification may no longer be necessary.

Any future extraction must preserve financial invariants, explicit ownership, idempotency, durable recovery, Reconciliation, auditability, observability, and the prohibition on hidden synchronous distributed transactions.
