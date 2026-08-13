# Technical Debt Prioritisation

**Status:** Active method for this repository
**Scope:** Challenge codebase and documented migration path

This document defines how technical debt is identified, scored, owned, and scheduled. It does **not** invent production incidents, user volumes, regulatory obligations, or SLAs.

Debt classification aligns with review rules in [AGENTS.md](../AGENTS.md): **Blocker**, **Non-blocking improvement**, **Preference**.

---

## 1. Definition of Technical Debt

**Technical debt** is an intentional or accumulated gap between the current implementation and the desired maintainable, correct, and operable state — where deferral creates measurable risk or delivery friction.

Debt is **not**:

| Term | Meaning |
| ---- | ------- |
| **Defect** | Incorrect behaviour against requirements — fix as a bug, not debt |
| **Risk** | Potential future harm without a concrete code/doc gap yet |
| **Missing feature** | Out-of-scope or future TASK work |
| **Preference** | Style or alternative design with no correctness impact |

---

## 2. Sources of Debt

- Known placeholder or stub modules
- Document/code drift (broken links, stale counts)
- Unresolved domain policy (documented as limitation, not hidden)
- Test coverage gaps for legal but rare state paths
- Operational tooling not yet built for production migration
- Temporary bridges (polling, shadow adapters) beyond intended lifetime
- AI-generated or expedient shortcuts accepted with recorded trade-off

---

## 3. Registering Debt — Evidence Required

Each register entry must include:

1. **Identifier** (DEBT-NNN)
2. **Evidence** — file path, test gap, or doc reference
3. **Affected module/document**
4. **Risk category**
5. **Scored priority**
6. **Recommended treatment**
7. **Owner role** (not a person name)
8. **Trigger / review condition**
9. **Blocks challenge submission?** (yes/no)

No entry based solely on reviewer preference without evidence.

---

## 4. Scoring Dimensions

Score each dimension **0–3** (0 = negligible, 3 = severe):

| Dimension | Question |
| --------- | -------- |
| **Business impact** | Does delay affect customer transfers or migration milestones? |
| **Financial-correctness risk** | Could debt cause double spend, negative balance, or lost reservation? |
| **Security risk** | Exposure of data, auth bypass, or audit gap? |
| **Operational/reliability risk** | Outage, unrecoverable workflow, or silent data loss? |
| **Probability / frequency** | How often is the path exercised? |
| **Blast radius** | One Transfer vs platform-wide |
| **Maintainability / delivery friction** | Slows every change in the area? |
| **Cost of delay** | Does debt compound weekly? |
| **Remediation effort** | 0 = trivial doc fix, 3 = multi-sprint refactor |
| **Reversibility** | Hard to undo after production traffic? |
| **Regulatory/compliance uncertainty** | Unknown policy only — **do not claim compliance** |

**Priority score** = weighted sum:

```
Priority = (Financial × 3) + (Security × 3) + (Operational × 2) + (Business × 2)
         + (Blast radius × 2) + (Probability × 1) + (Maintainability × 1) + (Cost of delay × 1)
         − (Remediation effort × 1) − (Reversibility bonus if easy to defer: 0–2)
```

---

## 5. Priority Classes and Response

| Class | Score range | Response expectation |
| ----- | ----------- | -------------------- |
| **P0 — Critical** | ≥ 28 or any financial/security score 3 | Address before production migration traffic; may be review **Blocker** |
| **P1 — High** | 20–27 | Next TASK or dedicated sprint item |
| **P2 — Medium** | 12–19 | Scheduled within roadmap phase |
| **P3 — Low** | < 12 | Backlog; review at debt cadence |

---

## 6. Ownership and Cadence

- **Owner role:** module owner or Platform/Docs owner as listed per item.
- **Review cadence:** each sprint + phase exit gates in [modernisation-roadmap.md](./modernisation-roadmap.md).
- **Entry criteria:** evidence recorded in this register or TASK evidence.
- **Exit criteria:** evidence of remediation (test, doc, code) + score re-evaluated to P3 or removed.

---

## 7. Debt During Incremental Modernisation

During Strangler migration:

- Debt that breaks **single-owner**, **no dual-write**, or **reconciliation** rules is **P0**.
- Shadow-mode shortcuts must not leak into canary financial paths.
- Phase exit reviews scan this register against phase exit criteria.

---

## 8. When Debt Becomes a Release Blocker

Debt is a **Blocker** when:

- It violates a locked ADR or [AGENTS.md](../AGENTS.md) review rule (e.g. cross-module DbContext).
- A mandatory challenge requirement lacks truthful evidence ([requirement-to-evidence.md](./requirement-to-evidence.md)).
- Financial/security invariant could fail in production path with no compensating test.

Otherwise debt remains **Non-blocking improvement** or **Preference**.

---

## 9. Relation to Review Classifications

| Review label | Debt action |
| ------------ | ----------- |
| **Blocker** | Register as P0/P1; fix before TASK Done or next migration phase |
| **Non-blocking improvement** | Register P2/P3 with owner |
| **Preference** | Optional backlog; do not inflate priority |

**Evidence changes priority:** new test proving safety may downgrade item; new incident or audit finding may upgrade.

---

## 10. Project-Specific Debt Register

Verified against `main` baseline (TASK-20 merged; TASK-21 debt exercise). Re-verify on each major TASK.

| ID | Evidence | Module / doc | Risk | Priority | Treatment | Owner role | Trigger | Blocks submission? |
| -- | -------- | ------------ | ---- | -------- | --------- | ---------- | ------- | ------------------ |
| **DEBT-001** | `src/Modules/Reconciliation/` contains only empty csproj; logic in `TransferManagement/Application/Reconciliation/*` | Reconciliation / TransferManagement | Maintainability; premature extraction confusion | **P2** (score ~14) | Keep documented placeholder; extract only when ADR-001 extraction evidence met | TransferManagement owner | Phase 5 ops migration planning | **No** — behaviour implemented and tested in TransferManagement |
| **DEBT-002** | `architecture.md` referenced non-existent `ADR-004-reliable-messaging-outbox.md` | Documentation | Doc drift; broken link | **P3** (score ~6) | Fixed in TASK-17 to `ADR-004-reliable-messaging.md` | Docs owner | Link validation on doc TASKs | **No** (remediated TASK-17) |
| **DEBT-003** | `TransferState.CompensationRequired` and `MarkCompensationRequired()` exist; limited dedicated test vs ManualReview path | TransferManagement | Financial-correctness edge path less proven | **P2** (score ~13) | Add domain/integration tests when compensation workflow is in scope | TransferManagement owner | Product defines compensation policy | **No** for challenge — state exists; manual review path tested |
| **DEBT-004** | Daily Limit ownership/window noted as unresolved in [ubiquitous-language.md](./ubiquitous-language.md) §1 | TransferManagement / Product | Business policy uncertainty | **P2** (score ~11) | Product decision + doc update; current UTC-day implementation documented in tests | Product + Transfer owner | Pre-production policy sign-off | **No** — implemented with documented assumption |
| **DEBT-005** | `PaymentNetwork` module has Application + Contracts only (no Infrastructure folder) | PaymentNetwork | Convention variance | **P3** (score ~5) | Accept for ACL stub; add Infrastructure if real provider SDK integrated | PaymentNetwork owner | Real provider integration TASK | **No** |
| **DEBT-006** | `README.md` still work-in-progress; final demo path in TASK-18 | Documentation | Reviewer friction | **P2** (score ~10) | Complete in TASK-18 only | Docs owner | TASK-18 start | **No** for TASK-17 — explicitly out of scope |
| **DEBT-007** | Legacy routing/ACL not implemented in code (challenge focuses on New platform) | Migration | Migration gap vs real Legacy | **P1** (score ~18) | Implement per [modernisation-roadmap.md](./modernisation-roadmap.md) Phases 2+ with Legacy SME | Transfer team + Legacy SME | Real Legacy environment available | **No** for challenge code scope — documented as future phase |

---

## 11. Eight-Week Product Delivery Trade-off

**Exercise type:** Challenge leadership simulation — not production incident data
**Team:** Three Backend Developers, one QA Engineer, one Product Owner, one shared DevOps/Platform Engineer
**Goal:** Ship the Modular Monolith transfer capability to a controlled release gate within **eight weeks**, while Legacy coexistence documentation remains honest about partial runtime implementation.

### Assumptions

- The eight weeks cover hardening, operational readiness, and selected debt — not building a full Legacy replacement or production broker cluster.
- No invented latency SLAs, regulatory claims, or team capacity beyond the roles above.
- Financial correctness, Outbox atomicity, and payment timeout semantics are **non-negotiable** (locked ADRs).
- Legacy routing ACL in production code remains a **documented future phase** unless Product reprioritises the entire release.
- Residual risks require a named **acceptance owner** (Product Owner for business risk; Platform for operational risk).

### Phase allocation (eight weeks)

| Weeks | Focus | Primary owners |
| ----- | ----- | -------------- |
| **1–2** | Release blockers: log redaction, fraud idempotency contract with provider, integration test expansion for payment/reconciliation paths | Backend + QA + Platform |
| **3–4** | Legacy account query mitigation (read-model/ACL caching design); payment-network documentation pack for operators | Backend Dev B + Platform + Product |
| **5–6** | Broker readiness decision gate; automated integration suite in CI; stuck-transfer/runbook validation | Platform + QA + Backend Dev C |
| **7** | Residual-risk review; release/no-release rehearsal; debt register closure | Product + all |
| **8** | Controlled release or documented no-release with accepted mitigations | Product Owner sign-off |

**Critical path:** concerns **1** (Legacy query performance at coexistence boundary) and **2** (fraud idempotency) affect customer-visible correctness during migration — they consume early backend capacity. Concern **6** (broker cluster) is evaluated in week 5; if not production-ready, release proceeds with in-process Outbox (current architecture) rather than delaying eight weeks for Kafka.

**Explicitly not built in eight weeks:** full Legacy decommission, Kubernetes platform, production fraud engine, complete broker-based exactly-once illusion, team-per-microservice ownership.

### Concern matrix (exactly six)

| # | Concern | Classification | Risk | Mitigation | Owner role | Release impact | Follow-up condition |
| - | ------- | -------------- | ---- | ---------- | ---------- | -------------- | ------------------- |
| **1** | Legacy account queries are slow | **Can be mitigated temporarily** | Slow authorisation or balance lookups delay submission; migration traffic may hit Legacy read paths | Add timed ACL caching layer + read-model projection for hot account metadata; cap Legacy query fan-out in router; monitor p95 in shadow mode | Backend Dev B + Platform | Release allowed with cache TTL documented and rollback to uncached path | Product confirms acceptable staleness window; remove cache when New owns account reads |
| **2** | Fraud integration has no idempotency support | **Must resolve before release** | Duplicate fraud calls could produce inconsistent screening outcomes for the same Transfer | Implement client-side idempotency key on fraud requests; persist screening attempt reference in process state; bounded retry only with same key; escalate to Manual Review on ambiguity | Backend Dev A + QA | **Blocks release** until contract test with provider stub proves duplicate call safety | Provider documents idempotency semantics; integration test green on CI |
| **3** | Payment-network documentation is incomplete | **Can be mitigated temporarily** | Operators mis-handle timeout vs rejection; incorrect manual recovery | Produce operator runbook: timeout → `SubmissionStatusUnknown`, enquiry-only recovery, reference stability; link to sequence diagram | Backend Dev C + Product | Release allowed when runbook reviewed by Operations advisor | Complete provider API catalogue in Phase 4 migration doc |
| **4** | Existing logs contain account numbers | **Must resolve before release** | Sensitive data exposure in log aggregation | Structured logging redaction/fingerprint helpers (as implemented in `OperationalTelemetry`); scrub legacy log pipelines; add CI grep for raw account patterns in new code | Platform + Backend Dev C | **Blocks release** until redaction verified in integration tests | Security review confirms no raw account numbers in standard log fields |
| **5** | Automated integration tests are limited | **Can be mitigated temporarily** (+ **Requires business decision** on scope) | Undetected regressions in rare paths (fraud escalation, reconciliation) | Expand PostgreSQL suite in CI for top risk scenarios; manual test charter for gaps; Product accepts documented untested paths | QA + Platform | Release allowed if CI covers financial/concurrency/Outbox/security blockers and Product signs residual test gap register | CI runs full integration project on every main merge |
| **6** | Broker cluster is not production-ready | **Can be postponed** (+ **Requires business decision** on messaging topology) | Cannot fan out to independent consumers at scale | Continue in-process Outbox dispatcher (current challenge architecture); document broker introduction criteria per ADR-004; do not claim Kafka exactly-once | Platform + Product | Release allowed **without** broker — at-least-once via Outbox remains | Re-evaluate when independent consumer deployment is required |

### Release / no-release conditions

**Release allowed when:**

- Concerns **2** and **4** are resolved with test evidence.
- Concerns **1**, **3**, **5** have documented mitigations and named acceptance owners.
- Concern **6** explicitly postponed — Outbox in-process path proven (existing `TransactionalOutboxTests`).
- Product Owner accepts residual-risk register for items mitigated temporarily.
- No P0 debt remains open ([§10 register](#10-project-specific-debt-register)).

**No-release when:**

- Fraud idempotency or log redaction lacks executable proof.
- Product rejects staleness or test-gap residual risk.
- Team attempts to substitute broker deployment for Outbox/idempotency testing.

**Residual-risk acceptance owner:** Product Owner (business/customer impact); Platform Engineer (operational/logging/broker deferral).

---

## 12. Related Documents

- [engineering-standards.md](./engineering-standards.md)
- [team-engineering-model.md](./team-engineering-model.md)
- [modernisation-roadmap.md](./modernisation-roadmap.md)
- [requirement-to-evidence.md](./requirement-to-evidence.md)
- [architecture.md](./architecture.md) §23 Known Limitations
