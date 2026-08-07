# Event Storming Summary

This document summarises the Event Storming output for the **Resilient Interbank Transfer Orchestration Platform**. It is intentionally aligned with the challenge brief and separates confirmed business rules from implementation assumptions.

## 1. Main Domain Discoveries

The transfer capability is a long-running financial workflow rather than a CRUD operation. It crosses several domain boundaries and must remain correct under duplicate requests, concurrent transfers, external timeouts, delayed events, partial failures, and application restarts.

Main discoveries:

- **Transfer Management owns the Transfer lifecycle and persisted end-to-end process state.**
- **Account and Balance Management owns the financial consistency and concurrency boundary for balance reservation.**
- Transfer validation must prevent invalid data from entering the workflow, including:
  - amount must be greater than zero;
  - source and destination must differ;
  - source-account currency must match transfer currency;
  - source account must be active.
- Customer authorisation and daily-limit approval are required before the Transfer may continue.
- Fraud screening must reach an approved outcome before balance reservation and external submission.
- Fraud approval does not reserve funds; available balance must still be checked atomically at reservation time.
- A Transfer must create at most one active reservation.
- A payment-network timeout is an **ambiguous outcome**, not a rejection.
- An ambiguous external timeout moves the Transfer to `SubmissionStatusUnknown` and triggers reconciliation; blind resubmission is prohibited.
- `Accepted` and `Settled` are different outcomes. A reservation is consumed only after successful settlement.
- A rejected or cancelled Transfer releases any active reservation.
- Transfer completion must eventually produce an Integration Event.
- The committed Transfer result must not be lost when Integration Event publication fails.
- Integration-event delivery is **at-least-once**; consumers therefore must be idempotent.
- Notification is asynchronous and outside the critical financial transaction.
- Stuck or ambiguous Transfers require durable recovery, reconciliation, operational escalation, and auditable manual intervention.
- Bounded Contexts are domain boundaries and do not automatically imply separate Microservices.

### Transfer-Type Variation

The challenge defines two destination variations:

1. another account within the same bank;
2. an account in another domestic bank.

The detailed resilience flow focuses on **Domestic Interbank** transfers because that path includes the external Payment Network, ambiguous timeouts, settlement tracking, and reconciliation.

An **Internal Bank Transfer** follows the common validation, authorisation, limit, fraud, and reservation rules, but does not require external Payment Network submission. It must use an internal settlement/accounting path instead.

The architecture and Context Map must preserve this distinction rather than forcing both transfer types through the external-network workflow.

## 2. Ubiquitous Language

The detailed domain glossary and engineering vocabulary are maintained in [`ubiquitous-language.md`](./ubiquitous-language.md).

Key domain terms:

- Transfer
- Transfer Type
- Transfer State
- Source Account
- Destination Account
- Account Validation
- Balance Reservation
- Available Balance
- Reserved Balance
- Fraud Screening
- Manual Fraud Review
- Daily Transfer Limit
- External Submission
- Payment Network
- Network Submission Reference
- Submission Status Unknown
- Settlement
- Reconciliation
- Compensation
- Stuck Transfer
- Manual Recovery Action

Key reliability terms:

- Idempotency Key
- Payload Fingerprint
- Domain Event
- Integration Event
- Transactional Outbox
- Outbox Message
- At-Least-Once Delivery
- Idempotent Consumer
- Correlation ID
- Causation ID

## 3. Candidate Aggregates

The current Event Storming and challenge requirements support two primary Aggregate Roots.

### 3.1 Transfer

**Aggregate Root:** `Transfer`

Responsibilities:

- Own the Transfer lifecycle.
- Reject invalid state transitions.
- Prevent duplicate submission of the same Transfer.
- Prevent progress after definitive fraud rejection.
- Prevent external submission before fraud approval.
- Prevent external submission before successful balance reservation.
- Distinguish ambiguous timeout from definitive rejection.
- Prevent a completed Transfer from being submitted or completed again.
- Prevent a normally rejected Transfer from becoming completed through a normal command.
- Raise Domain Events for important lifecycle transitions.

### 3.2 Account

**Aggregate Root:** `Account`

Responsibilities:

- Own available and reserved balance invariants.
- Enforce account status and currency requirements relevant to reservation.
- Prevent available balance from becoming negative.
- Protect concurrent reservation operations.
- Prevent duplicate reservation for the same Transfer.
- Release an active reservation safely.
- Consume a settled reservation safely.
- Reject invalid reservation transitions.

## 4. Supporting Persisted Concepts

These concepts are persisted and important, but are **not automatically separate Aggregates**.

### Balance Reservation

Represents an amount held for one Transfer against one source Account.

Required information includes:

- Reservation ID
- Transfer ID
- Account ID
- Amount
- Status
- Creation time
- Expiration time
- Release or consumption time

For the challenge implementation, the Reservation may be stored separately for auditability while Account remains the financial consistency/concurrency boundary.

### Idempotency Record

Persists request-deduplication state including:

- idempotency scope/key;
- canonical request fingerprint;
- in-progress/completed state;
- replayable result;
- conflict detection information.

It prevents the same client request from creating multiple Transfers.

### Transfer Process State

Durable workflow state used to continue orchestration after retries, delayed external results, or application restarts.

Its ownership remains with Transfer Management.

### Outbox Message

A durable Integration Event record persisted atomically with the relevant committed business-state change.

### Processed Message

A durable consumer-side deduplication record used to prevent duplicate downstream effects under at-least-once delivery.

### Reconciliation Record

Tracks attempts and outcomes while resolving ambiguous external status.

### Operations Audit Record

Records authorised manual actions, including:

- operator identity;
- Transfer ID;
- previous state;
- resulting state or action;
- reason;
- timestamp;
- correlation identifier.

## 5. Candidate Bounded Contexts

The challenge requires at least the following boundaries to be considered.

### Transfer Management — Core Domain

Owns:

- Transfer lifecycle;
- Transfer invariants;
- persisted process coordination state;
- transfer-type routing;
- external-submission eligibility;
- HTTP idempotency for Transfer creation.

### Account and Balance Management — Supporting Domain

Owns:

- Account identity, currency, and status;
- available and reserved balances;
- Balance Reservations;
- reservation concurrency;
- reserve / release / consume operations.

### Customer Authorisation — Supporting Domain

Owns the decision whether a customer is authorised to use the source Account.

### Fraud and Compliance — Supporting Domain

Owns Fraud Screening outcomes:

- `Approved`
- `Rejected`
- `ManualReviewRequired`
- `Timeout`
- `TemporarilyUnavailable`

### Payment Network Integration — Integration Context

Protects the domain from the external payment-network protocol and owns:

- external submission;
- external reference mapping;
- response translation;
- timeout classification;
- external status enquiry.

This context applies to transfer types that require the external domestic Payment Network.

### Reconciliation — Supporting Domain

Owns recovery of ambiguous, delayed, or conflicting external outcomes.

It determines whether the Transfer can safely:

- continue;
- settle;
- reject;
- remain unknown;
- escalate to Operations.

### Notification — Generic / Supporting Capability

Consumes Integration Events and notifies customers asynchronously.

Notification failure does not roll back or change a completed financial Transfer.

### Audit and Operations — Supporting Domain

Owns:

- stuck-transfer visibility;
- operational queues;
- escalation;
- authorised manual recovery;
- immutable audit history.

### Daily Limit — Logical Supporting Capability

The challenge explicitly asks whether daily-limit ownership belongs to Transfer or Account.

For the current design, daily-limit evaluation is kept as a **logical supporting capability** behind a boundary/port. It is not yet promoted to an independently deployable Bounded Context or Microservice.

The final ownership and data model remain a documented design decision for the Context Map and architecture.

## 6. Important Policies

1. **When a request is received → Validate Transfer.**
2. **When Transfer Validated → Check Customer and Account Authorisation.**
3. **When Customer Authorised → Validate Daily Transfer Limit.**
4. **When Daily Limit Approved → Request Fraud Screening.**
5. **When Fraud Approved → Reserve Source Balance.**
6. **When Balance Reserved → Route according to Transfer Type.**
7. **For Domestic Interbank → Mark Ready and submit to the Payment Network.**
8. **For Internal Bank Transfer → use the internal settlement/accounting path rather than Payment Network submission.**
9. **When Payment Network Times Out ambiguously → Mark Submission Status Unknown.**
10. **When Submission Status Becomes Unknown → Request Reconciliation.**
11. **When Settlement Is Confirmed → Consume Balance Reservation.**
12. **When Transfer Is Rejected or Cancelled → Release Active Reservation, if one exists.**
13. **When Transfer Completes → persist the business-state change and corresponding Outbox Message atomically.**
14. **When Outbox Publication Fails → retry using bounded backoff.**
15. **When an Integration Event Is Redelivered → prevent duplicate downstream effects.**
16. **When a Transfer exceeds its allowed state-age threshold → detect it as Stuck and escalate to Operations.**
17. **When fraud-screening retries are exhausted → request Manual Fraud Review.**
18. **All manual recovery actions → create an auditable record.**

## 7. Main Hotspots

The Event Storming exercise identifies and discusses the required hotspots:

- Should Fraud Screening happen before or after Balance Reservation?
- How long may a Reservation remain active?
- Who owns the Transfer process state?
- What is the source of truth for Settlement?
- Is Payment Network submission idempotent?
- Can the external network accept a client-generated reference?
- When does a timeout become `SubmissionStatusUnknown`?
- Which failures require Compensation?
- Which failures require Reconciliation?
- Which failures require manual intervention?
- What is the boundary between Transfer Management and Account and Balance Management?
- Is the daily transfer limit part of the Transfer or Account domain?
- Should customer notification participate in the critical workflow?
- Which events must cross Bounded Context boundaries?
- Which data must not be included in external events?
- How will operational users identify Stuck Transfers?

Additional hotspots discovered:

- How should conflicting external settlement results be resolved?
- What permissions and approvals are required for manual recovery?
- How should Internal Bank and Domestic Interbank flows diverge after common validation/reservation stages?

## 8. Unresolved Business Questions

The challenge does not provide enough business information to answer the following definitively:

1. What is the maximum lifetime of an active Balance Reservation?
2. What is the exact daily-limit calculation window and timezone?
3. Which domain ultimately owns daily-limit consumption data?
4. Does the external Payment Network provide native idempotency guarantees?
5. Does the Payment Network support a client-generated immutable submission reference?
6. What external status is authoritative when settlement information conflicts?
7. How long should automated reconciliation continue before operational escalation?
8. When may an old active Reservation be released automatically?
9. What exact accounting/settlement mechanism should complete an Internal Bank Transfer?
10. What roles and approval controls are required for manual settlement confirmation or manual reservation release?
11. Which customer/account fields may cross Bounded Context boundaries in Integration Events?
12. What retention periods apply to idempotency, Outbox, reconciliation, and audit records?

These remain explicit business or operational questions and must not be silently converted into permanent business rules.

## 9. Decisions Influenced by Event Storming

The following decisions are current architecture/design decisions:

- Transfer Management owns the persisted end-to-end workflow state.
- Fraud Screening occurs before Balance Reservation.
- Account is the reservation consistency and concurrency boundary.
- Balance Reservation is persisted for auditability without automatically becoming a separate Aggregate Root.
- Transfer type determines whether processing uses the external Payment Network or an internal settlement path.
- Payment Network timeout produces `SubmissionStatusUnknown`; blind resubmission is prohibited.
- Reconciliation owns recovery from ambiguous external-network outcomes.
- A stable network submission reference is required for safe external correlation.
- Transfer completion must eventually produce an Integration Event.
- The committed business-state change and its Outbox Message are persisted atomically.
- Integration-event delivery is treated as at-least-once.
- Message consumers are idempotent.
- Notification is asynchronous and outside the critical financial transaction.
- Manual recovery requires explicit authorisation and complete auditability.
- Bounded Contexts are not automatically Microservices or deployment boundaries.

## 10. Implementation Assumptions

The following assumptions are introduced to make the challenge implementation executable. They are **not confirmed business requirements**:

- PostgreSQL is used as the relational persistence store.
- Optimistic concurrency is used for Account and Transfer updates.
- Database constraints provide final protection for reservation and idempotency uniqueness.
- The challenge Payment Network adapter supports correlation by an immutable `NetworkSubmissionReference`.
- External Payment Network submission is not blindly retried after an ambiguous timeout.
- Fraud-screening technical retries are bounded with configurable backoff and maximum attempts.
- Reservation expiry is configurable and no permanent automatic-expiry business rule is assumed.
- Notification is driven asynchronously by an Integration Event.
- Outbox publication uses at-least-once delivery.
- Consumers maintain durable processed-message state.
- Stuck-transfer thresholds are configurable.
- Real Customer Authorisation, Fraud, Payment Network, Notification, and complete authentication platforms may be represented by adapters/stubs because full integrations are outside the challenge scope.
- The first coded vertical slice may focus on `DomesticInterbank`; Internal Bank routing remains an explicit domain variation even if its complete accounting implementation is not included in the slice.

## 11. Consistency Notes for Subsequent Artifacts

The following statements are the baseline that future Context Maps, ADRs, architecture documents, code, and tests must not contradict without an explicit ADR/change:

- `Accepted` is not `Settled`.
- `Timeout` is not `Rejected`.
- Fraud approval is required before external submission.
- Successful Balance Reservation is required before external submission.
- Source Account must be active.
- Available Balance must never become negative.
- One Transfer must not create multiple active Reservations.
- Rejected/Cancelled releases an active Reservation.
- Settled consumes the Reservation.
- Unknown external outcome is recovered through Reconciliation, not blind resubmission.
- Outbox reliability is at-least-once, not exactly-once.
- Manual intervention is auditable.
- Bounded Context, Aggregate, Module, Microservice, Database Boundary, and Deployment Boundary are distinct concepts.
