# ADR-001: Architecture Style

- **Status:** Accepted
- **Decision Date:** 2026-08-07
- **Decision Owners:** Backend Engineering / Architecture
- **Scope:** Resilient Interbank Transfer Orchestration Platform

## Context

The transfer capability is a long-running financial workflow that must remain correct under duplicate requests, concurrent balance reservations, external timeouts, delayed settlement results, broker/publisher failures, application restarts, and manual recovery.

The Event Storming and domain analysis established the following boundaries and responsibilities:

- **Transfer Management** owns the Transfer lifecycle and persisted end-to-end process state.
- **Account and Balance Management** owns balance and reservation consistency.
- **Fraud and Compliance**, **Customer Authorisation**, and the **Domestic Payment Network** are external/upstream dependencies.
- **Payment Network Integration** protects the domain from the external payment protocol.
- **Reconciliation** resolves ambiguous or delayed external outcomes.
- **Notification** is asynchronous and outside the critical financial transaction.
- **Audit and Operations** supports stuck-transfer detection and auditable manual recovery.

The organisation also has an existing Legacy Banking System. The challenge explicitly requires an incremental modernisation path and rejects a big-bang rewrite. It also states that unnecessary distribution is a weakness and does not require multiple production-ready Microservices.

The architecture therefore needs to optimise for financial correctness, recoverability, modifiability, delivery speed, and controlled legacy migration without introducing distributed-system complexity before it is justified.

## Decision

Adopt an **Incremental Hybrid architecture at the system level**.

The new Transfer Orchestration capability will initially be implemented as a **Modular Monolith** with explicit domain/module boundaries, while it coexists with the Legacy Banking System during a Strangler-style incremental migration.

### System-Level Architecture

During migration:

- the Legacy Banking System remains operational for capabilities that have not yet moved;
- the new Transfer Orchestration Platform owns the new transfer workflow;
- old and new capabilities coexist behind explicit integration/routing boundaries;
- migration occurs incrementally rather than through a full-system replacement.

This coexistence is what makes the overall architecture **Hybrid**.

### New Capability Architecture

The new platform is initially **one deployable application**, organised as explicit modules aligned with the domain model:

- Transfer Management
- Account and Balance Management
- Payment Network Integration
- Reconciliation
- Notification
- Audit and Operations

Daily-limit evaluation remains a logical capability associated with the Transfer workflow rather than a separate deployment unit.

Module boundaries must be enforced through explicit contracts. A module must not directly depend on another module's internal implementation or arbitrarily read/write another module's persistence tables.

A single PostgreSQL instance is acceptable initially, but persistence ownership must remain explicit by module/schema/table ownership. Sharing a database instance does not imply shared data ownership.

### Integration with Legacy and External Systems

External and legacy dependencies are accessed through explicit adapters / Anti-Corruption Layers where translation is required.

The new domain model must not depend directly on legacy database structures or external payment-network representations.

### Future Extraction

A Bounded Context or module may later be extracted into an independent service only when there is a measurable reason, such as:

- materially different scaling requirements;
- required failure isolation;
- independent security/compliance boundary;
- independently owned team;
- independent release cadence;
- operational or availability requirements that cannot be met economically inside the Modular Monolith.

Likely extraction candidates include:

- Payment Network Integration;
- Reconciliation;
- Notification.

Extraction is not performed merely because a concept is an Entity, Aggregate, or Bounded Context.

## Business and Technical Drivers

### Business Drivers

- Deliver the critical transfer capability without waiting for full legacy replacement.
- Reduce migration risk through incremental rollout.
- Preserve business continuity while old and new capabilities coexist.
- Keep the implementation realistic for the challenge scope and team size.
- Allow future evolution without committing prematurely to a distributed topology.

### Technical Drivers

- Strong financial consistency requirements around balance reservation.
- Need for explicit domain boundaries.
- Long-running recoverable workflows.
- Need to handle ambiguous external failures safely.
- High value of local transactions and simple failure reasoning.
- Need for durable processing after application restarts.
- Need for future extraction without paying the operational cost immediately.

## Quality Attributes Prioritised

The decision prioritises:

1. **Correctness and consistency** — financial invariants are easier to protect with local transactional boundaries.
2. **Recoverability** — persistent workflow state and reconciliation can be implemented without depending on distributed coordination.
3. **Modifiability** — explicit modules provide clear change boundaries.
4. **Testability** — critical flows can be exercised end-to-end in one application boundary.
5. **Operational simplicity** — fewer independently deployed components reduce production complexity.
6. **Migration safety** — Strangler migration allows gradual replacement of Legacy behaviour.
7. **Observability** — one deployment initially simplifies end-to-end tracing while module-level correlation remains explicit.

## Quality Attributes Deliberately Compromised

The initial architecture deliberately accepts weaker characteristics in the following areas:

- **Independent deployment:** modules cannot initially be deployed separately.
- **Fine-grained failure isolation:** a process-level failure may affect multiple modules.
- **Independent horizontal scaling:** modules scale together initially.
- **Technology heterogeneity:** modules intentionally share the .NET application/runtime rather than selecting independent stacks.

These compromises are accepted because the challenge places higher value on correctness, resilience, and architectural judgement than on premature distribution.

## Alternatives Considered

### Alternative A — Pure Modular Monolith Without Hybrid Migration Framing

**Strengths**

- Simplest target topology.
- Strong local consistency.
- Lowest operational overhead.

**Why not selected alone**

The solution must explicitly address coexistence with the existing Legacy Banking System and incremental modernisation. Describing only the new application as a Modular Monolith does not fully express the system-level migration architecture.

The Modular Monolith remains the selected architecture **inside the new capability**, but the system-level architecture is Hybrid during migration.

### Alternative B — Microservices From the Start

Example decomposition:

- Transfer Service
- Account Service
- Reservation Service
- Fraud Service
- Limit Service
- Reconciliation Service
- Notification Service
- Audit Service

**Potential strengths**

- Independent deployment and scaling.
- Stronger process/service failure isolation.
- Potential for independent team ownership.

**Reasons rejected initially**

- Introduces network failure into operations that currently benefit from local consistency.
- Creates distributed consistency and coordination problems around financial invariants.
- Increases retry, timeout, tracing, deployment, testing, and operational complexity.
- Risks creating one service per domain noun rather than justified service boundaries.
- Requires operational maturity that is not necessary to demonstrate the challenge's critical slice.
- Multiple production-ready Microservices are explicitly outside the challenge's required scope.

### Alternative C — Big-Bang Legacy Replacement

**Potential strength**

- Clean final-state architecture with no transitional coexistence.

**Reasons rejected**

- High delivery and migration risk.
- Difficult rollback.
- Requires complete understanding of Legacy behaviour before value can be delivered.
- Contradicts the challenge's incremental-modernisation objective.
- Expands the project beyond the required critical transfer slice.

## Consequences

### Positive Consequences

- Financial invariants can use local transactions and database constraints where appropriate.
- The team can focus effort on idempotency, concurrency, reliable messaging, reconciliation, and recovery rather than platform distribution.
- Domain boundaries remain explicit and testable.
- Legacy migration can proceed capability by capability.
- Future service extraction remains possible.
- Local development and integration testing remain relatively simple.

### Negative Consequences

- The initial application is a shared deployment unit.
- Poor discipline could allow module boundaries to erode.
- Heavy traffic in one module can require scaling the entire application.
- A process-level outage affects more than one module.
- Future service extraction requires deliberate contract and data migration work.
- Temporary coexistence with Legacy creates duplicated operational and integration concerns during migration.

## Risks

### Boundary Erosion

Developers may bypass module contracts and access another module's data directly.

**Mitigation:** dependency rules, module-level tests, code review, explicit ownership, and architecture fitness tests.

### Accidental Distributed Monolith Later

Extracting modules without redesigning contracts could create synchronous chatty services.

**Mitigation:** extraction requires an explicit trigger, ADR review, independent data ownership, and failure analysis.

### Legacy Coupling

The new domain model could become polluted by Legacy schemas and terminology.

**Mitigation:** use Anti-Corruption Layers and explicit mapping at integration boundaries.

### Shared Database Misuse

A shared PostgreSQL instance could be mistaken for shared data ownership.

**Mitigation:** module-owned schemas/tables and prohibition of arbitrary cross-module persistence access.

### Migration Complexity

Running old and new components simultaneously creates routing, reconciliation, and data-consistency concerns.

**Mitigation:** use staged Strangler migration, controlled ownership transfer, reconciliation, and measurable rollback points. Detailed migration strategy is covered by ADR-005.

## Operational Costs

Initial operational costs are intentionally kept lower than a Microservices-first design:

- one main application deployment for the new capability;
- one relational database platform initially;
- fewer network hops inside the new capability;
- fewer independent deployment pipelines and runtime units;
- simpler local and integration environments.

The system still requires production-grade monitoring for:

- Transfer state and age;
- reconciliation backlog;
- Outbox backlog;
- external dependency failures;
- concurrency conflicts;
- manual recovery activity.

## Team Impact

The team must organise ownership around modules and domain boundaries rather than around horizontal technical layers.

Developers must:

- respect module contracts;
- avoid direct cross-module table access;
- review changes that alter boundaries;
- keep domain terminology aligned with the Ubiquitous Language;
- treat future extraction as an architectural decision, not a routine refactor.

A small or medium backend team can work effectively in one repository and deployment while still assigning clear module ownership.

## Migration Implications

The migration approach is incremental:

1. keep unaffected Legacy Banking capabilities in place;
2. introduce the new Transfer Orchestration capability;
3. integrate through explicit adapters / routing boundaries;
4. move eligible Transfer traffic gradually;
5. compare/reconcile old and new outcomes where required;
6. retire Legacy transfer behaviour only after confidence and rollback criteria are met.

Detailed Strangler sequencing, data duplication, rollback, and coexistence rules are deferred to **ADR-005: Incremental Legacy Modernisation Strategy**.

## Revisit Conditions

Revisit this decision when one or more of the following becomes true:

- a module requires materially different scaling characteristics;
- a module requires stronger independent failure isolation;
- regulatory or security constraints require a separate deployment/data boundary;
- different teams require truly independent delivery ownership;
- release cadence differences materially slow delivery;
- the Modular Monolith becomes difficult to build, test, or deploy within acceptable targets;
- operational evidence shows that one module repeatedly affects availability of unrelated capabilities;
- legacy migration is complete and the system-level Hybrid framing is no longer relevant.

A revisit does **not** automatically imply Microservices. The new evidence must justify the additional distribution cost.

## Related Decisions

- **ADR-002:** Transfer Process Coordination Strategy
- **ADR-003:** Account Reservation and Concurrency Strategy
- **ADR-004:** Reliable Messaging and Outbox Strategy
- **ADR-005:** Incremental Legacy Modernisation Strategy
