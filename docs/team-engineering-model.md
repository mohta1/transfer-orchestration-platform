# Team Engineering Model

**Status:** Recommended operating model for this repository
**Scope:** Modular Monolith delivery and incremental Legacy modernisation

This document describes how a team **should operate** on the Transfer Orchestration Platform. It does not assert a specific existing organisational chart, headcount, or named employees. Roles are **responsibility areas**, not job titles.

The model aligns with [ADR-001](./adr/ADR-001-architecture-style.md) (Modular Monolith) and [ADR-005](./adr/ADR-005-legacy-modernisation.md) (Strangler Fig). It does **not** recommend premature microservice extraction.

---

## 1. Team Topology

For the current challenge scope, a **single product-aligned backend team** owning the Modular Monolith is appropriate:

```
┌─────────────────────────────────────────────────────────────┐
│                    Transfer Platform Team                    │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────────┐ │
│  │ Transfer    │ │ Account &   │ │ Reliability / Ops slice │ │
│  │ Management  │ │ Balance     │ │ (Outbox, Reconciliation,│ │
│  │ owners      │ │ owners      │ │  Audit, Notification)   │ │
│  └─────────────┘ └─────────────┘ └─────────────────────────┘ │
│         Shared: API composition, BuildingBlocks, CI/runtime    │
└─────────────────────────────────────────────────────────────┘
          │                              │
          ▼                              ▼
   Legacy SME (advisory)          Platform / DevOps (shared)
   Product / Security / QA        Operations (runbooks, incidents)
```

**Why module-aligned, not layer-aligned:** Financial invariants, Process Manager behaviour, and reservation concurrency cross-cut a vertical slice. Horizontal-only ownership (e.g. "all Infrastructure") creates review gaps and encourages cross-module DbContext access.

---

## 2. Module and Component Ownership

| Area | Primary owner responsibilities |
| ---- | ------------------------------ |
| **TransferManagement** | Transfer state machine, Process Manager, idempotency, Outbox, Reconciliation records/workers, daily-limit logic, submission/read APIs |
| **AccountBalance** | Account aggregate, reservations, concurrency, module contract `IAccountBalanceReservations`, schema `account_balance` |
| **PaymentNetwork** | ACL mapping, timeout classification, status enquiry contract; no domain persistence |
| **Notification** | Idempotent `TransferCompleted` consumer, Processed Messages, provider adapter |
| **AuditOperations** | Correlation middleware, manual-operation endpoints, audit persistence |
| **BuildingBlocks** | Shared Problem Details, security primitives; must stay dependency-light |
| **API host (`TransferOrchestration.Api`)** | Composition root, JWT auth, health checks, module registration — owned collectively with module owners reviewing their registrations |
| **Reconciliation (placeholder module)** | No runtime owner until extraction; behaviour owned by TransferManagement until then |

### 2.1 Contract ownership

Each **Contracts** surface has an explicit owner module:

| Contract | Owner | Consumers |
| -------- | ----- | --------- |
| `IAccountBalanceReservations` | AccountBalance | TransferManagement |
| `IPaymentNetworkGateway` | PaymentNetwork | TransferManagement, Reconciliation step |
| Notification dispatch port | Notification | Outbox dispatch path |
| Manual-operation DTOs / audit contracts | AuditOperations | TransferManagement manual services |

Contract changes require **both** owner and consumer reviewers on the pull request.

---

## 3. Shared and Advisory Roles

| Role area | Responsibilities in this repository |
| --------- | ------------------------------------- |
| **Tech Lead / Architect (facilitator)** | ADR discipline, cross-module design review, escalation; must not become sole approver for all financial changes |
| **Platform / DevOps** | Docker Compose, CI workflow, migration scripts, PostgreSQL operations, secret hygiene in pipelines |
| **Legacy SME** | Legacy behaviour inventory, routing/ACL feasibility, shadow-mode interpretation, decommission evidence — advisory during Phases 1–6 ([modernisation-roadmap.md](./modernisation-roadmap.md)) |
| **Product Owner** | Unresolved business policy (daily-limit window, reservation expiry, retention), acceptance of manual-review workflows |
| **Security** | JWT policy, role model, logging redaction, cross-customer concealment, manual-command authorisation |
| **QA / test engineer** | Risk-based scenarios: concurrency, restart, duplicate delivery, timeout/reconciliation, security negatives |
| **Operations** | Runbooks for stuck transfers, Outbox backlog, dead letters, manual recovery; feedback into engineering after incidents |

---

## 4. Cross-Module Change Procedure

1. **Identify affected modules and contracts** before coding.
2. **Read locked ADRs** if the change touches coordination, concurrency, messaging, or migration boundaries.
3. **Design review** (lightweight written note or PR description section) for:
   - new cross-module calls;
   - migration/schema changes;
   - Process Manager or Outbox behaviour changes;
   - security or manual-operation paths.
4. **Implement in one TASK branch** — no mixed TASK scope.
5. **Add tests in the same TASK** — domain, integration (PostgreSQL), architecture as applicable.
6. **PR lists evidence**: tests run, migration impact, rollback notes for migration phases.
7. **Owners from each touched module** review.

Forbidden without explicit architecture escalation: direct table access across schemas, new global DbContext, silent contract breaking changes.

---

## 5. Pull Request Review Rules

### 5.1 Classification

Every review comment is **Blocker**, **Non-blocking improvement**, or **Preference** ([AGENTS.md](../AGENTS.md)). Only Blockers prevent merge.

### 5.2 Required reviewer focus

| PR touches | Minimum review focus |
| ---------- | -------------------- |
| Account/reservation/balance | Financial correctness, concurrency tests, constraint impact |
| Transfer state / Process Manager | State machine legality, restart recovery |
| Payment Network / external I/O | Timeout ≠ rejection, no blind resubmit, reference stability |
| Outbox / consumers | Atomic persistence, at-least-once semantics, idempotent consumer proof |
| Security / auth | 401/403/404 semantics, audit actor trust |
| Persistence / migrations | Schema ownership, backward compatibility, rollback |
| Cross-module contract | Both module owners |
| CI / Docker / runtime | Reproducibility, no secrets, no test binaries in runtime image |
| Documentation only | Factual alignment with code/tests; no unverified SLA claims |

### 5.3 PR author obligations

- Link TASK ID and scope.
- State test commands and results.
- Declare migration requirement (`yes/no`).
- For manual operations or financial changes, attach test class/method evidence.

---

## 6. Test Ownership

| Test type | Owner |
| --------- | ----- |
| Domain invariants | Module domain owner |
| Module integration (PostgreSQL) | Module owner + QA pairing on high-risk scenarios |
| End-to-end API flows | TransferManagement owner with QA |
| Architecture dependency rules | Rotating "architecture owner" (weekly/monthly rotation recommended) |
| CI/runtime verification | Platform owner |

**Architecture-test ownership** includes maintaining forbidden-dependency lists when new modules or references are introduced, and running the negative architecture proof when rules change.

Tests are **not** delegated entirely to QA; developers own meaningful coverage with QA designing adversarial scenarios.

---

## 7. Migration Ownership

Incremental modernisation follows [modernisation-roadmap.md](./modernisation-roadmap.md):

| Phase | Engineering lead | Legacy SME | Operations |
| ----- | ---------------- | ---------- | ------------ |
| 1 — Discovery | Transfer team + SME | Primary | Metrics baseline |
| 2 — Boundary isolation | Transfer team | ACL/routing input | Rollback runbook |
| 3 — New submission | Transfer team | Shadow comparison | Canary monitoring |
| 4 — External integration | PaymentNetwork owner | Legacy network parity | Timeout/reconciliation alerts |
| 5 — Ops migration | Reliability slice | Historical tooling | Manual recovery training |
| 6 — Decommission | Tech lead + Product | Sign-off evidence | Closure runbook |

**Backward-compatible change expectation:** New schema/API changes during coexistence must not break Legacy routing or in-flight ownership rules. Feature toggles affect **future routing only**; in-flight Transfers stay with their original owner.

---

## 8. Operational Readiness and Incident Learning

Before increasing New-owned traffic in a real migration:

- Runbooks exist for `SubmissionStatusUnknown`, Outbox backlog, dead letters, stuck Process state.
- On-call can trace a Transfer by `TransferId` and `CorrelationId` without sensitive log exposure.
- Post-incident reviews produce **concrete** backlog items (debt register, tests, docs) — not blame.

Feedback loop: Operations → weekly triage → debt prioritisation ([technical-debt-prioritisation.md](./technical-debt-prioritisation.md)).

---

## 9. Technical Debt Review

- **Cadence:** lightweight review each sprint; formal scoring when registering new debt items.
- **Participants:** module owners + Tech Lead facilitator.
- **Output:** updated debt register, priority class, owner role, trigger condition.

Debt does not automatically block the challenge submission unless classified as a **Blocker** under review rules.

---

## 10. Strangler and Legacy Collaboration

| Activity | Transfer team | Legacy SME |
| -------- | ------------- | ---------- |
| Routing toggle design | Implements | Validates side effects |
| Shadow mode | Implements comparison | Interprets Legacy outcomes |
| Ownership metadata | Defines | Confirms Legacy semantics |
| Historical backfill | Designs idempotent batches | Source data access |
| Decommission sign-off | Provides evidence | Confirms drain complete |

**Human approval boundaries:**

- Production routing percentage changes → Product + Operations approval.
- Manual financial recovery → authorised operator only; never AI-autonomous.
- Rollback of future routing → Operations runbook; no automatic reassignment of in-flight ownership.

---

## 11. AI-Assisted Changes

All AI-assisted work follows [ai-assisted-engineering.md](./ai-assisted-engineering.md):

- AI output is a **proposal**, not evidence.
- Human author runs tests and captures evidence.
- Financial, security, and architecture-sensitive changes require human specialist review — AI cannot self-approve.

---

## 12. Service Extraction Conditions

Extraction of a module into an independent service is **future consideration only**. Evidence required before changing a locked boundary ([ADR-001](./adr/ADR-001-architecture-style.md)):

1. Sustained independent scaling or release-cadence need **measured**, not assumed.
2. Extraction preserves Account concurrency boundary and Outbox/idempotency invariants.
3. No hidden synchronous distributed transactions for financial commits.
4. Consumer idempotency and reconciliation remain provable with tests.
5. ADR-001 system-level classification revisited **after** Legacy decommission — not before.

Candidate extractions documented in [architecture.md](./architecture.md) §24: Payment Network Integration, Reconciliation, Notification — each requires the above evidence.

---

## 13. Related Documents

- [engineering-standards.md](./engineering-standards.md)
- [ai-assisted-engineering.md](./ai-assisted-engineering.md)
- [technical-debt-prioritisation.md](./technical-debt-prioritisation.md)
- [modernisation-roadmap.md](./modernisation-roadmap.md)
- [requirement-to-evidence.md](./requirement-to-evidence.md)
