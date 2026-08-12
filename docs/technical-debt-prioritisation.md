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

Verified against `main` baseline (TASK-16 merged). Re-verify on each major TASK.

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

## 11. Related Documents

- [engineering-standards.md](./engineering-standards.md)
- [team-engineering-model.md](./team-engineering-model.md)
- [modernisation-roadmap.md](./modernisation-roadmap.md)
- [requirement-to-evidence.md](./requirement-to-evidence.md)
- [architecture.md](./architecture.md) §23 Known Limitations
