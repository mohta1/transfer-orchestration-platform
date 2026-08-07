# Ubiquitous Language

This document contains two deliberately separated vocabularies:

1. **Domain Ubiquitous Language** — business terms that must be used consistently across domain code, tests, diagrams, API semantics, logs, and technical discussions.
2. **Engineering Vocabulary** — architecture and reliability terms used to keep technical boundaries precise.

Engineering mechanisms must not redefine the meaning of domain terms.

# Part I — Domain Ubiquitous Language

## 1. Transfer Terms

| Term | Definition |
|---|---|
| **Transfer** | A customer instruction to move a monetary amount from a Source Account to a Destination Account. A Transfer owns its business lifecycle and must reject invalid state transitions. |
| **Transfer ID** | The system-generated stable identifier of a Transfer. |
| **Transfer Type** | The route/category of the Transfer. The challenge explicitly requires at least the variation between another account within the same bank and an account in another domestic bank. |
| **Internal Bank Transfer** | A Transfer whose destination is another account within the same bank. It follows the common validation/authorisation/risk/reservation rules but does not require submission to the external domestic Payment Network. |
| **Domestic Interbank Transfer** | A Transfer whose destination is an account at another domestic bank and therefore requires the external Payment Network path. |
| **Submission Channel** | The channel through which the customer initiated the Transfer. |
| **Transfer State** | The current lifecycle position of the Transfer. State transitions must obey domain invariants. |
| **Source Account** | The account from which funds are intended to be transferred. It must be active, use the transfer currency, and be authorised for the customer. |
| **Destination Account** | The account intended to receive funds. It may be an internal Account or external domestic-bank account information depending on Transfer Type. |
| **Transfer Validation** | Validation that prevents invalid transfer data from entering the workflow. It includes positive amount, different source/destination, source-account currency match, active source account, and required destination/request data. |
| **Account Validation** | Validation of account-related eligibility required by the Transfer workflow, including source status/currency and destination validity appropriate to the Transfer Type. |
| **Authorisation** | The decision that the customer is permitted to use the Source Account for the requested Transfer. |
| **Daily Transfer Limit** | A configured business restriction on transferable value during an applicable business period. Exact ownership, calculation window, and timezone require business/design confirmation. |
| **Transfer Failure Reason** | A business-meaningful explanation recorded when a Transfer cannot continue or requires recovery. |

## 2. Account and Balance Terms

| Term | Definition |
|---|---|
| **Account** | The financial model that owns account identity, customer ownership, currency, status, Available Balance, Reserved Balance, and concurrency version. |
| **Active Account** | An Account whose status permits the requested financial operation. An inactive Source Account cannot enter the normal Transfer workflow. |
| **Available Balance** | Funds currently available to be reserved for new Transfers. It must never become negative. |
| **Reserved Balance** | Funds held by active Balance Reservations and unavailable to other Transfers. |
| **Balance Reservation** | A hold of a specific amount on a Source Account for a specific Transfer. |
| **Active Reservation** | A Reservation whose funds remain held and have not been Consumed, Released, or Expired. |
| **Reserve Balance** | The operation that atomically protects the requested amount for a Transfer. Under the selected balance model it reduces Available Balance and increases Reserved Balance. |
| **Consume Reservation** | Finalises the held funds after successful Settlement. The amount leaves Reserved Balance and is not returned to Available Balance. |
| **Release Reservation** | Cancels an active hold and returns the amount from Reserved Balance to Available Balance after definitive rejection or cancellation. |
| **Reservation Expiry** | The state reached when a Reservation exceeds its allowed lifetime. The exact expiry and automatic-release policy is an unresolved business decision. |
| **Insufficient Balance** | A definitive reservation outcome where Available Balance is not sufficient for the requested amount. |
| **Duplicate Reservation** | A second reservation attempt for the same Transfer. It must be idempotently handled or rejected so that only one financial hold exists. |
| **Over-reservation** | An invalid concurrent outcome where more funds are reserved than the Account has available. The system must prevent it. |

## 3. Fraud and Compliance Terms

| Term | Definition |
|---|---|
| **Fraud Screening** | Evaluation of a Transfer by the Fraud and Compliance capability. |
| **Fraud Approved** | A definitive fraud decision allowing the Transfer to continue. |
| **Fraud Rejected** | A definitive fraud decision after which the Transfer cannot continue through the normal workflow. |
| **Manual Fraud Review** | A fraud outcome requiring an authorised human analyst to approve or reject the Transfer. |
| **Fraud Timeout** | No definitive Fraud Screening result was received within the expected time. Timeout is neither approval nor rejection. |
| **Fraud Temporarily Unavailable** | A transient inability to obtain a fraud decision. Retry must be bounded and must not create duplicate business effects. |

## 4. Payment Network and Settlement Terms

| Term | Definition |
|---|---|
| **Payment Network** | The external domestic payment system used for Domestic Interbank Transfers. |
| **External Submission** | Sending an eligible Domestic Interbank Transfer to the Payment Network. It may occur only after Fraud Approval and successful Balance Reservation. |
| **Network Submission Reference** | A stable immutable reference used to correlate one Transfer with its external Payment Network operation. Support for a client-generated reference is an implementation assumption pending confirmation of the external contract. |
| **Submission Accepted** | The Payment Network has acknowledged/accepted the Transfer for processing. Acceptance is not Settlement. |
| **Submission Rejected** | A definitive external result stating that the Payment Network rejected the Transfer. |
| **Submission Status Unknown** | A Transfer state used when an external submission may have succeeded but the system cannot safely determine the result, typically after an ambiguous timeout. |
| **Settlement Pending** | External processing has not yet produced a definitive successful Settlement. |
| **Settlement** | The authoritative successful completion of the financial movement. |
| **Settlement Confirmation** | The external evidence/event that allows a Domestic Interbank Transfer to be marked Settled. |
| **Internal Settlement** | The bank-internal accounting/settlement path for an Internal Bank Transfer. Its exact accounting design is outside the challenge brief and must not be assumed to be the external Payment Network flow. |
| **Ambiguous Timeout** | A timeout where the system cannot know whether the Payment Network received, accepted, processed, or settled the Transfer. It must not be treated as rejection. |
| **Blind Resubmission** | Re-sending an external Transfer after an ambiguous timeout without first determining the previous outcome. This is prohibited because it can duplicate financial execution. |

## 5. Recovery and Operations Terms

| Term | Definition |
|---|---|
| **Reconciliation** | A durable recovery process that determines the authoritative external state of a Transfer with an ambiguous, delayed, or conflicting outcome. |
| **Reconciliation Attempt** | One persisted attempt to determine or verify external Transfer state. |
| **Compensation** | A controlled business action that reverses or neutralises a previously committed effect when normal forward processing cannot safely continue. |
| **Stuck Transfer** | A Transfer that remains in a non-terminal state beyond the configured operational threshold. |
| **Operational Escalation** | Moving a Transfer into an operational queue after automated recovery cannot safely resolve it. |
| **Manual Review Required** | A Transfer state indicating that automated processing is suspended until an authorised human decision is recorded. |
| **Manual Recovery Action** | An explicitly authorised human action such as requesting reconciliation, releasing a Reservation, confirming an externally verified outcome, rejecting a Transfer, or resolving an operational case. |
| **Audit Record** | An immutable record of a significant/manual action containing actor or operator, Transfer ID, previous state, result/new state, reason, timestamp, and correlation information. |

## 6. Preferred Domain State Vocabulary

The challenge provides the following suggested Transfer states; the lifecycle may be refined where justified:

- `Draft`
- `Submitted`
- `ValidationFailed`
- `PendingAuthorisation`
- `Authorised`
- `PendingFraudScreening`
- `FraudRejected`
- `PendingBalanceReservation`
- `BalanceReserved`
- `PendingExternalSubmission`
- `SubmissionStatusUnknown`
- `SettlementPending`
- `Completed`
- `Rejected`
- `Cancelled`
- `CompensationRequired`
- `ManualReviewRequired`

Balance Reservation states:

- `Active`
- `Consumed`
- `Released`
- `Expired`

Fraud Screening outcomes:

- `Approved`
- `Rejected`
- `ManualReviewRequired`
- `Timeout`
- `TemporarilyUnavailable`

Payment Network outcomes:

- `Accepted`
- `Rejected`
- `Settled`
- `Timeout`
- `TemporarilyUnavailable`
- `Unknown`

# Part II — Engineering Vocabulary

## 7. Events and Reliable Messaging

| Term | Definition |
|---|---|
| **Domain Event** | A fact that occurred inside a Bounded Context and is meaningful to its domain model. |
| **Integration Event** | A stable event published for consumption outside the owning Bounded Context. |
| **TransferCompleted** | The Integration Event representing that a Transfer reached the `Completed` business state. |
| **Transactional Outbox** | A reliability pattern in which the relevant committed business-state change and the Outbox Message are persisted in the same local transaction, then publication occurs asynchronously. |
| **Outbox Message** | A durable Integration Event record awaiting publication or recording completed publication state. |
| **At-Least-Once Delivery** | Delivery semantics where a message may be delivered more than once. The platform does not claim end-to-end exactly-once delivery. |
| **Idempotent Consumer** | A consumer that prevents repeated message delivery from causing repeated downstream business effects. |
| **Processed Message** | A durable record proving that a named consumer has already processed a particular message. |
| **Poison Message** | A message that repeatedly fails and exhausts the configured bounded retry policy. |
| **Dead Letter** | An operational terminal state for failed work that exhausted automated retry and requires investigation or explicit recovery. |

## 8. Request Idempotency

| Term | Definition |
|---|---|
| **Idempotency Key** | A client-generated identifier used to recognise repeated attempts of the same logical HTTP request. |
| **Idempotency Scope** | The server-defined namespace within which an Idempotency Key must be unique. |
| **Idempotency Record** | Persisted server-side state for an idempotent request, including its fingerprint, processing/result state, and replayable response information. |
| **Payload Fingerprint** | A deterministic hash of the canonical request payload used to detect reuse of an Idempotency Key with different content. |
| **Duplicate HTTP Request** | A repeated HTTP request using the same idempotency scope/key and same payload fingerprint. |
| **Idempotency Conflict** | Reuse of the same idempotency scope/key with a different payload fingerprint. The existing Transfer must not be modified. |
| **Duplicate Business Command** | Repetition of the same business intent after transport-level deduplication; domain invariants must still prevent duplicate effects. |
| **Duplicate Broker Message** | Redelivery of the same message by asynchronous infrastructure. |
| **Duplicate External Response** | Repeated delivery of the same external result, such as duplicate Settlement Confirmation. |
| **Duplicate Outbox Publication** | Re-publication of an Outbox event due to at-least-once behaviour. |
| **Duplicate Consumer Processing** | A repeated attempt to execute downstream effects for an already processed message. |

## 9. Concurrency and Consistency

| Term | Definition |
|---|---|
| **Invariant** | A business rule that must remain true for every valid domain state. |
| **Consistency Boundary** | The state that must be changed with sufficient atomicity/isolation to preserve a business invariant. |
| **Concurrency Boundary** | The resource/domain boundary whose competing writes must be detected or serialised to preserve invariants. |
| **Optimistic Concurrency** | Detecting a conflicting stale write through a persisted version/concurrency token rather than relying on a long-lived distributed lock. |
| **Concurrency Conflict** | A detected competing update that prevents a mutation from committing safely. |
| **Transaction Boundary** | The database transaction within which related persistence changes either all commit or all roll back. |
| **Database Constraint** | A database-enforced rule such as unique or check constraints that provides final protection for critical invariants. |

Account is the key concurrency boundary for Balance Reservation. Transfer state transitions, Idempotency Records, external-result processing, and Outbox claiming also require explicit concurrency protection.

## 10. Tracing and Observability

| Term | Definition |
|---|---|
| **Correlation ID** | An identifier grouping technical activity belonging to the same end-to-end request/workflow. |
| **Causation ID** | An identifier linking a command/event to the message or action that directly caused it. |
| **External Reference** | A stable reference used to correlate the internal Transfer with an external Payment Network operation. |
| **Safe Idempotency-Key Representation** | A non-sensitive representation used in logs so observability does not expose raw keys unnecessarily. |

## 11. DDD and Architecture Terms

| Term | Definition |
|---|---|
| **Subdomain** | A business capability/problem area, classified as Core, Supporting, or Generic. |
| **Core Domain** | The business area where domain differentiation and the most important modelling effort are concentrated. |
| **Supporting Subdomain** | A business capability needed by the Core Domain but not itself the primary differentiator. |
| **Generic Subdomain** | A necessary but broadly reusable/non-differentiating capability. |
| **Bounded Context** | A boundary within which a domain model and vocabulary have one consistent meaning. |
| **Context Relationship** | The explicit dependency/integration relationship between Bounded Contexts. |
| **Aggregate** | A cluster of domain objects protected as one consistency boundary. |
| **Aggregate Root** | The entry point through which external code changes the protected state of an Aggregate. |
| **Entity** | A domain object defined by identity and lifecycle. |
| **Value Object** | An immutable domain object defined by its values rather than identity. |
| **Domain Service** | Domain behaviour that belongs to the model but does not naturally belong to one Entity or Value Object. |
| **Repository** | A domain-facing abstraction for retrieving and persisting Aggregate Roots. |
| **Module** | A code-organisation boundary. It is not automatically a Bounded Context or deployment unit. |
| **Microservice** | An independently deployable service with independent operational ownership. A Bounded Context does not automatically require a Microservice. |
| **Database Boundary** | A physical data ownership/persistence boundary. It is not automatically equal to an Aggregate, Bounded Context, or deployment boundary. |
| **Deployment Boundary** | A unit independently built/deployed. It is distinct from domain modelling boundaries. |

## 12. Language Rules

To keep the model precise:

- Do not use **Accepted** as a synonym for **Settled**.
- Do not use **Timeout** as a synonym for **Rejected**.
- Do not use **Reservation** as a synonym for **Debit** or **Settlement**.
- Do not route an **Internal Bank Transfer** through the external Payment Network merely because the Domestic Interbank flow does.
- Do not use **Domain Event** and **Integration Event** interchangeably.
- Do not claim **exactly-once** end-to-end delivery; use **at-least-once + idempotency**.
- Do not use **Bounded Context**, **Subdomain**, **Module**, **Microservice**, **Aggregate**, **Entity**, **Database Boundary**, or **Deployment Boundary** interchangeably.
- Use `SubmissionStatusUnknown` only for ambiguous external outcomes.
- Use **Release** when held funds return to Available Balance.
- Use **Consume** when successfully settled funds permanently leave Reserved Balance.
- Manual intervention must always include explicit authorisation and auditability.
- Business requirements and implementation assumptions must be labelled separately.
