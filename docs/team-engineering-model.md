# Team Engineering Model

**Status:** Recommended operating model for this challenge
**Scope:** Modular Monolith delivery and incremental Legacy modernisation
**Team composition (proposed, not named employees):** Three Backend Developers, one QA Engineer, one Product Owner, one shared DevOps/Platform Engineer

This document describes how the **proposed challenge team** should operate on the Transfer Orchestration Platform. Roles are responsibility areas, not job titles. The model aligns with [ADR-001](./adr/ADR-001-architecture-style.md) (Modular Monolith) and [ADR-005](./adr/ADR-005-legacy-modernisation.md) (Strangler Fig). It does **not** recommend team-per-microservice or premature service extraction.

Detailed debt trade-offs: [technical-debt-prioritisation.md](./technical-debt-prioritisation.md). AI usage policy: [ai-assisted-engineering.md](./ai-assisted-engineering.md).

---

## 1. Proposed Team Composition

| Role | Count | Primary focus |
| ---- | ----- | --------------- |
| **Backend Developer** | 3 | Module-aligned vertical slices inside one Modular Monolith |
| **QA Engineer** | 1 | Risk-based scenarios, adversarial testing, release evidence |
| **Product Owner** | 1 | Business policy, acceptance, prioritisation, release trade-offs |
| **DevOps / Platform Engineer** | 1 (shared) | CI, Docker Compose, PostgreSQL operations, secret hygiene, runbooks |

**Facilitation without bottlenecking:** one Backend Developer acts as rotating **Tech Lead / Architect facilitator** (typically the TransferManagement owner). That person facilitates ADRs and cross-module design review but is **not** the sole approver for all financial changes.

### Module-aligned developer slices

| Slice | Backend owner | Modules / areas |
| ----- | ------------- | --------------- |
| **Workflow slice** | Backend Dev A | TransferManagement — state machine, Process Manager, idempotency, Outbox, reconciliation steps, daily limit, submission/read APIs |
| **Financial slice** | Backend Dev B | AccountBalance — Account aggregate, reservations, concurrency, `IAccountBalanceReservations` contract |
| **Reliability slice** | Backend Dev C | PaymentNetwork ACL, Notification consumer, AuditOperations, BuildingBlocks, API composition registrations |

All three developers share review load for cross-cutting changes (security, migrations, CI). Platform Engineer owns pipeline reproducibility; QA owns scenario design and sign-off evidence.

---

## 2. Responsibility Boundaries by Module and Role

| Area | Primary backend owner | QA focus | Product Owner | Platform |
| ---- | -------------------- | -------- | ------------- | -------- |
| **TransferManagement** | Workflow slice | State machine, restart, idempotency, stuck transfers | Daily-limit window, manual-review policy | — |
| **AccountBalance** | Financial slice | Concurrency, reservation invariants | — | Migration scripts for `account_balance` schema |
| **PaymentNetwork** | Reliability slice | Timeout ≠ rejection, no blind resubmit | — | — |
| **Notification** | Reliability slice | Duplicate delivery, Processed Messages | — | — |
| **AuditOperations** | Reliability slice | Manual audit actor, operator endpoints | Manual-recovery approval rules | — |
| **BuildingBlocks** | Reliability slice (lightweight changes only) | — | — | — |
| **API host** | Collective — module owners review their registrations | Security negatives, Problem Details | Demo acceptance | Docker/CI image |
| **Legacy coexistence** | Workflow slice + Product | Shadow/canary evidence | Routing percentage, decommission sign-off | Rollback runbooks |
| **Reconciliation placeholder** | Workflow slice until extraction | Unknown-submission recovery | Escalation thresholds | — |

### Contract ownership

| Contract | Owner module | Required reviewers on change |
| -------- | ------------ | ---------------------------- |
| `IAccountBalanceReservations` | AccountBalance | AccountBalance + TransferManagement |
| `IPaymentNetworkGateway` | PaymentNetwork | PaymentNetwork + TransferManagement |
| Notification dispatch port | Notification | Notification + Outbox owner |
| Manual-operation / audit contracts | AuditOperations | AuditOperations + TransferManagement |
| `IStuckTransferQueries` | TransferManagement | TransferManagement + AuditOperations |

**Forbidden without architecture escalation:** cross-module DbContext access, direct cross-schema table writes, global AppDbContext, silent contract breaking changes.

---

## 3. Design-Review Process and Triggers

### When design review is required (written note in PR or short design doc)

| Trigger | Participants | Output |
| ------- | ------------ | ------ |
| New cross-module call or contract change | Owners of both modules + facilitator | Interface sketch, failure modes, test plan |
| Process Manager or Outbox behaviour change | Workflow slice + Reliability slice + QA | State/sequence impact, restart proof |
| Migration or schema constraint change | Module owner + Platform + facilitator | Rollback notes, constraint names |
| Security or manual-operation path | Reliability slice + QA + Product (if policy) | Auth matrix, audit evidence |
| Legacy routing / ACL / shadow mode | Workflow slice + Product + Platform | Coexistence rules, no dual-write proof |
| Financial invariant change | Financial slice + Workflow slice + QA | Concurrency test list |
| ADR boundary challenge | Facilitator + all backend devs | ADR draft or explicit rejection |

### Lightweight procedure

1. Author identifies affected modules and reads locked ADRs.
2. Author posts design note (problem, options, decision, test impact).
3. Module owners async-review; sync call only if Blocker-level disagreement.
4. Implementation stays within **one TASK** branch.
5. Evidence captured in TASK file and PR description.

---

## 4. Pull Request Review Expectations and Required Reviewers

Every review comment is **Blocker**, **Non-blocking improvement**, or **Preference** ([AGENTS.md](../AGENTS.md)). Only Blockers prevent merge.

| PR touches | Minimum reviewers | Review focus |
| ---------- | ----------------- | ------------ |
| Account / reservation / balance | Financial slice + one other backend dev | Financial correctness, concurrency tests, constraints |
| Transfer state / Process Manager | Workflow slice + QA (high-risk) | State legality, restart recovery |
| Payment Network / external I/O | Reliability slice + Workflow slice | Timeout ≠ rejection, reference stability |
| Outbox / consumers | Reliability slice + Workflow slice | Atomic persistence, at-least-once, idempotent consumer proof |
| Security / auth | Reliability slice + QA | 401/403/404 semantics, audit actor trust |
| Persistence / migrations | Module owner + Platform | Schema ownership, backward compatibility |
| Cross-module contract | Both module owners | Consumer impact, versioning |
| CI / Docker / runtime | Platform + one backend dev | Reproducibility, no secrets, clean runtime image |
| Documentation only | Any backend dev + QA spot-check | Factual alignment with code/tests; no unverified SLA claims |

### PR author obligations

- Link TASK ID and scope.
- State test commands and **executed** results.
- Declare migration requirement (`yes` / `no`).
- For manual or financial changes, cite test class/method evidence.
- Classify review findings in responses.

---

## 5. Definition of Ready (Verifiable Checklist)

A TASK or story is **Ready** when all items below are true:

- [ ] TASK file read completely; scope and out-of-scope understood.
- [ ] Applicable locked ADRs identified (001–005 as relevant).
- [ ] Affected modules and contracts named.
- [ ] Acceptance criteria map to testable behaviour (not prose-only).
- [ ] PostgreSQL integration required? Decision recorded.
- [ ] Migration impact assessed (`yes` / `no`).
- [ ] Product Owner confirms unresolved business policy is documented or decided.
- [ ] No dependency on a later TASK unless explicitly allowed as minimal prerequisite.
- [ ] Branch name follows TASK recommendation.
- [ ] QA agrees on adversarial scenarios for financial/security/reliability paths.

---

## 6. Definition of Done (Verifiable Checklist)

A TASK is **Done** when:

- [ ] Implementation matches TASK scope only (no speculative future TASK work).
- [ ] `dotnet build TransferOrchestrationPlatform.sln` → **0 warnings, 0 errors**.
- [ ] `dotnet test TransferOrchestrationPlatform.sln` → all applicable tests pass.
- [ ] Financial/concurrency/Outbox/restart tests use **real PostgreSQL** where mandated.
- [ ] Required tests from TASK file exist in same TASK (not deferred).
- [ ] TASK **Status** updated to Done with evidence section filled.
- [ ] Requirement matrix updated when TASK closes a documented gap.
- [ ] No secrets, local absolute paths, or binaries committed.
- [ ] Review Blockers resolved; findings classified.
- [ ] Documentation claims match code/tests (no Verified-from-prose-only).
- [ ] Human author accountable — AI output is not evidence.

---

## 7. Testing Ownership Across Developers and QA

| Test type | Primary owner | QA role |
| --------- | ------------- | ------- |
| Domain invariants | Module domain owner (backend dev) | Review edge cases; add adversarial cases |
| Module integration (PostgreSQL) | Module owner | Pair on concurrency, restart, duplicate delivery |
| End-to-end API flows | Workflow slice | Own scenario scripts; sign-off evidence |
| Architecture dependency rules | Rotating architecture owner (weekly) | Verify negative proof when rules change |
| CI / runtime verification | Platform Engineer | Smoke after pipeline changes |
| Security negatives | Reliability slice + QA | Mandatory for auth/manual paths |
| Demo / README paths | Workflow slice + Platform | Clean-room reproduction |

**Rule:** developers own meaningful coverage; QA designs scenarios developers might miss (duplicate HTTP, concurrent reservation, timeout/reconciliation, fraud retry escalation, stuck-transfer query). QA does **not** replace developer-written tests for domain invariants.

---

## 8. Architecture Decision Process and ADR Rules

| Rule | Detail |
| ---- | ------ |
| **When to write an ADR** | System-level boundary change, messaging semantics, concurrency model, Legacy migration strategy, rejected major alternative |
| **Locked ADRs** | Five ADRs (001–005) — change only with explicit blocker and new ADR not permitted in challenge scope without escalation |
| **Author** | Facilitator drafts; all three backend devs review |
| **Approval** | Consensus among backend devs + Product for business-visible decisions |
| **Evidence** | ADR must cite Event Storming / requirement drivers; link diagrams |
| **Extraction** | Service extraction requires measured evidence per ADR-001 — not team preference |
| **AI role** | AI may draft ADR text; humans verify against code and tests |

New decisions during delivery that do not change locked boundaries are recorded in TASK evidence or engineering standards — not as a sixth ADR during this challenge.

---

## 9. Technical-Debt Handling

- **Register:** [technical-debt-prioritisation.md](./technical-debt-prioritisation.md) — evidence, owner role, priority class, trigger.
- **Cadence:** lightweight review each sprint; formal scoring when registering P0/P1 items.
- **Participants:** module owners + facilitator + Product for business-decision items.
- **Release gate:** P0 or review **Blocker** debt blocks release; P2/P3 scheduled with explicit residual-risk owner.
- **Eight-week trade-off exercise:** see §29 simulation in debt document — not production incident data.

Debt does not automatically block challenge submission unless classified Blocker under [AGENTS.md](../AGENTS.md).

---

## 10. Incident-Learning Process

1. **Detect:** Operations or on-call uses correlation ID, transfer ID, stuck-transfer query, Outbox backlog signals — no sensitive log exposure.
2. **Stabilise:** Manual recovery via authorised operator commands only; no AI-autonomous financial action.
3. **Timeline:** Capture facts (state, external reference, retry count) within 24 hours.
4. **Review:** Blameless post-incident within one week — facilitator + module owners + Platform + QA.
5. **Backlog:** Concrete items only — test gap, runbook, debt register entry, doc correction.
6. **Verify:** Follow-up TASK or debt item shows test/doc evidence before closure.

Feedback loop: Operations → weekly triage → debt prioritisation → TASK planning.

---

## 11. Mentoring Approach

| Mechanism | Purpose |
| --------- | ------- |
| **Pair on first module change** | New dev on a slice pairs with owner for first cross-module PR |
| **Rotating architecture owner** | Weekly rotation among three backend devs — architecture tests, ADR literacy |
| **QA-led failure injection** | QA walks team through timeout/reconciliation/concurrency demos |
| **Review shadowing** | Junior author tags facilitator; reviewer explains Blocker vs preference |
| **Legacy SME sessions** | Product arranges advisory sessions during migration phases (not daily stand-up) |

Goal: any backend dev can review financial and Outbox changes without waiting for a single Tech Lead.

---

## 12. Knowledge-Sharing Practices

| Practice | Cadence | Owner |
| -------- | ------- | ----- |
| Module walkthrough (30 min) | When onboarding or after major TASK | Module owner |
| TASK evidence review | End of each TASK merge | Author presents test commands + gaps |
| Ubiquitous Language sync | When domain terms change | Workflow slice |
| Diagram drift check | Doc TASKs (17, 21, 22) | Facilitator + QA |
| Runbook read-through | Before canary traffic increase | Platform + Operations |
| ADR lunch-and-learn | After new organisational decision | Facilitator |

All session notes link to repo paths — not chat-only knowledge.

---

## 13. Escalation Path for Ambiguity

| Ambiguity type | First responder | Escalate to | Decision deadline |
| -------------- | --------------- | ----------- | ------------------- |
| **Product / policy** (daily limit window, reservation expiry, retention) | Product Owner | Business stakeholder | Before production migration |
| **Security** (JWT policy, log redaction, role model) | Reliability slice | Security specialist + Platform | Before exposing manual endpoints |
| **Financial** (compensation, double-effect risk) | Financial slice | Facilitator + Product | Block release until test proof |
| **Operational** (stuck threshold, on-call runbook) | Platform + Workflow slice | Product + Operations | Before traffic increase |
| **Architecture boundary** | Facilitator | All backend devs | Before cross-module contract change |
| **Legacy routing / decommission** | Workflow slice + Product | Legacy SME advisory | Phase gate in modernisation roadmap |

**Release hold:** any unresolved financial or security Blocker escalates to Product Owner with explicit written release/no-release recommendation.

---

## 14. Release-Readiness Criteria

Before increasing New-owned traffic (beyond challenge submission):

| Criterion | Evidence |
| --------- | -------- |
| Build/test green on target SHA | CI + local PostgreSQL suite |
| Mandatory challenge matrix | [requirement-to-evidence.md](./requirement-to-evidence.md) — no Not verified blockers |
| Runbooks | Stuck transfers, `SubmissionStatusUnknown`, Outbox backlog, manual recovery |
| Security | SecurityBoundaryTests + manual-operation audit proof |
| Financial | Concurrency + reservation tests on real PostgreSQL |
| Messaging | Outbox atomicity + idempotent consumer tests |
| Observability | Correlation trace without sensitive fields; stuck-transfer query for operators |
| Debt | No open P0; §29 release gates satisfied |
| Product sign-off | Accepted residual risks documented with owner |
| Rollback | Legacy routing rollback runbook (future migration phases) |

Challenge submission release uses the same evidence bar for **implemented** scope; Legacy runtime routing remains explicitly partial ([DEBT-007](./technical-debt-prioritisation.md)).

---

## 15. Knowledge Distribution and Rotation (Anti-Bottleneck)

| Mechanism | Rotation | Prevents |
| --------- | -------- | -------- |
| **Architecture test ownership** | Weekly among three backend devs | Facilitator as sole gatekeeper |
| **On-call primary** | Weekly backend rotation + Platform secondary | Single workflow expert |
| **PR review load** | Module owners review own area; cross-review mandatory on financial/Outbox | Review queue on one person |
| **ADR facilitation** | Fixed facilitator; all devs must approve | Unilateral architecture changes |
| **Demo / README ownership** | Workflow slice documents; Reliability slice verifies security demo | Tacit local-only knowledge |
| **Contract changes** | Always two module owners | Silent breaking API changes |

**Target:** at least two backend developers can explain Process Manager restart recovery, Outbox semantics, and reservation concurrency without the facilitator present.

---

## 16. Migration and Legacy Collaboration

| Activity | Backend team | Product Owner | Platform |
| -------- | ------------ | ------------- | -------- |
| Routing toggle design | Implements | Approves percentage | Executes runbook |
| Shadow mode | Implements comparison | Accepts evidence | Monitors |
| Decommission sign-off | Provides drain evidence | Signs off | Closes runbook |

**Human approval boundaries:** production routing changes → Product + Operations; manual financial recovery → authorised operator only; AI cannot approve release.

See [modernisation-roadmap.md](./modernisation-roadmap.md) for phase ownership detail.

---

## 17. AI-Assisted Changes

All AI-assisted work follows [ai-assisted-engineering.md](./ai-assisted-engineering.md) (policy in §1–13; §34 submission evidence in §15):

- AI output is a **proposal**, not evidence.
- Human author runs tests and captures evidence.
- Financial, security, and architecture-sensitive changes require human specialist review.

---

## 18. Service Extraction Conditions

Extraction is **future consideration only**. Evidence required before changing a locked boundary ([ADR-001](./adr/ADR-001-architecture-style.md)):

1. Sustained independent scaling or release-cadence need **measured**, not assumed.
2. Extraction preserves Account concurrency boundary and Outbox/idempotency invariants.
3. No hidden synchronous distributed transactions for financial commits.
4. Consumer idempotency and reconciliation remain provable with tests.
5. ADR-001 revisited after Legacy decommission — not before.

See [architecture.md](./architecture.md) §25 for why premature microservice decomposition is rejected for this team size.

---

## 19. Related Documents

- [engineering-standards.md](./engineering-standards.md)
- [technical-debt-prioritisation.md](./technical-debt-prioritisation.md)
- [ai-assisted-engineering.md](./ai-assisted-engineering.md)
- [architecture.md](./architecture.md)
- [modernisation-roadmap.md](./modernisation-roadmap.md)
- [requirement-to-evidence.md](./requirement-to-evidence.md)
