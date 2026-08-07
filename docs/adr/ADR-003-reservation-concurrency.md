# ADR-003: Account Reservation and Concurrency Strategy

- **Status:** Accepted
- **Decision Date:** 2026-08-07
- **Decision Owners:** Backend Engineering / Architecture
- **Scope:** Account and Balance Management, with cross-cutting concurrency guidance for related persistence races

## Context

The platform must preserve financial and workflow invariants under genuinely concurrent requests.

The most important financial race is concurrent balance reservation against the same source Account.

Example:

- Available Balance: `£1,000`
- Transfer A requests: `£750`
- Transfer B requests: `£600`

Both requests may read the same initial balance concurrently.

The required outcome is:

- the Account never reserves `£1,350`;
- at most one reservation succeeds;
- the losing request receives a business-relevant result;
- Account invariants remain valid.

The challenge also requires concurrency protection for:

- Transfer state transitions;
- Balance Reservation;
- Idempotency Records;
- external-result processing;
- Outbox message claiming.

The Event Storming and domain baseline establish that:

- `Account` is the financial consistency and concurrency boundary for reservation;
- Available Balance must never become negative;
- one Transfer must not create more than one active Reservation;
- Balance Reservation is persisted separately for auditability;
- reserve / release / consume behaviour belongs to Account and Balance Management;
- database constraints provide final protection for critical uniqueness/invariants;
- optimistic concurrency is the preferred baseline assumption.

## Decision

Use **Optimistic Concurrency as the default strategy for business Aggregate updates**, reinforced by **database-enforced constraints** and **short local database transactions**.

Do **not** introduce distributed locking initially.

Use narrow database-level pessimistic row locking only where it is operationally appropriate and does not replace database constraints, such as competing workers claiming Outbox records.

### Core Decision

For `Account` reservation:

1. load the Account with its concurrency version;
2. evaluate reservation business rules in the Account Aggregate;
3. update balance state using optimistic concurrency;
4. insert/update the Balance Reservation in the same local transaction;
5. rely on database constraints as the final safety net;
6. commit atomically;
7. if optimistic concurrency loses, reload current state and re-evaluate the business operation;
8. retry only when the operation is still valid and the retry policy permits it.

## Protected Resource

### Account

The primary protected resource is the **source Account balance state**:

- Available Balance
- Reserved Balance
- Account status
- Account currency
- concurrency version

The Account is the concurrency boundary because multiple Transfers may compete for the same available funds.

### Balance Reservation

Reservation persistence must protect:

- uniqueness for a Transfer;
- valid Reservation lifecycle;
- safe Release and Consume;
- prevention of duplicate financial holds.

The Reservation record may be stored separately, but reserve/release/consume mutations occur within the Account and Balance consistency boundary.

## Account Reservation Transaction

Conceptually, a successful reservation uses one short local transaction:

1. read Account with concurrency token;
2. verify Account is active;
3. verify Account currency matches the Transfer currency;
4. verify requested amount is positive;
5. verify sufficient Available Balance;
6. verify no Reservation already exists for the Transfer;
7. execute domain behaviour equivalent to `account.Reserve(transferId, amount)`;
8. persist updated Account balance/version;
9. persist Balance Reservation;
10. commit.

For the selected balance model:

`Reserve(amount)`:

- `AvailableBalance -= amount`
- `ReservedBalance += amount`

The transaction must not include slow external calls such as Fraud Screening or Payment Network submission.

## Concurrent Reservation Scenario

Initial state:

- Available Balance = `£1,000`
- Reserved Balance = `£0`
- Version = `10`

Transfer A requests `£750`.

Transfer B requests `£600`.

Both may initially read Version `10`.

### Winning Request

Assume A commits first:

- Available Balance becomes `£250`
- Reserved Balance becomes `£750`
- Version changes from `10` to `11`
- Reservation A is inserted
- transaction commits

### Losing Request

B attempts to update the Account using the stale Version `10`.

The update affects zero rows / EF Core reports an optimistic concurrency conflict.

B must not assume the original balance is still valid.

The application:

1. reloads the Account;
2. observes Available Balance = `£250`;
3. re-evaluates the requested `£600` reservation;
4. receives `InsufficientBalance`;
5. returns a business-relevant result.

The losing request does **not** reserve funds and does not produce a negative balance.

## Losing Request Behaviour

A concurrency conflict is not automatically the final client/business response.

After conflict, the application must distinguish between:

### Case A — Business Condition Changed

Example:

- original Available Balance = `£1,000`;
- another Transfer commits;
- reloaded Available Balance = `£250`;
- request requires `£600`.

Result:

`InsufficientBalance`

This is the business-relevant final outcome.

### Case B — Operation Remains Valid

Example:

- original Available Balance = `£1,000`;
- another operation reserves `£100`;
- reloaded Available Balance = `£900`;
- request requires `£200`.

A bounded retry may be attempted after reload/re-evaluation.

### Case C — Retry Budget Exhausted / Repeated Contention

If repeated optimistic conflicts occur:

- do not retry indefinitely;
- stop according to a bounded policy;
- expose a consistent concurrency/retryable response where appropriate;
- record/measure the conflict.

Exact retry count is configuration/implementation policy and is not fixed permanently by this ADR.

## Retry Safety

Retry is safe only when:

- the latest state has been reloaded;
- all relevant domain invariants are re-evaluated;
- the operation itself is idempotent or otherwise protected from duplicate financial effects;
- the retry count is bounded.

Retry must not reuse stale business assumptions.

The retry loop must not be tight or unbounded.

For the Reservation operation, duplicate protection by Transfer identifier remains active during retry.

## Database Constraints — Final Protection

Application-level domain checks and optimistic concurrency are not considered sufficient by themselves.

The database provides final protection.

### Account Checks

At minimum:

- `AvailableBalance >= 0`
- `ReservedBalance >= 0`

Equivalent database check constraints must prevent invalid persisted balance values.

### Reservation Uniqueness

A Transfer must not create more than one active financial hold.

The implementation must enforce uniqueness by Transfer at the database level.

For the challenge slice, the preferred simple model is:

- one persisted Reservation record per Transfer;
- unique constraint/index on `TransferId`;
- Reservation status tracks `Active`, `Consumed`, `Released`, or `Expired`.

This is stricter and simpler than allowing multiple historical rows per Transfer.

If a future design requires multiple historical Reservation records per Transfer, the uniqueness rule must be redesigned explicitly, for example using an active-reservation uniqueness mechanism supported by the selected database.

### Referential / Domain Integrity

Where practical:

- Reservation references a valid Account identifier;
- amount must be positive;
- persisted currency/value representations must be constrained appropriately.

Database constraints are the last line of defence and must remain present even if application code is correct.

## Reservation Idempotency

Duplicate calls to reserve for the same `TransferId` must not create a second financial hold.

Possible safe behaviour:

- return the existing equivalent Reservation as an idempotent result; or
- reject the duplicate as a domain conflict.

For the challenge implementation, the preferred behaviour is:

- same Transfer + same reservation intent -> return/recognise existing Reservation without changing balances again;
- same Transfer + conflicting reservation amount/account -> reject and audit/log the conflict.

The database unique constraint on `TransferId` provides final protection under races.

## Reservation Release and Consumption

Reservation lifecycle transitions are also protected.

### Release

An active Reservation may be released when the Transfer is definitively rejected/cancelled according to the workflow.

Effect:

- `ReservedBalance -= amount`
- `AvailableBalance += amount`
- Reservation becomes `Released`

A second Release must not move money twice.

### Consume

A settled Reservation is consumed.

Effect:

- `ReservedBalance -= amount`
- Available Balance is not restored
- Reservation becomes `Consumed`

A second Consume must not move money twice.

### Invalid Lifecycle Races

Examples:

- Consume and Release arriving concurrently;
- two Release commands;
- two settlement confirmations causing duplicate Consume.

Optimistic concurrency / Reservation state checks plus database transaction protection ensure only one valid terminal transition succeeds.

The loser reloads current Reservation state and is handled idempotently or rejected as an invalid transition without repeating the financial effect.

## Reservation Expiration

The challenge requires Reservation expiration to be considered, but does not define the exact business policy.

Therefore:

- expiration status is supported by the model;
- expiry timing/policy remains configurable / business-confirmed;
- automatic expiry must use the same concurrency rules as Release/Consume;
- expiration must not race unsafely with settlement consumption;
- an expired Reservation must not silently be revived.

Exact expiry duration is not fixed by this ADR.

## Transfer State Transition Concurrency

`Transfer` also uses optimistic concurrency.

Example:

- Transfer state = `SettlementPending`
- Version = `6`
- two duplicate Settlement Confirmations arrive concurrently.

One update succeeds:

`SettlementPending -> Completed`
`Version 6 -> 7`

The other loses the optimistic concurrency race.

After reload it observes the Transfer is already in the resulting state and must not:

- complete the Transfer twice;
- consume the Reservation twice;
- create an unnecessary duplicate Integration Event.

The duplicate result is handled idempotently according to the command/event semantics.

## Idempotency Record Concurrency

Two identical HTTP requests may arrive simultaneously with the same Idempotency Key.

The system must ensure at most one request begins the business operation.

Concurrency protection includes:

- a database uniqueness constraint over the selected idempotency scope/key;
- atomic creation/claiming of the Idempotency Record;
- detection of same-key/same-fingerprint vs same-key/different-fingerprint;
- an existing or in-progress result for the losing request.

The complete HTTP idempotency lifecycle is implemented separately from this ADR, but database uniqueness is mandatory.

## External-Result Processing Concurrency

External responses may be duplicated or arrive concurrently.

Examples:

- duplicate Settlement Confirmation;
- reconciliation result arriving near an external callback;
- repeated accepted/rejected response.

Concurrency protection must ensure:

- only legal Transfer transitions commit;
- financial side effects occur once;
- stale results cannot overwrite newer authoritative state;
- duplicated results can be recognised safely.

Optimistic concurrency on Transfer/process state plus idempotent side-effect protection is the default approach.

## Outbox Claiming

Outbox claiming is a worker-coordination problem rather than a financial Aggregate update.

Multiple worker instances must be able to process different pending messages concurrently without intentionally blocking one another.

For PostgreSQL, a narrow database-level claiming strategy such as:

`SELECT ... FOR UPDATE SKIP LOCKED`

may be used during claim/lease acquisition.

This is intentionally different from using pessimistic locking as the default business Aggregate strategy.

The detailed Outbox claim model, lease/lock fields, retry/backoff, and publication semantics are defined in **ADR-004**.

## Why Optimistic Concurrency Was Selected

Optimistic concurrency is preferred because:

- most Accounts are not expected to experience constant high-contention writes;
- transactions remain short;
- no long-lived lock is held while application/domain logic executes;
- it integrates naturally with EF Core concurrency tokens;
- stale writes are explicitly detectable;
- the selected Modular Monolith and PostgreSQL architecture can enforce final safety in the same database;
- it keeps normal throughput high while preserving correctness;
- genuine concurrency conflicts remain observable and testable.

The design accepts that hot Accounts may experience retries/conflicts.

## Why Pessimistic Locking Was Not Selected as the Default

Pessimistic row locking can provide straightforward serialisation but is not selected globally because:

- competing operations block rather than fail fast;
- lock duration and transaction design become more operationally sensitive;
- deadlock risk increases when multiple resources are involved;
- long or accidental transactions can reduce throughput;
- most Accounts should not need permanent serialised access.

Pessimistic locking remains an available targeted tool if measured contention later justifies it.

## Why Distributed Locking Was Rejected

Distributed locking is not required for the initial architecture.

The new capability is a Modular Monolith backed by PostgreSQL, and the financial truth already resides in the database.

Adding Redis or another distributed-lock service would introduce:

- another infrastructure dependency;
- lock acquisition/release failure modes;
- TTL/lease-expiration concerns;
- network partition concerns;
- crash/ownership edge cases;
- additional operational complexity.

A distributed lock also does not replace database constraints.

Because the database must remain the final authority for financial invariants, adding a distributed lock initially would provide limited value for substantial complexity.

## Alternative — Pessimistic Account Row Lock

Example:

`SELECT Account ... FOR UPDATE`

### Strengths

- directly serialises competing reservations for one Account;
- simple mental model for financial mutation;
- losing request naturally sees latest balance after lock release.

### Weaknesses

- blocking;
- deadlock/lock-timeout risk;
- throughput degradation under long transactions;
- greater sensitivity to transaction boundaries.

### Decision

Not selected as the default.

Revisit only if measured contention makes optimistic conflict/retry materially more expensive than short row-level serialisation.

## Alternative — Distributed Lock Per Account

Example:

`lock:account:{accountId}`

### Strengths

- coordinates across application instances before database mutation.

### Weaknesses

- extra infrastructure;
- lease/TTL correctness complexity;
- cannot be the final financial guarantee;
- failure modes can diverge from database state;
- unnecessary for the current topology.

### Decision

Rejected initially.

## Alternative — Database Atomic Conditional Update Only

Example conceptually:

`UPDATE Account SET Available = Available - amount ... WHERE Available >= amount`

### Strengths

- highly efficient;
- can prevent negative balance in one statement.

### Weaknesses

- business behaviour can move into persistence-specific SQL;
- Reservation lifecycle/uniqueness still requires coordinated persistence;
- domain intent becomes less explicit;
- release/consume flows still require a complete model.

### Decision

May be considered as an optimisation later, but it is not the primary domain design for the challenge.

## Consistency, Contention, Aggregate Size, Transaction Frequency, Scalability, Auditability

### Consistency

Account and Reservation financial mutation is strongly consistent within one local transaction.

No external network call participates in that transaction.

### Contention

Contention is localised to Accounts being mutated.

Optimistic concurrency avoids serialising unrelated Accounts.

### Aggregate Size

Account remains a focused balance/concurrency Aggregate.

Reservation records are persisted separately rather than loading unbounded historical Reservation collections into the Account Aggregate.

### Transaction Frequency

Each reserve/release/consume operation creates one short database transaction.

This keeps lock time and failure scope limited.

### Scalability

Different Accounts can be processed concurrently.

Very hot Accounts may generate repeated optimistic conflicts and are a known revisit trigger.

### Auditability

Persisting Reservation as a first-class record provides:

- Transfer correlation;
- amount;
- lifecycle status;
- creation time;
- release/consume time;
- operational visibility.

## Observability

Concurrency handling must be visible.

Structured logs/metrics should include:

- `AccountId`
- `TransferId`
- operation type: Reserve / Release / Consume
- concurrency conflict count
- retry attempt
- final outcome
- insufficient-balance result
- duplicate-reservation detection
- reservation state conflict
- elapsed transaction time

Sensitive account data must be masked; identifiers must follow the project's logging/security rules.

Suggested metrics:

- reservation attempts;
- successful reservations;
- insufficient-balance failures;
- concurrency conflicts;
- reservation retry count;
- duplicate reservation attempts;
- old active Reservations;
- release/consume conflicts.

## Testing Implications

The challenge requires genuinely concurrent testing.

At minimum:

### Domain Tests

- Account cannot be over-reserved.
- Duplicate Reservation is idempotent or rejected.
- Consumed Reservation cannot be released.
- Release/Consume lifecycle invariants are enforced.

### Integration Tests

#### Concurrent Reservation

Use two genuinely concurrent operations against the same persisted Account.

Given:

- Available Balance = `£1,000`;
- Transfer A = `£750`;
- Transfer B = `£600`.

Assert:

- at most one reservation succeeds;
- persisted Available Balance never becomes negative;
- persisted Reserved Balance does not exceed valid funds;
- exactly one financial hold is created for the winner;
- loser receives `InsufficientBalance` or the appropriate business result after concurrency handling.

The test must not simulate concurrency by merely invoking two methods sequentially.

#### Optimistic Concurrency Conflict

Explicitly create two independently loaded versions of the same Aggregate, commit one, then verify the stale writer is detected and handled correctly.

#### Duplicate Reservation Race

Run two concurrent attempts for the same `TransferId`.

Assert the database uniqueness rule prevents a duplicate persisted Reservation and balance is mutated only once.

## Consequences

### Positive Consequences

- Financial invariants remain protected under concurrency.
- No distributed locking infrastructure is required.
- Unrelated Accounts can mutate concurrently.
- Database constraints provide defence in depth.
- Reservation persistence remains auditable.
- Retry behaviour is explicit and bounded.
- The design integrates naturally with EF Core and PostgreSQL.
- The approach is straightforward to demonstrate with genuine concurrent integration tests.

### Negative Consequences

- Application code must explicitly handle optimistic concurrency exceptions.
- Hot Accounts can create retry/conflict pressure.
- Correct retry logic requires reloading and re-evaluating business rules.
- Developers must understand the difference between duplicate/idempotent outcomes and true concurrency conflicts.
- Database constraints and domain invariants must remain aligned.
- Targeted pessimistic locking may still be required for non-Aggregate worker coordination.

## Risks

### Retry Storm on Hot Accounts

Repeated optimistic conflicts may increase latency.

**Mitigation:** bounded retry, metrics, jitter/backoff where appropriate, and revisit targeted row locking if measured contention becomes high.

### Missing Database Constraint

A code defect could bypass application checks.

**Mitigation:** mandatory check/unique constraints and integration tests that verify them.

### Duplicate Financial Mutation

A duplicate Reserve/Consume/Release could move money twice.

**Mitigation:** Reservation uniqueness, lifecycle state, optimistic concurrency, idempotent command handling, and atomic transactions.

### Large Account Aggregate

Loading a growing Reservation history into the Account Aggregate would hurt performance.

**Mitigation:** persist Reservations separately and keep Account focused on current financial consistency state.

### Stale Retry

Retrying using previously read state could violate current business conditions.

**Mitigation:** reload latest persisted state and rerun domain validation before every retry.

### Pessimistic Lock Leakage

Targeted row locks could accidentally expand into long business transactions.

**Mitigation:** limit pessimistic locking to short infrastructure claiming operations unless a future ADR/revisit explicitly changes Account strategy.

## Revisit Conditions

Revisit the default optimistic strategy when:

- measured conflict rates on hot Accounts materially increase latency or failure rate;
- bounded retries become a significant portion of reservation workload;
- transaction volume or access patterns change substantially;
- Account mutation requires multiple rows/resources whose conflict handling becomes too complex;
- a dedicated ledger model becomes necessary for accounting/audit requirements;
- database sharding or separate Account deployment changes the consistency topology;
- regulatory requirements demand a different financial serialization model.

Possible future alternatives include:

- targeted pessimistic Account row locking;
- atomic conditional SQL updates;
- a dedicated Balance Ledger;
- partitioned/account-owner processing.

Any replacement must continue to provide database-level final protection for financial invariants.

## Related Decisions

- **ADR-001:** Architecture Style
- **ADR-002:** Transfer Process Coordination Strategy
- **ADR-004:** Reliable Messaging and Outbox Strategy
- **ADR-005:** Incremental Legacy Modernisation Strategy
