# Architecture Review Simulation

**Status:** Challenge exercise — §30 deliverable  
**Date:** 2026-08-13  
**Reviewers (simulated panel):** Backend facilitator, Account owner, Platform Engineer, Product Owner  
**Aligned ADRs:** [ADR-001](./adr/ADR-001-architecture-style.md) through [ADR-005](./adr/ADR-005-legacy-modernisation.md)

---

## 1. Proposal under review

> Create separate Microservices for Transfer, Account, Reservation, Fraud, Limit, Notification, Audit, and Reconciliation, each with its own Kafka topic and database.

The proposal decomposes every noun-like capability into an independently deployable service, assigns each a dedicated Kafka topic for integration, and splits persistence so each service owns its own database.

---

## 2. Strengths

| Strength | Why it appears attractive |
| -------- | ------------------------- |
| **Independent scaling story** | Payment or fraud workloads could scale without scaling the entire API |
| **Technology isolation** | Different storage or language choices per service become possible |
| **Failure containment (theoretical)** | A bug in Notification might not crash Transfer — if boundaries are perfect |
| **Team ownership narrative** | Each service mapsto a team backlog and separate release cadence |
| **Event-driven decoupling** | Kafka topics suggest loose coupling between producers and consumers |

These strengths are real **when** independent scaling, compliance isolation, or release cadence differences are **measured** — not assumed on day one.

---

## 3. Risks

| Risk | Impact on transfer workflow |
| ---- | ----------------------------- |
| **Distributed transaction need** | Reserve funds + advance Transfer state + emit integration event becomes cross-service coordination; partial failure causes double hold or lost workflow |
| **Reservation as separate service** | Violates Account-as-financial-boundary ([ADR-003](./adr/ADR-003-reservation-concurrency.md)); introduces synchronous cross-database reservation calls |
| **Kafka ≠ business exactly-once** | At-least-once delivery remains; consumers still need idempotency and Outbox on the producer side |
| **Operational surface area** | Eight services + eight databases + broker cluster + schema migration coordination |
| **Timeout/reconciliation complexity** | Payment ambiguity spans Transfer, Payment ACL, and Reconciliation services — harder to keep “timeout ≠ rejection” invariant |
| **Legacy migration** | Strangler routing plus eight new services increases dual-write and ownership-error risk |
| **Three-developer team** | Cognitive load, on-call rotation, and review breadth exceed sustainable ownership |

---

## 4. Unnecessary complexity

| Element | Why unnecessary initially |
| ------- | ------------------------- |
| **Reservation microservice** | Reservation is not an Aggregate Root; it is owned by Account ([AGENTS.md](../AGENTS.md)) |
| **Limit microservice** | Daily limit is a logical capability inside Transfer workflow, not a separate deployment unit ([context map](./diagrams/context-map.drawio)) |
| **Separate Fraud service before volume proof** | Challenge uses adapter/stub; extraction needs measured isolation benefit |
| **Eight databases** | Module-owned schemas in one PostgreSQL instance already enforce ownership without network partitions |
| **Kafka as default glue** | In-process Outbox + at-least-once dispatch satisfies current consumer count; broker adds ops without removing Outbox |
| **Team-per-service** | Three backend developers cannot own eight production services with meaningful on-call depth |

---

## 5. Alternative design

Adopt the **locked decision**: Incremental Hybrid at system level with a **Modular Monolith** for the new transfer capability ([ADR-001](./adr/ADR-001-architecture-style.md)).

- One deployable `TransferOrchestration.Api` with explicit modules.
- One PostgreSQL instance; module-owned schemas and DbContexts.
- Persistent Process Manager inside TransferManagement ([ADR-002](./adr/ADR-002-process-coordination.md)).
- Transactional Outbox in TransferManagement schema ([ADR-004](./adr/ADR-004-reliable-messaging.md)).
- Account module owns reservations and concurrency ([ADR-003](./adr/ADR-003-reservation-concurrency.md)).
- Legacy coexistence via ACL and routing — not by building eight greenfield microservices beside Legacy ([ADR-005](./adr/ADR-005-legacy-modernisation.md)).

This is **not** permanent dogma: it defers distribution until evidence appears.

---

## 6. Recommended initial boundaries

| Boundary | Initial form | Communication |
| -------- | ------------ | --------------- |
| Transfer workflow | TransferManagement module | In-process + durable process state |
| Financial concurrency | AccountBalance module | Contract `IAccountBalanceReservations` |
| External payments | PaymentNetwork ACL module | Synchronous adapter; timeout classification |
| Fraud | Adapter behind process step | Durable retry + Manual Review escalation |
| Daily limit | Logic in TransferManagement | Same DbContext transaction where applicable |
| Notification | Module + idempotent consumer | Outbox → in-process dispatch initially |
| Reconciliation | Logic in TransferManagement (placeholder assembly) | Durable workers |
| Audit / operations | AuditOperations module | HTTP + audit persistence |
| Messaging | Transactional Outbox | At-least-once; no exactly-once claim |

---

## 7. Conditions that justify later extraction

Extract a module to an independent service **only when measured evidence shows**:

1. Sustained independent scaling or release cadence need.
2. Required failure or compliance isolation that cannot be achieved modularly.
3. Independently owned team with on-call capacity.
4. Extraction preserves Account concurrency boundary — reservations do not become a separate financially authoritative service without ADR revision.
5. Producer-side Outbox and consumer idempotency remain provable after extraction.
6. Reconciliation and ambiguous payment handling remain traceable across boundaries.

Likely **first** candidates (not immediate): Payment Network Integration, Reconciliation, Notification — per ADR-001.

Kafka/RabbitMQ behind the Outbox publisher is justified when **independently deployed consumers** or fan-out throughput require it — not as a default for three developers.

---

## 8. Operational implications

| Topic | Microservice proposal | Modular Monolith (recommended) |
| ----- | --------------------- | ------------------------------ |
| Deployments | Eight pipelines, version skew risk | One API artifact + migration job |
| Observability | Distributed trace mandatory for every transfer | Correlation ID across modules; structured logs ([TASK-20](./tasks/TASK-20-stuck-transfer-operations-observability.md)) |
| Failure modes | Network partitions between Account and Transfer | Local transaction failure rolls back atomically within module DbContext scope |
| Broker ops | Cluster patching, ACLs, topic retention, consumer lag | Deferred; poison-message bounds in Outbox store |
| Runbooks | Eight services + broker + eight DB backups | Compose/CI documented; stuck-transfer query + manual recovery |
| Recovery | Saga compensation across services | Process Manager restart + reconciliation steps |

---

## 9. Team-size implications

**Proposed team:** three Backend Developers, one QA, one Product Owner, one shared Platform Engineer.

| Factor | Eight microservices | Modular Monolith |
| ------ | ------------------- | ---------------- |
| Ownership depth | ~0.4 FTE per service — unsustainable | ~1 FTE per slice (workflow / financial / reliability) |
| Review breadth | Cross-team contract tests for every change | Module contract tests + architecture tests |
| On-call | Eight paging surfaces | One primary API + PostgreSQL + external adapters |
| Delivery | Cross-service coordination per feature | One TASK branch, one PR, one CI pipeline |
| Knowledge | Siloed service experts | Rotating architecture owner ([team model](./team-engineering-model.md)) |

Premature microservices **increase** coordination overhead for a three-developer team without reducing financial risk.

---

## 10. Data-consistency implications

### Account and Reservation

- **Correct model:** Account aggregate owns BalanceReservation entities; Reserve/Consume/Release are atomic within AccountBalance module transactions ([ADR-003](./adr/ADR-003-reservation-concurrency.md)).
- **Proposal flaw:** Separate Reservation service implies cross-service commit to hold funds and advance Transfer — classic distributed transaction or eventual-consistency gap.
- **Risk:** Double reservation, lost release, or negative available balance if messages reorder or retry.

### Transfer state and Outbox

- Business state change and Outbox message must commit **atomically** in one module DbContext ([ADR-004](./adr/ADR-004-reliable-messaging.md)).
- Splitting Transfer and Outbox across services reintroduces the dual-write problem Kafka alone does not solve.

### Kafka semantics

- Kafka provides at-least-once (or effectively-once **processing** only with idempotent consumers and transactional producers in specific configurations).
- **Business** exactly-once (reserve once, pay once, notify once) still requires idempotent handlers and durable deduplication — already implemented via Processed Messages.

---

## 11. Migration / Legacy implications

Incremental Hybrid coexistence ([ADR-005](./adr/ADR-005-legacy-modernisation.md)) requires:

- Single owner per Transfer — no financial dual-write.
- Legacy routing for in-flight transfers; New system owns new submissions incrementally.
- Shadow mode without real duplicate settlement.

Adding eight greenfield microservices **alongside** Legacy multiplies integration points (Legacy → router → N services → N databases). The Strangler path is simpler when New remains one deployable with clear ACL boundaries to Legacy.

**Distinction:** Hybrid coexistence (Legacy + Modular Monolith) is not the same as prematurely distributing the New system into microservices. The first manages **where** transfers execute; the second manages **how** the New codebase is deployed — orthogonal decisions.

Legacy runtime routing remains partially implemented in challenge code ([DEBT-007](./technical-debt-prioritisation.md)); microservice proliferation would worsen that gap.

---

## 12. Final recommendation

**Reject** the proposal to create eight microservices with eight Kafka topics and eight databases as the **initial** architecture for this team and workload.

**Accept** the locked Modular Monolith with module-owned schemas, Transactional Outbox, Account-owned reservations, and Incremental Hybrid Legacy coexistence.

**Revisit extraction** per module when independent scale, isolation, ownership, or release cadence evidence exists — introducing Kafka **behind** the Outbox publisher if independently deployed consumers require it, without claiming exactly-once business effects.

This recommendation aligns with ADR-001–005, the implemented codebase under `src/Modules/`, and the three-developer team model in [team-engineering-model.md](./team-engineering-model.md).

---

## Related documents

- [architecture.md](./architecture.md)
- [diagrams/target-architecture.drawio](./diagrams/target-architecture.drawio)
- [diagrams/deployment-runtime.drawio](./diagrams/deployment-runtime.drawio)
- [team-engineering-model.md](./team-engineering-model.md)
- [technical-debt-prioritisation.md](./technical-debt-prioritisation.md) §11 (broker postponement)
