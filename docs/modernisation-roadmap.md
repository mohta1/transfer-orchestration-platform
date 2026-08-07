# Legacy Modernisation Roadmap

This roadmap operationalises **ADR-005: Incremental Legacy Modernisation Strategy** for the Resilient Interbank Transfer Orchestration Platform.

It follows the challenge-required six-phase migration structure and preserves the agreed safety rules:

- Strangler Fig migration;
- Domestic Interbank Transfer submission is the first capability moved;
- every Transfer has exactly one workflow owner;
- no real financial dual-write between Legacy and New;
- rollback changes routing for new requests only;
- in-flight Transfers remain with their original owner until terminal state;
- Legacy integration is protected by an Anti-Corruption Layer;
- Legacy Transactional Outbox is preferred when feasible, CDC is the fallback, and polling is temporary last resort;
- parallel run uses shadow/comparison behaviour, not duplicate financial execution;
- historical migration is phased;
- Reconciliation is required throughout coexistence;
- Legacy decommissioning occurs only after explicit exit criteria are met.

## 1. Target Migration Outcome

The migration moves Domestic Interbank Transfer orchestration from the Legacy Banking System to the New Transfer Orchestration Platform without a big-bang replacement.

### Initial State

- Legacy owns Transfer submission and execution.
- Legacy owns existing in-flight Transfers.
- New platform is not authoritative for production Transfer execution.

### Transitional State

- Legacy and New coexist.
- A routing boundary decides the owner of each **new** eligible Transfer.
- Each Transfer is financially executed by only one owner.
- New-owned Transfers use the new Process Manager, Account/Reservation model, Payment Network Integration, Reconciliation, Outbox, and Operations model.
- Legacy-owned in-flight Transfers remain in Legacy until terminal.

### Target State

- 100% of eligible new Domestic Interbank Transfers route to New.
- Legacy-owned in-flight Transfers are drained.
- no unintended Legacy financial writes occur for New-owned Transfers;
- operational, Reconciliation, audit, historical-access, and rollback-closure criteria are satisfied;
- the obsolete Legacy Transfer capability can be retired.

## 2. Migration Principles

### 2.1 Single Owner per Transfer

A Transfer is owned by the system that accepted it for real execution.

Ownership remains stable until the Transfer reaches a terminal state.

### 2.2 No Financial Dual-Write

The same logical Transfer must not be executed financially by both Legacy and New.

Shadow comparison must not create a real Reservation, Payment Network submission, or Settlement.

### 2.3 Traffic Rollback, Not Workflow Reassignment

Feature toggles may route future requests back to Legacy.

Already New-owned in-flight Transfers remain in New and complete, Reconcile, or enter authorised manual recovery there.

### 2.4 Anti-Corruption Boundary

New code does not depend directly on arbitrary Legacy database structures or terminology.

Integration is through an explicit Legacy adapter / Anti-Corruption Layer.

### 2.5 Evidence-Based Progression

Each phase advances only after measurable exit criteria are met.

Migration progress is driven by correctness, Reconciliation results, operational evidence, and rollback readiness rather than a fixed calendar date.

---

# Phase 1 — Domain Discovery and Observability

## Goal

Understand actual Legacy Transfer behaviour and establish enough observability to migrate safely.

## Key Activities

- Map the current Legacy Transfer flow and dependencies.
- Identify hidden business rules and manual operational steps.
- Document current Payment Network behaviour and external references.
- Identify Account/balance source-of-truth dependencies.
- Capture existing timeout, retry, and failure behaviour.
- Review current logging for sensitive-data exposure.
- Establish baseline metrics:
  - request volume;
  - success/rejection rates;
  - Payment Network timeout rate;
  - settlement delay;
  - error rate;
  - latency;
  - manual-recovery volume.
- Introduce/standardise safe correlation identifiers where feasible.
- Identify data-quality and historical-data issues.
- Determine whether Legacy can support:
  - a Transactional Outbox;
  - CDC;
  - only polling.

## Deliverables

- Current-state dependency map.
- Legacy behaviour inventory.
- Baseline operational metrics.
- Known migration risks.
- Legacy integration capability assessment.
- Initial data classification/security findings.

## Exit Criteria

- Critical Legacy dependencies are known.
- The current financial execution path is understood sufficiently to avoid accidental duplicate execution.
- Required baseline metrics and logs are available.
- A feasible Legacy event-integration method has been selected or ranked.
- Blocking sensitive-data issues are identified and owned.

## Rollback Point

No production routing has moved yet.

Rollback consists only of disabling discovery instrumentation that causes unexpected impact.

---

# Phase 2 — Boundary Isolation

## Goal

Create the technical boundaries required for safe coexistence before moving real Transfer ownership.

## Key Activities

- Introduce a routing / feature-toggle boundary for new Transfer requests.
- Introduce the Legacy Anti-Corruption Layer.
- Define explicit Legacy/New ownership metadata.
- Prevent New modules from directly coupling to Legacy schema.
- Define routing audit records.
- Establish a Legacy Integration Event mechanism:
  1. Legacy Transactional Outbox where feasible;
  2. CDC adapter as fallback;
  3. polling only as temporary bridge.
- Establish Reconciliation data needed to compare ownership/status.
- Define safe external-reference mapping.
- Prepare shadow/comparison execution path with financial side effects disabled.
- Define rollback switches and operational runbook.

## Deliverables

- Routing contract.
- Feature-toggle configuration.
- Legacy ACL contract.
- Ownership model.
- Reconciliation comparison model.
- Legacy event bridge.
- Shadow-mode controls.
- Rollback runbook.

## Exit Criteria

- A request can be deterministically assigned to one owner.
- Routing decisions are auditable.
- New code does not require arbitrary direct Legacy-table access.
- Shadow execution cannot Reserve funds or submit a real Payment.
- Operators can identify whether a Transfer is Legacy-owned or New-owned.
- New routing can be disabled without moving in-flight ownership.

## Rollback Point

All real financial execution remains in Legacy.

The new boundary can be disabled without transferring workflow state.

---

# Phase 3 — New Transfer Submission Capability

## Goal

Move the first real business capability: **new Domestic Interbank Transfer submission and orchestration**.

## Key Activities

- Enable the New platform for a controlled eligible cohort.
- Use the New API and HTTP Idempotency model.
- Execute:
  - validation;
  - Customer Authorisation integration;
  - Daily Limit evaluation;
  - Fraud Screening;
  - Balance Reservation;
  - persisted Process Manager state;
  - Transactional Outbox.
- Maintain Legacy as owner for non-migrated traffic.
- Start with shadow comparison before real canary ownership.
- Progress through controlled canary cohorts.
- Compare:
  - validation outcomes;
  - rejection reasons;
  - latency;
  - failure rates;
  - Reservation outcomes;
  - workflow state.
- Exercise restart recovery and duplicate-request behaviour.
- Verify one Transfer is never financially owned by both systems.

## Exit Criteria

- New-owned Transfers preserve all mandatory financial invariants.
- Concurrent Reservation tests and production controls behave as designed.
- Idempotency prevents duplicate Transfer creation.
- Process state survives restart.
- Outbox backlog/retry behaviour is operationally visible.
- No unexplained ownership mismatch exists.
- Canary metrics remain inside agreed thresholds.
- Rollback procedure has been tested.

## Rollback Point

Disable New routing for future requests.

Existing New-owned Transfers remain in New until terminal/recovered.

---

# Phase 4 — External Integration Migration

## Goal

Move New-owned Domestic Interbank Transfers through the new Payment Network Integration boundary.

## Key Activities

- Validate the new Payment Network request/response mapping.
- Validate `NetworkSubmissionReference` correlation behaviour.
- Confirm acceptance is treated differently from Settlement.
- Confirm Payment Network timeout produces `SubmissionStatusUnknown`.
- Prohibit blind resubmission after ambiguous timeout.
- Exercise status enquiry and Reconciliation.
- Keep Legacy Payment integration available only for Legacy-owned in-flight Transfers.
- Compare external response/error classification between Legacy and New.
- Monitor external timeout and unknown-status rates.

## Exit Criteria

- New-owned Transfers use the new Payment Network Integration path.
- Duplicate external submission is prevented.
- Ambiguous timeout reliably enters Reconciliation.
- External references are traceable.
- Settlement outcomes can be correlated to the correct New-owned Transfer.
- Legacy-owned in-flight external work continues safely until drained.
- No unresolved high-severity Payment Network mapping mismatch exists.

## Rollback Point

Stop routing additional new Transfers to New.

Do not resubmit already New-owned in-flight Transfers through Legacy.

Use New Reconciliation/manual recovery for ambiguous New-owned work.

---

# Phase 5 — Reconciliation and Operations Migration

## Goal

Make the New platform operationally self-sufficient for unknown, stuck, failed, and manually recovered Transfers.

## Key Activities

- Enable durable Reconciliation processing.
- Surface `SubmissionStatusUnknown` Transfers.
- Add stuck-Transfer detection based on configured state age.
- Provide operational read models.
- Support authorised manual recovery commands.
- Audit:
  - operator;
  - reason;
  - previous state;
  - resulting action/state;
  - timestamp;
  - Correlation ID.
- Monitor:
  - Reconciliation backlog;
  - unknown-state age;
  - Outbox backlog;
  - Dead Letter items;
  - manual-review backlog;
  - ownership mismatches.
- Reconcile old/new operational and historical views.
- Train Operations on new ownership and recovery rules.

## Exit Criteria

- Unknown outcomes are recoverable without direct database editing.
- Stuck Transfers are discoverable.
- Manual recovery is authorised and auditable.
- Operations can determine the authoritative owner of a Transfer.
- Reconciliation mismatches remain within the agreed threshold.
- Required alerts and runbooks are active.
- Legacy operational tooling is no longer required for New-owned Transfers.

## Rollback Point

New routing may still be reduced or disabled for future requests.

Existing New-owned Transfers remain recoverable in New.

---

# Phase 6 — Legacy Transfer Decommissioning

## Goal

Retire the obsolete Legacy Domestic Interbank Transfer capability after migration evidence proves it is no longer required.

## Key Activities

- Route 100% of eligible new Domestic Interbank Transfer traffic to New.
- Prevent new Legacy Transfer creation for the migrated capability.
- Drain all Legacy-owned in-flight Transfers.
- Reconcile final ownership and settlement records.
- Complete required historical-data access/backfill.
- Remove temporary bridges only after consumers no longer need them.
- Archive Legacy data according to retention/security policy.
- Remove obsolete routes, jobs, credentials, and operational procedures.
- Formally close the rollback window.

## Decommissioning Criteria

Legacy Transfer execution is retired only when:

- 100% of eligible new Transfer traffic routes to New;
- Legacy-owned in-flight Transfers are terminal/drained;
- no unresolved ownership conflicts exist;
- no unintended Legacy financial writes occur for New-owned Transfers;
- Reconciliation mismatch backlog is at or below the formally accepted threshold;
- New error-rate and latency objectives are met over an agreed observation window;
- Payment Network timeout and Reconciliation paths have been proven;
- restart recovery and Outbox recovery have been proven;
- Operations can investigate and recover New-owned Transfers;
- monitoring and alerting are active;
- security/audit requirements are satisfied;
- historical-data access requirements are covered;
- rollback closure has been approved.

## Rollback Point

After formal decommissioning and rollback-window closure, rollback to Legacy is no longer the normal recovery mechanism.

Any reactivation would require a new architecture/migration decision.

---

# 3. Parallel Run Model

Parallel run means **comparison without duplicate financial execution**.

## Legacy Primary / New Shadow

Allowed:

- compare validation result;
- compare classification/routing;
- compare non-side-effect decision results where safe;
- compare expected state transitions;
- compare latency/error mapping.

Not allowed in Shadow:

- real Balance Reservation;
- real Payment Network submission;
- real Settlement;
- duplicate customer financial effects.

## Canary

For a canary cohort:

- New becomes the real owner;
- Legacy does not execute that Transfer financially;
- the remaining population continues to use Legacy.

---

# 4. Historical Data Migration

Historical migration is not a prerequisite for the first New production Transfer.

## Initial

- Legacy history remains readable from Legacy.
- New history is stored in New PostgreSQL.
- a query/facade may combine sources where product/operations require one view.

## Backfill

Historical data is migrated in bounded, restartable batches.

Each batch should provide:

- source identifier;
- migration timestamp;
- idempotent import key;
- counts/control totals;
- status/result comparison;
- failure reporting.

Backfill must not overwrite live New-owned Transfer state.

---

# 5. Legacy Event Integration Decision Tree

Use the first feasible option:

1. **Legacy Transactional Outbox** — preferred when Legacy can be safely modified.
2. **CDC + semantic adapter** — fallback when Legacy application changes are unsafe/impractical.
3. **Persisted polling bridge** — temporary last resort.

Raw CDC database-row changes must be translated into stable Integration Event contracts before entering the New domain.

---

# 6. Cross-Cutting Migration Controls

## Financial Safety

- one owner per Transfer;
- no dual financial write;
- no duplicate external submission;
- no direct ownership reassignment of in-flight Transfers.

## Observability

Track:

- Legacy vs New ownership count;
- routing percentage;
- error/latency comparison;
- shadow mismatch rate;
- Payment Network timeout rate;
- `SubmissionStatusUnknown` count/age;
- Reconciliation backlog;
- ownership mismatch count;
- unexpected Legacy writes;
- CDC/Legacy Outbox lag;
- rollback events.

## Security

- ACL exposes minimum required data;
- logs do not expose raw account numbers unnecessarily;
- migration extracts are access-controlled;
- Legacy/New credentials are managed securely;
- manual migration/recovery actions are auditable.

## Operational Ownership

Migration requires shared ownership across:

- Transfer/backend team;
- Legacy SME/team;
- QA;
- Platform/DB;
- Operations;
- Security;
- Product/Business.

The Tech Lead coordinates architecture decisions but must not become the only person able to operate or approve migration.

---

# 7. Rollback Triggers

Examples:

- financial invariant violation;
- duplicate Payment Network submission;
- unexplained settlement mismatch;
- unexpected ownership conflict;
- excessive growth of `SubmissionStatusUnknown`;
- unacceptable error/latency regression;
- unrecoverable Reconciliation backlog;
- critical observability gap;
- security/privacy issue.

A transient dependency failure that is safely handled by retry/Reconciliation does not automatically require full traffic rollback.

---

# 8. Known Limitations / Decisions Requiring Real Legacy Evidence

The challenge does not provide enough information to permanently determine:

- whether Legacy can support a Transactional Outbox;
- whether CDC is available/approved;
- exact feature-toggle cohorts/percentages;
- exact rollout thresholds;
- exact observation windows;
- authoritative production Account/Ledger migration design;
- full historical-data retention and backfill scope.

These remain explicit implementation/operational decisions and must be validated against the real Legacy environment before production migration.
