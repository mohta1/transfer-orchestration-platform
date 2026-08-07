# ADR-002: Transfer Process Coordination Strategy

- **Status:** Accepted
- **Decision Date:** 2026-08-07
- **Decision Owners:** Backend Engineering / Architecture
- **Scope:** Transfer Management process coordination

## Context

A Transfer is a long-running financial process rather than a single database transaction.

The workflow can include:

1. Transfer validation
2. Customer authorisation
3. Daily-limit evaluation
4. Fraud screening
5. Balance reservation
6. Transfer-type routing
7. External submission for Domestic Interbank Transfers
8. Settlement tracking
9. Reconciliation when the external result is ambiguous
10. Reservation consumption or release
11. Transfer completion
12. Asynchronous downstream notification
13. Operational escalation or manual recovery when automated processing cannot safely continue

The process must remain recoverable under:

- duplicate requests;
- duplicate commands/events;
- transient dependency failure;
- fraud timeout;
- payment-network timeout;
- ambiguous external outcomes;
- delayed settlement;
- broker outage;
- application restart;
- reconciliation retry;
- manual intervention.

The Event Storming baseline establishes that:

- Transfer Management owns the persisted end-to-end workflow state.
- `Timeout` is not equivalent to `Rejected`.
- an ambiguous Payment Network timeout moves the Transfer to `SubmissionStatusUnknown`;
- blind external resubmission is prohibited;
- `Accepted` is not equivalent to `Settled`;
- Reconciliation resolves ambiguous external outcomes;
- Account and Balance Management owns financial reservation invariants;
- Notification is outside the critical financial transaction;
- manual recovery must be authorised and auditable.

The architecture must therefore provide one understandable owner for the end-to-end workflow without moving domain invariants into a large coordinator.

## Decision

Use a **Persistent Process Manager** as the Transfer process-coordination strategy.

The Process Manager owns **coordination state and next-step decisions** for the long-running Transfer workflow.

It does not own the business invariants of the Transfer Aggregate, Account Aggregate, Fraud capability, or external Payment Network.

### Responsibility Model

The responsibilities are deliberately separated:

| Component | Responsibility |
|---|---|
| **Persistent Process Manager** | Determines which workflow action should occur next and persists coordination/recovery state. |
| **Transfer Aggregate / State Machine** | Enforces legal Transfer lifecycle transitions and Transfer invariants. |
| **Account Aggregate** | Enforces balance and Balance Reservation invariants. |
| **Application/Module Contracts** | Execute explicit commands against another capability without exposing internal implementation. |
| **Local Transactions** | Persist local state changes atomically. |
| **Transactional Outbox** | Reliably bridges committed local state to asynchronous Integration Events. |
| **Durable Background Workers** | Execute retryable asynchronous work after commit and after application restart. |
| **Reconciliation** | Resolves ambiguous, delayed, or conflicting external Payment Network outcomes. |
| **Audit and Operations** | Exposes stuck workflows and authorised manual recovery actions. |

The selected process-coordination strategy is **Persistent Process Manager**.

The Transfer state machine, local transactions, Outbox, and durable workers are supporting mechanisms, not alternative coordination strategies being used simultaneously.

## Why a Process Manager

The workflow is stateful and may remain active for substantially longer than one request or one database transaction.

A central coordination owner makes the following questions answerable:

- What step is this Transfer currently waiting for?
- What action is expected next?
- Which action was last attempted?
- Which dependency result was received?
- Is retry safe?
- Is the workflow waiting for Reconciliation?
- Has automated recovery been exhausted?
- Does an operator need to intervene?
- Can processing resume safely after restart?

This is especially important for:

`PendingExternalSubmission -> SubmissionStatusUnknown -> Reconciliation -> Settled / Rejected / StillUnknown / ManualReviewRequired`

A purely implicit event chain would make this recovery path harder to understand and operate.

## Persisted Workflow State

Workflow state must be durable.

The Process Manager persists sufficient information to reconstruct the current coordination state after application restart.

Conceptually, persisted process state includes:

- `TransferId`
- current process step/state
- correlation identifiers
- relevant external reference(s)
- current waiting condition
- retry metadata where applicable
- next eligible attempt time where applicable
- last significant outcome
- timestamps used for stuck-process detection

Exact table/column design is deferred to implementation.

Business truth must not be duplicated unnecessarily inside the Process Manager. For example:

- Transfer lifecycle truth remains in the Transfer Aggregate.
- Balance/Reservation truth remains in Account and Balance Management.
- the Process Manager stores only coordination information necessary to continue the workflow safely.

## Correlation

Every end-to-end Transfer process is correlated primarily by `TransferId`.

Additional identifiers are used where appropriate:

- `CorrelationId` for end-to-end technical tracing;
- `CausationId` for command/event causality;
- `NetworkSubmissionReference` for Payment Network correlation;
- message identifier for asynchronous message deduplication.

The same Transfer must remain traceable across:

- HTTP submission;
- Process Manager actions;
- module calls;
- external calls;
- Outbox publication;
- background processing;
- Reconciliation;
- manual recovery.

## Commands and Events

The Process Manager coordinates through explicit commands and reacts to outcomes/events.

Examples:

- `CheckCustomerAuthorisation`
- `EvaluateDailyTransferLimit`
- `RequestFraudScreening`
- `ReserveBalance`
- `SubmitToPaymentNetwork`
- `ScheduleReconciliation`
- `ConsumeReservation`
- `ReleaseReservation`
- `CompleteTransfer`

Relevant outcomes/events include:

- `CustomerAuthorised`
- `DailyLimitApproved`
- `FraudApproved`
- `FraudRejected`
- `BalanceReserved`
- `InsufficientBalance`
- `SubmissionAccepted`
- `SubmissionRejected`
- `PaymentNetworkTimedOut`
- `SettlementConfirmed`
- `ReconciliationResolved`
- `ReservationConsumed`
- `ReservationReleased`
- `TransferCompleted`

Command names and event names may be refined during implementation, but commands represent requested actions and events represent facts that have occurred.

## Transfer State Machine

The Transfer Aggregate enforces an explicit lifecycle/state machine.

For example:

- a completed Transfer cannot be submitted again;
- a definitively rejected Transfer cannot continue through the normal workflow;
- `SubmissionStatusUnknown` is valid only from an external-submission path where the outcome is ambiguous;
- settlement processing cannot bypass required Fraud Approval and Balance Reservation.

The Process Manager does not implement these rules itself.

Instead:

1. the Process Manager decides which business action should be requested next;
2. the appropriate Aggregate/Context validates and executes that action;
3. the resulting fact is persisted;
4. the Process Manager reacts to the resulting outcome.

This prevents coordination code from becoming the owner of domain invariants.

## Retry Strategy

Retry is **operation-specific**, not a blanket Process Manager rule.

The Process Manager may schedule retry only when the operation is classified as safely retryable.

Examples:

### Safe / Bounded Technical Retry

Potential candidates include:

- temporary Fraud service unavailability;
- Outbox publication failure;
- Reconciliation status enquiry;
- notification-provider failure.

Retries must:

- be bounded where appropriate;
- use backoff;
- persist attempt state when durability is required;
- avoid tight retry loops;
- remain observable.

### Unsafe Blind Retry

External Payment Network submission is not blindly retried after an ambiguous timeout.

If submission may already have been accepted, retrying it without resolving the existing outcome may create a duplicate financial effect.

The Process Manager therefore transitions the Transfer into the recoverable unknown path and requests Reconciliation.

Exact retry counts/backoff intervals are configuration/operational decisions and are not fixed by this ADR unless the business contract requires them.

## Timeout Handling

Timeout is classified according to whether the business outcome is known.

### Fraud Timeout

A Fraud timeout does not equal approval or rejection.

The Process Manager may use bounded technical retry and/or route to Manual Fraud Review when automated recovery is exhausted, according to policy.

### Payment Network Timeout

A Payment Network timeout is treated as ambiguous.

The required behaviour is:

`PaymentNetworkTimedOut -> SubmissionStatusUnknown -> ScheduleReconciliation`

The Balance Reservation remains controlled while the external result is unresolved.

Blind external resubmission is prohibited.

## Compensation

Compensation is explicit business behaviour, not a generic rollback of distributed work.

Examples:

- a definitively rejected or cancelled Transfer with an active Reservation requests `ReleaseReservation`;
- a successfully settled Transfer requests `ConsumeReservation`.

The Process Manager determines when a compensating action is required, but Account and Balance Management validates and performs the financial operation.

A Payment Network timeout does **not** automatically trigger reservation release because the external Transfer may already have been accepted or settled.

## Reconciliation

Reconciliation is the recovery path for ambiguous or delayed Payment Network outcomes.

The Process Manager may request/schedule Reconciliation when a Transfer enters `SubmissionStatusUnknown` or another explicitly recoverable external state.

Possible reconciliation outcomes include:

- settled;
- definitively rejected;
- still unknown;
- conflicting result requiring further investigation;
- manual intervention required.

The Process Manager maps the resolved outcome to the next workflow command, but Reconciliation owns the external-status investigation itself.

Repeated Reconciliation attempts must be durable, bounded/configurable, and observable.

## Manual Intervention

Manual intervention is a first-class recovery path, not an undocumented database edit.

When automation cannot resolve a workflow safely:

- the Transfer becomes visible to authorised Operations users;
- the current state and failure/recovery history remain inspectable;
- an explicit manual command is performed;
- operator identity and reason are recorded;
- the previous state and resulting action/state are auditable;
- the same domain invariants remain enforced.

Examples may include:

- request Reconciliation;
- resolve an externally verified outcome;
- release a Reservation when explicitly authorised;
- reject or otherwise resolve a Transfer according to an approved operational procedure.

The exact role/approval matrix is an unresolved business/security decision and is not invented by this ADR.

## Broker Outage

The Process Manager must not require a broker to be available for a local business transaction to commit when the Integration Event can be delivered through the Transactional Outbox.

For example:

1. Transfer reaches `Completed`;
2. the completed business state and Outbox Message are persisted atomically;
3. the transaction commits;
4. if the broker is unavailable, the Outbox Message remains pending;
5. durable publication is retried later;
6. the backlog is observable.

Broker failure therefore does not erase a successfully committed financial result.

The detailed Outbox/publisher strategy is defined in **ADR-004**.

## Application Restart

Process progress must survive application restart.

The design must not depend on in-memory fire-and-forget work.

After restart:

- persisted workflow state remains available;
- pending durable work can be rediscovered;
- Outbox records remain available;
- Reconciliation work remains recoverable;
- a previously created Balance Reservation is not silently abandoned;
- external submission is not duplicated simply because the process restarted.

Restart recovery must be idempotent and driven from persisted state.

## Idempotency

Process coordination assumes duplicate delivery and duplicate invocation can occur.

Idempotency is required at multiple layers:

- duplicate HTTP request;
- duplicate business command;
- duplicate asynchronous message;
- duplicate external response;
- duplicate settlement confirmation;
- duplicate Outbox publication;
- duplicate consumer processing.

The Process Manager must not rely only on current status checks as a complete idempotency strategy.

Each protected side effect must use the appropriate persistence/concurrency mechanism.

Exact HTTP idempotency and database-concurrency designs are addressed separately in the implementation and ADR-003/ADR-004 where relevant.

## Observability

A Transfer process must be diagnosable without reconstructing its history manually from unrelated logs.

Structured telemetry/logging must include safe identifiers such as:

- `TransferId`
- `CorrelationId`
- current/previous process step
- command/action name
- event/outcome type
- external reference where safe
- attempt count
- next-attempt time
- elapsed time in state
- failure classification
- reconciliation status
- manual-action identifier where applicable

Operational monitoring should make it possible to identify:

- Transfers stuck beyond the configured state-age threshold;
- Transfers in `SubmissionStatusUnknown`;
- Reconciliation backlog;
- repeated transient failures;
- manual-review backlog;
- Outbox backlog;
- dependency failures.

Sensitive account/customer information must not be exposed in logs merely for correlation.

## Avoiding a God Service

The Process Manager is intentionally narrow.

It may:

- persist coordination state;
- react to workflow outcomes;
- select the next command;
- schedule durable work;
- classify workflow-level timeout/recovery paths;
- coordinate Compensation and Reconciliation.

It must **not**:

- calculate whether an Account has sufficient funds;
- mutate Account balances directly;
- implement Transfer state-transition invariants;
- implement Fraud scoring/rules;
- implement Payment Network protocol details;
- query another module's tables directly;
- become the source of truth for Account/Reservation state;
- become the source of truth for external settlement state.

Domain decisions remain with the owning Aggregate or Bounded Context.

This boundary is enforced through explicit contracts, tests, code review, and module dependency rules.

## Local Transactions and Asynchronous Integration

`Local transaction with asynchronous integration` is **not** selected as the end-to-end process-coordination strategy.

It is used as a supporting reliability mechanism where a local business state change must commit atomically before asynchronous work continues.

Important examples:

### Transfer Completion and Outbox

In one local transaction:

- persist the Transfer completion state;
- persist the corresponding Outbox Message.

After commit:

- an Outbox worker publishes the Integration Event asynchronously.

### Account Balance Reservation

Within the Account and Balance consistency boundary:

- update the Account's available/reserved balance;
- persist the Balance Reservation;
- commit atomically.

### Durable Reconciliation / Retry Scheduling

When a workflow must continue asynchronously:

- persist the recoverable process state/work metadata first;
- commit;
- let a durable worker continue processing after commit/restart.

The exact transaction and Outbox implementations are defined in later implementation decisions.

## Alternatives Considered

### Alternative A — Saga Orchestration

**Strengths**

- Explicit central workflow owner.
- Clear forward and compensating actions.
- Good fit for multi-step workflows.

**Why not selected as the primary term**

The Transfer process includes long-lived ambiguous states, Reconciliation, external status investigation, restart recovery, and manual intervention.

`Process Manager` more directly describes the responsibility of persisting and coordinating this long-running business process rather than only a sequence of distributed transactional steps and compensations.

The selected Process Manager still uses orchestration-style coordination where appropriate.

### Alternative B — Saga Choreography

**Strengths**

- Low direct coupling between participants.
- Natural event-driven integration.
- Useful for non-critical downstream reactions such as Notification.

**Reasons rejected for the Core Transfer workflow**

- end-to-end control flow becomes distributed across handlers;
- recovery ownership becomes less obvious;
- `SubmissionStatusUnknown` and Reconciliation become harder to reason about;
- operational users have no single coordination state to inspect;
- timeout/retry logic risks becoming scattered;
- event chains can become difficult to understand and test.

Choreography remains appropriate for selected downstream Integration Events such as asynchronous Notification after Transfer completion.

### Alternative C — Persistent Workflow State Machine as the Main Coordinator

**Strengths**

- explicit states and legal transitions;
- strong restart recovery;
- easy visibility of current state.

**Why not selected alone**

A state machine answers whether a transition is legal, but does not by itself define ownership of external commands, retry scheduling, Reconciliation, Compensation, or operational recovery.

The Transfer Aggregate therefore uses an explicit state machine **inside** the selected Process Manager architecture.

### Alternative D — Local Transaction with Asynchronous Integration as the Main Strategy

**Strengths**

- simple;
- reliable for local state plus asynchronous event publication;
- effective with Transactional Outbox.

**Reasons rejected as the end-to-end coordinator**

It does not provide sufficient ownership or visibility for the complete long-running Transfer process, particularly:

- external submission ambiguity;
- Reconciliation;
- delayed settlement;
- repeated recovery attempts;
- manual intervention;
- process state after restart.

It remains an important supporting mechanism.

## Consequences

### Positive Consequences

- One explicit owner coordinates the long-running Transfer workflow.
- Workflow state survives application restart.
- Ambiguous external outcomes have a clear recovery owner.
- End-to-end behaviour is easier to understand and observe than a highly choreographed workflow.
- Transfer and Account domain invariants stay in their owning Aggregates.
- Retry, timeout, Reconciliation, Compensation, and manual recovery are modelled explicitly.
- The architecture remains compatible with the Modular Monolith selected in ADR-001.
- Future extraction of modules does not require changing the conceptual workflow owner immediately.

### Negative Consequences

- Additional persistent process state must be designed and maintained.
- Coordination code can grow as workflow variations grow.
- Developers must carefully distinguish process state from domain state.
- Incorrect boundaries could turn the Process Manager into a God Service.
- Recovery and retry paths require more tests than a simple synchronous workflow.
- Long-running process migrations/versioning may become necessary as workflow definitions evolve.

## Risks

### God Service Risk

The Process Manager could accumulate domain logic.

**Mitigation:** keep business invariants in Aggregates/contexts and coordination only in the Process Manager.

### Process State Duplication

Workflow state may duplicate domain truth and drift.

**Mitigation:** persist only coordination state necessary for recovery; treat Aggregate-owned state as authoritative.

### Duplicate Effects

Restart/retry/message redelivery could repeat financial or external effects.

**Mitigation:** operation-specific idempotency, database constraints, external correlation, and idempotent consumers.

### Incorrect Retry Classification

A developer may retry an operation whose outcome is ambiguous.

**Mitigation:** distinguish transient failure from unknown business outcome; specifically prohibit blind Payment Network resubmission after ambiguous timeout.

### Stuck Processes

Persisted workflows may remain indefinitely in intermediate states.

**Mitigation:** state-age monitoring, durable Reconciliation, escalation thresholds, and auditable Operations tooling.

### Workflow Evolution

Changing the process while Transfers are in flight may create compatibility problems.

**Mitigation:** keep persisted state explicit, introduce backwards-compatible transitions or process-version handling if/when required.

## Revisit Conditions

Revisit this decision when:

- the Transfer workflow becomes simple enough that persistent coordination is no longer justified;
- a dedicated workflow engine provides measurable value over the custom Process Manager;
- independently deployed services require a materially different coordination topology;
- the number of workflow variants makes the current coordination model difficult to evolve safely;
- operational evidence shows the Process Manager is becoming an unhealthy coupling point;
- process versioning requirements become too complex for the current implementation;
- choreography provides a demonstrably simpler model for a separated non-critical workflow.

A revisit must preserve the mandatory properties of:

- persisted workflow state;
- safe timeout treatment;
- Reconciliation;
- idempotency;
- restart recovery;
- auditable manual intervention;
- observability.

## Related Decisions

- **ADR-001:** Architecture Style
- **ADR-003:** Account Reservation and Concurrency Strategy
- **ADR-004:** Reliable Messaging and Outbox Strategy
- **ADR-005:** Incremental Legacy Modernisation Strategy
