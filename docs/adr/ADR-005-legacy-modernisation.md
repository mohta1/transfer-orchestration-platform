# ADR-005: Incremental Legacy Modernisation Strategy

- **Status:** Accepted
- **Decision Date:** 2026-08-07
- **Decision Owners:** Backend Engineering / Architecture
- **Scope:** Migration from the Legacy Banking System to the new Transfer Orchestration Platform

## Context

The organisation has an existing Legacy Banking System that currently participates in transfer processing and related banking capabilities.

The target architecture introduces a new Transfer Orchestration Platform with explicit domain boundaries, durable process coordination, safe balance reservation, Transactional Outbox, Reconciliation, and operational recovery.

A full replacement of the Legacy Banking System is explicitly outside the challenge scope.

The modernisation strategy must therefore:

- avoid a big-bang rewrite;
- allow old and new capabilities to coexist;
- preserve financial correctness during migration;
- provide controlled traffic migration;
- provide measurable rollback points;
- prevent duplicate financial execution;
- protect the new domain model from Legacy structures and terminology;
- support historical-data coexistence;
- support Reconciliation between old and new views where required;
- provide clear criteria for final Legacy decommissioning.

ADR-001 selected an Incremental Hybrid system architecture:

- Legacy capabilities remain operational during migration;
- the new Transfer Orchestration capability is implemented as a Modular Monolith;
- migration occurs incrementally through explicit routing/integration boundaries.

This ADR defines the migration strategy in detail.

## Decision

Use a **Strangler Fig migration strategy**.

The first business capability to be migrated is:

> **New Domestic Interbank Transfer Submission and its orchestration workflow**

The migration uses:

- explicit routing / feature toggles;
- single ownership per Transfer;
- no financial dual-write between Legacy and New;
- Anti-Corruption Layers for Legacy integration;
- shadow/parallel comparison without duplicate financial side effects;
- controlled canary rollout;
- Reconciliation and operational comparison;
- phased historical-data migration;
- traffic rollback for new requests;
- owner-retention for in-flight Transfers;
- explicit decommissioning criteria.

## Core Ownership Rule

Every Transfer has exactly one workflow owner.

### Legacy-Owned Transfer

A Transfer created and accepted by the Legacy Banking System remains Legacy-owned until it reaches a terminal state.

### New-Owned Transfer

A Transfer created and accepted by the New Transfer Orchestration Platform remains New-owned until it reaches a terminal state.

Ownership does not casually move between systems while the Transfer is in flight.

This rule is required to prevent:

- duplicate Balance Reservation;
- duplicate Payment Network submission;
- duplicated settlement handling;
- conflicting workflow state;
- ambiguous recovery ownership.

## Rollback Rule

Rollback changes **routing for future/new Transfer requests**.

Rollback does not move an already in-flight Transfer from one owner to the other.

Example:

If the new platform is handling 10% of new Transfers and a defect is detected:

- feature routing for new Transfers can be switched back to Legacy;
- already New-owned Transfers continue under New ownership;
- they complete, Reconcile, or enter authorised manual recovery in the new platform;
- they are not automatically resubmitted through Legacy.

This is a traffic rollback, not a time-travel/database rollback.

## First Capability to Extract

The first migrated capability is the **Domestic Interbank Transfer submission flow** because it is:

- the Core Domain of the challenge;
- where the highest-value correctness/resilience improvements exist;
- already modelled through the new Process Manager;
- already protected by explicit Idempotency and concurrency mechanisms;
- the natural place to introduce safe Payment Network timeout and Reconciliation behaviour;
- small enough to migrate incrementally without replacing the whole bank.

The first production slice should focus on a controlled cohort of eligible new Transfers.

Other Legacy banking capabilities remain in place until explicitly migrated.

## Strangler Routing Boundary

A routing boundary decides whether a new eligible Transfer enters:

- Legacy; or
- the New Transfer Orchestration Platform.

Routing may use controlled criteria such as:

- Transfer Type;
- Submission Channel;
- customer cohort;
- destination-bank cohort;
- explicitly configured percentage;
- operational allowlist/denylist.

The routing decision is recorded for auditability and Reconciliation.

The same logical request must not be routed to both systems for real financial execution.

## Feature Toggles

Feature toggles provide controlled rollout and rollback.

Examples:

- enable New platform for selected channels;
- enable New platform for Domestic Interbank only;
- enable for a customer cohort;
- enable by percentage;
- disable new routing immediately if safety thresholds are breached.

Feature-toggle state is operational configuration.

It must not silently change ownership of already in-flight Transfers.

## No Financial Dual-Write

The design explicitly rejects synchronous financial dual-write such as:

1. create/update Transfer in New;
2. create/update the same financial Transfer in Legacy;
3. require both writes to succeed.

This creates an unsafe consistency problem:

- New succeeds / Legacy fails;
- Legacy succeeds / New fails;
- external submission occurs only on one side;
- recovery becomes ambiguous.

Instead, each Transfer has one authoritative execution owner.

Cross-system information required for coexistence is shared through explicit integration, read models, Reconciliation records, or controlled data replication where appropriate.

## System of Record During Migration

Ownership is capability/Transfer-specific.

### New Transfers Routed to Legacy

Legacy remains authoritative for:

- lifecycle;
- financial execution;
- external submission;
- terminal outcome.

### New Transfers Routed to New Platform

The New platform is authoritative for:

- Transfer workflow state;
- new Account/Reservation model used by that workflow;
- Payment Network orchestration through the new integration boundary;
- Reconciliation state;
- Outbox publication;
- operational recovery.

This avoids two active writers for the same workflow.

## Legacy Integration

The new domain model must not query or mutate arbitrary Legacy database tables directly.

Integration occurs through explicit boundaries.

Preferred order:

1. supported Legacy API / service interface;
2. purpose-built Legacy adapter;
3. Anti-Corruption Layer;
4. CDC integration where Legacy modification/API access is not feasible;
5. polling only as a temporary last-resort bridge.

## Anti-Corruption Layer

An Anti-Corruption Layer protects the new Ubiquitous Language from Legacy concepts.

Responsibilities may include:

- field/name translation;
- Legacy status mapping;
- account identifier translation;
- error translation;
- protocol conversion;
- missing/default-value handling;
- Legacy response classification.

Example:

Legacy:

`acct_stat = "A"`

New domain:

`AccountStatus.Active`

The new domain must not adopt Legacy naming merely to simplify integration.

## Event Publication from Legacy Transactions

The preferred mechanism depends on how safely Legacy can be changed.

### Preferred: Legacy Transactional Outbox

If Legacy can be safely modified:

1. Legacy business state changes;
2. Legacy Outbox record is inserted in the same transaction;
3. transaction commits;
4. a publisher exposes the Integration Event.

This provides the strongest publication reliability.

### Fallback: Change Data Capture

If Legacy application modification is unsafe or impractical:

- CDC observes committed Legacy database changes;
- an adapter maps technical changes to stable Integration Events;
- events are correlated and deduplicated;
- operational lag/failure is monitored.

CDC-derived events must not simply expose raw database row changes as domain contracts.

### Temporary Last Resort: Polling

Polling may be used only as a transitional bridge where neither reliable Legacy Outbox nor CDC is currently feasible.

Polling requires:

- persisted cursor/watermark;
- idempotent processing;
- missed-record detection;
- Reconciliation;
- operational monitoring.

Polling is not the preferred long-term integration mechanism.

## Parallel Run Strategy

Parallel run must not mean two systems execute the same financial Transfer.

The preferred parallel stage is **Shadow / Comparison Mode**.

Example:

Legacy remains Primary for a cohort:

- Legacy performs the real financial workflow;
- New platform may evaluate/mirror non-side-effect decisions for comparison;
- New platform must not create a real duplicate Reservation;
- New platform must not submit a real duplicate Payment Network request;
- New platform must not settle the same Transfer.

Comparison may include:

- validation outcome;
- routing decision;
- limit result where safe;
- fraud integration mapping;
- expected lifecycle/state classification;
- response/latency/error comparison.

Once confidence is established, selected Transfers move to New as Primary through the router.

## Canary Rollout

After shadow validation:

- a small controlled cohort is routed to New;
- Legacy remains Primary for the rest;
- each Transfer has exactly one owner;
- operational metrics and Reconciliation are compared;
- rollout percentage increases only when safety criteria are met.

The rollout can progress through stages such as:

- internal/test cohort;
- 1-5%;
- 10-25%;
- larger cohorts;
- 100% of eligible traffic.

Exact percentages are deployment decisions, not permanent architecture rules.

## Reconciliation During Migration

Reconciliation is mandatory because old and new systems may temporarily expose overlapping operational/history views.

Migration Reconciliation must be able to compare appropriate control data such as:

- Transfer identifier / mapping;
- owning system;
- amount;
- currency;
- source/destination safe identifiers;
- external submission reference;
- current/terminal status;
- settlement status;
- timestamps;
- final result.

Examples of mismatch requiring investigation:

- routing says New owns a Transfer but Legacy also performed financial execution;
- one system reports Settled while the authoritative owner reports Unknown;
- the same external reference appears against multiple logical Transfers;
- a migrated historical record has inconsistent amount/status;
- expected Integration Event is missing.

A mismatch is not automatically corrected by copying one database over the other.

Authoritative ownership and business evidence determine the recovery action.

## Historical Data Strategy

Full historical migration is not a prerequisite for enabling the new Transfer capability.

Initial strategy:

- Legacy history remains readable from Legacy;
- new Transfers are stored in New PostgreSQL;
- a query/facade layer may combine history if product requirements require one logical view.

Historical migration is phased.

Potential phases:

1. recent history required for operations/support;
2. history required for reporting/regulatory use;
3. older/archive history only when justified.

Backfill must be:

- restartable;
- idempotent;
- measurable;
- auditable;
- verified using counts/control totals/checks where practical.

Historical migration must not overwrite New-owned live Transfer state.

## Data Ownership and Coexistence

During coexistence:

- a shared identifier/mapping mechanism correlates Legacy and New representations where necessary;
- authoritative ownership is explicit;
- no arbitrary cross-database writes are allowed;
- replicated/read data is clearly labelled as non-authoritative where applicable;
- migration metadata records source, migration time, and ownership.

If one physical database platform is shared temporarily, schema/table ownership remains explicit.

## Payment Network Migration

Payment Network integration should move behind the new Payment Network Integration boundary.

Migration sequence:

1. document current Legacy network behaviour;
2. introduce the new Anti-Corruption Layer/adapter;
3. validate request/response mapping;
4. validate external reference/idempotency behaviour;
5. shadow/compare where safe;
6. route New-owned Transfers through the new adapter;
7. retain Legacy path for Legacy-owned in-flight Transfers until drained;
8. decommission Legacy Payment integration only when no longer required.

A Payment Network timeout remains ambiguous and must follow the new `SubmissionStatusUnknown -> Reconciliation` model for New-owned Transfers.

## Account and Reservation Coexistence

Account/balance migration is safety-critical.

The implementation must not create two independent authorities that can both reserve the same funds without coordination.

The exact enterprise integration with the Legacy account ledger is outside the challenge's full-accounting scope.

For the challenge architecture:

- the new Account/Reservation model demonstrates the required concurrency/invariant behaviour;
- production migration would require an explicit source-of-truth decision for available balance;
- the new platform must use a controlled account interface/ACL rather than unsynchronised duplicated balances;
- cutover of balance authority would require separate reconciliation and financial controls.

The challenge implementation must not pretend a full banking ledger migration has been solved.

## Operational Observability

Migration-specific telemetry must include:

- current routing percentage/cohort;
- Transfers created by Legacy vs New;
- ownership mismatches;
- shadow comparison mismatches;
- New vs Legacy error rate;
- New vs Legacy latency where comparable;
- Payment Network timeout rate;
- Reconciliation backlog;
- stuck New-owned Transfers;
- rollback events;
- unexpected Legacy writes after cutover;
- historical migration progress;
- CDC/Legacy Outbox lag where used.

Migration decisions must be evidence-based.

## Rollback Triggers

Potential rollback triggers include:

- financial invariant violation;
- duplicate external submission;
- unexplained settlement mismatch;
- excessive `SubmissionStatusUnknown` growth;
- unacceptable error-rate regression;
- severe latency regression;
- unrecoverable Reconciliation backlog;
- critical observability gap;
- security/privacy issue.

Not every transient dependency failure requires full migration rollback because the new platform already has retry/Reconciliation mechanisms.

## Rollback Procedure

When rollback is required:

1. disable New routing for future eligible Transfers;
2. route new requests to Legacy;
3. preserve ownership of existing New-owned in-flight Transfers;
4. continue durable New processing where safe;
5. Reconcile ambiguous New-owned Transfers;
6. escalate unresolved financial cases to Operations;
7. preserve audit evidence;
8. diagnose/correct the defect;
9. resume rollout only after exit criteria are met.

Rollback must not resubmit in-flight New-owned Transfers through Legacy.

## Migration Phases

### Phase 1 — Domain Discovery and Observability

Goals:

- understand Legacy transfer behaviour;
- identify dependencies;
- capture baseline metrics;
- document hidden business rules;
- identify data/security issues;
- establish correlation identifiers and operational visibility.

Outputs:

- current-state map;
- dependency inventory;
- baseline latency/error metrics;
- known data-quality issues;
- migration risks.

### Phase 2 — Boundary Isolation

Goals:

- introduce routing boundary;
- introduce Legacy ACL/adapters;
- establish ownership markers;
- prevent new code from directly coupling to Legacy schema;
- establish event-integration mechanism.

Outputs:

- feature-toggle/routing mechanism;
- ACL contracts;
- owner mapping;
- Legacy Outbox/CDC plan;
- Reconciliation controls.

### Phase 3 — New Transfer Submission Capability

Goals:

- enable New Domestic Interbank Transfer workflow for controlled cohort;
- exercise Idempotency, Reservation, Process Manager, and Outbox;
- maintain Legacy as default for non-migrated traffic.

Rollout:

- shadow validation;
- small canary;
- measured percentage increase;
- rollback if thresholds fail.

### Phase 4 — External Integration Migration

Goals:

- route New-owned Transfers through the new Payment Network Integration context;
- validate external reference handling;
- validate timeout/recovery behaviour;
- drain Legacy-owned in-flight external submissions.

### Phase 5 — Reconciliation and Operations Migration

Goals:

- move recovery workflows to the new operational model;
- make stuck/unknown Transfers visible;
- provide auditable manual recovery;
- validate historical/operational views;
- reduce dependency on Legacy operational tooling.

### Phase 6 — Legacy Transfer Decommissioning

Goals:

- route all eligible new Transfer traffic to New;
- drain Legacy-owned in-flight Transfers;
- remove obsolete Legacy transfer routes;
- archive/retain history according to policy;
- remove temporary bridges only after evidence shows they are unnecessary.

## Decommissioning Criteria

Legacy Transfer capability can be decommissioned only when all agreed criteria are satisfied.

At minimum:

- 100% of eligible new Transfer traffic routes to New;
- no unresolved ownership conflicts exist;
- Legacy-owned in-flight Transfers are terminal/drained;
- no unintended Legacy financial writes occur for New-owned Transfers;
- Reconciliation mismatch backlog is within agreed zero/acceptable threshold;
- critical New error/latency SLOs are met over an agreed observation window;
- Payment Network timeout/recovery path has been proven operationally;
- Outbox/restart recovery has been proven;
- Operations can investigate and recover stuck Transfers;
- monitoring and alerts are active;
- audit/security requirements are satisfied;
- historical-data access requirements are covered;
- rollback window/decision is formally closed;
- stakeholders approve retirement.

Exact numeric thresholds and observation windows require business/operations agreement.

## Alternatives Considered

### Alternative A — Big-Bang Replacement

Replace the complete Legacy Transfer capability and related dependencies in one release.

**Strengths**

- shortest coexistence period;
- clean end-state if successful;
- fewer transitional integrations after cutover.

**Reasons rejected**

- high financial/operational risk;
- difficult rollback;
- hidden Legacy behaviour may be missed;
- requires wider scope than the challenge;
- violates the incremental-modernisation objective;
- increases probability of prolonged release delay.

### Alternative B — Permanent Dual-Write

Write every Transfer to both Legacy and New systems.

**Potential strength**

- both databases appear current.

**Reasons rejected**

- no atomic transaction across both systems;
- partial-write failure creates ambiguous authority;
- duplicate financial side effects become easier;
- recovery is complex;
- encourages two systems of record.

### Alternative C — Immediate Full Historical Migration

Migrate all historical Transfer data before enabling New traffic.

**Potential strength**

- one complete data store at cutover.

**Reasons rejected**

- delays delivery of the Core capability;
- increases migration scope/risk;
- historical quality issues may dominate the project;
- most old data is not required for executing new Transfers.

### Alternative D — Route In-Flight Transfers During Rollback

Move an already New-owned Transfer back to Legacy when rollback occurs.

**Potential strength**

- appears to centralise execution back in Legacy.

**Reasons rejected**

- duplicate reservation/submission risk;
- incompatible process states;
- difficult transfer of external references/retry history;
- ambiguous settlement ownership;
- unsafe for financial workflows.

## Consequences

### Positive Consequences

- migration risk is incremental and measurable;
- new capability can deliver value before full Legacy retirement;
- financial execution retains one owner per Transfer;
- rollback is fast for new traffic;
- New domain model is protected from Legacy pollution;
- migration decisions can use evidence from shadow/canary phases;
- historical migration does not block core delivery;
- the strategy aligns with the Hybrid architecture in ADR-001;
- Reconciliation and Operations are built into migration rather than added after failure.

### Negative Consequences

- Legacy and New operate simultaneously for a period;
- routing and ownership metadata must be maintained;
- operational teams must understand two systems during transition;
- ACL/CDC/Outbox bridges add temporary complexity;
- reporting/history may need federated views;
- some duplicate data may exist for read/comparison purposes;
- decommissioning requires disciplined completion rather than simply turning off Legacy.

## Risks

### Duplicate Financial Execution

A request could accidentally execute in both systems.

**Mitigation:** single-owner rule, routing audit, shared external correlation where appropriate, Reconciliation, and no real financial shadow execution.

### Ownership Ambiguity

Operators may not know which system is authoritative.

**Mitigation:** explicit ownership metadata, routing record, searchable operational view.

### Legacy Model Leakage

New code may adopt Legacy terminology/schema.

**Mitigation:** ACL and explicit translation contracts.

### CDC Semantic Leakage

CDC may expose database changes as if they were business events.

**Mitigation:** adapter translates committed changes into stable Integration Events and maintains deduplication/correlation.

### Rollback Misuse

Rollback could incorrectly resend in-flight work through Legacy.

**Mitigation:** rollback changes routing only for new requests; in-flight ownership is immutable except through an explicit separately approved migration procedure.

### Historical Data Inconsistency

Backfilled records may differ from Legacy truth.

**Mitigation:** restartable migration, control totals, counts, reconciliation, ownership/source metadata.

### Long Coexistence

Temporary migration architecture could become permanent.

**Mitigation:** phase exit criteria, explicit technical-debt ownership, decommissioning milestones, management visibility.

### Account Authority Ambiguity

Duplicated balances could create unsafe reservation behaviour.

**Mitigation:** explicit source-of-truth decision for production migration; avoid unsynchronised dual balance authority; treat the challenge Account model as the new target consistency design, not proof of full ledger migration.

## Security and Compliance Considerations

Migration must not increase sensitive-data exposure.

Requirements include:

- ACL contracts expose only required data;
- logs must not contain raw account numbers unnecessarily;
- historical extracts must be access-controlled;
- migration/backfill files must not become unmanaged data copies;
- manual migration/recovery actions must be auditable;
- secrets/credentials for Legacy integration are managed securely.

## Team and Delivery Implications

Migration ownership must be cross-functional.

Suggested ownership:

- Transfer team: new workflow and routing behaviour;
- Legacy SME/team: Legacy behaviour and safe integration points;
- Platform/DB: CDC/Outbox/infrastructure where applicable;
- Operations: Reconciliation and rollback procedures;
- Security: data exposure and access controls;
- Product/Business: cohort selection and rollout acceptance criteria.

The Tech Lead should not be the only person capable of authorising or understanding migration decisions.

## Testing Implications

Migration tests should cover:

### Routing

- eligible request routes to expected owner;
- feature toggle changes only future routing;
- one request is never financially executed by both owners.

### Ownership

- New-owned Transfer remains New-owned through restart/rollback;
- Legacy-owned Transfer is not imported into active New execution accidentally.

### Shadow Mode

- shadow path produces no real Reservation;
- shadow path produces no real external Payment submission.

### Reconciliation

- ownership mismatch is detected;
- status mismatch is surfaced rather than silently overwritten.

### Rollback

- disabling New routing sends new requests to Legacy;
- existing New in-flight workflow remains durable and recoverable.

### Historical Backfill

- repeated batch is idempotent;
- migrated counts/control totals are verifiable;
- live New records are not overwritten.

## Revisit Conditions

Revisit this strategy when:

- Legacy exposes substantially better/worse integration capabilities than assumed;
- regulatory requirements constrain shadow/canary operation;
- a full authoritative Account/Ledger migration is planned;
- transaction volume makes federated Reconciliation too expensive;
- product requires immediate unified historical access;
- coexistence duration becomes operationally unacceptable;
- organisational ownership changes allow faster capability retirement;
- new evidence shows a specific capability should be migrated before Domestic Interbank Transfer;
- Legacy decommissioning is complete.

When Legacy Transfer migration is complete, the system-level Hybrid classification in ADR-001 may also be revisited.

## Related Decisions

- **ADR-001:** Architecture Style
- **ADR-002:** Transfer Process Coordination Strategy
- **ADR-003:** Account Reservation and Concurrency Strategy
- **ADR-004:** Reliable Messaging and Outbox Strategy
