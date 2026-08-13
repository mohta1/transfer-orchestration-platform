# AI-Assisted Engineering Report

**Status:** Candidate submission report (§34) — separate from team policy  
**Author accountability:** Human candidate owns all merged output  
**Rendering assumption:** Approximately **1,050 words** at 11pt A4 (~two pages). Repository has no PDF renderer; word count used as proxy.  
**Baseline SHA:** `20bc709739d6368a932df9e2ff8dc944d8d2409d` (TASK-20 merged on `main`)  
**Policy reference:** [ai-assisted-engineering.md](./ai-assisted-engineering.md)

---

## 1. Tools actually used

| Tool | Use in this repository |
| ---- | ---------------------- |
| **Cursor** (AI coding agent) | Primary assistant for TASK-scoped implementation, tests, and documentation drafts |
| **Git / GitHub CLI** | Branch-per-TASK workflow, PR evidence, CI links |
| **dotnet CLI** | Build, test, EF migrations — mandatory verification gate |
| **Docker Compose** | Local PostgreSQL runtime (TASK-16) |

Git history shows `Co-authored-by: Cursor <cursoragent@cursor.com>` on commits such as `2a705e0` and `b9f9d1b` (architecture-test CI fixes).

---

## 2. Tasks delegated to AI

- Boilerplate aligned to existing module patterns (endpoints, DI registration, test fixtures).
- Draft documentation from verified code paths (TASK-17, TASK-18, TASK-21).
- First-pass test cases from TASK Required Tests lists (e.g. fraud workflow, stuck-transfer operations).
- CI/runtime script troubleshooting suggestions (Compose env, cross-platform paths).
- Exploratory grep/read assistance before edits — never accepted without compile/test proof.

**Not delegated:** merge approval, Blocker classification, production/release decisions, secret handling.

---

## 3. Important prompt patterns

| Pattern | Purpose |
| ------- | ------- |
| Read `AGENTS.md` + current TASK file completely before coding | Scope control; locked architecture |
| Inspect repository before generating imports or test names | Prevent hallucinated APIs |
| One TASK per branch/PR; no future-TASK features | Challenge execution rule |
| Real PostgreSQL for concurrency/Outbox/restart tests | Avoid false confidence from InMemory |
| Cite test class/method in evidence sections | Truthful requirement matrix |
| Classify review findings as Blocker / Non-blocking / Preference | Consistent review gate |

Example instruction shape used across TASKs: *“Read TASK-NN, inspect existing implementation, implement only in-scope behaviour, add Required Tests in same TASK, run full build/test, capture evidence.”*

---

## 4. Generated-code review process

1. Human reads full diff — especially financial, security, and migration files.
2. Run `dotnet build` (TreatWarningsAsErrors) and `dotnet test` with `TEST_DATABASE_CONNECTION_STRING`.
3. Map diff to TASK acceptance criteria; reject scope creep.
4. Architecture tests must pass (`TransferOrchestration.ArchitectureTests`).
5. PR description lists **executed** commands and outcomes — not “AI verified.”
6. Reviewer applies [AGENTS.md](../AGENTS.md) Blocker rules (timeout ≠ rejection, no cross-module DbContext, etc.).

---

## 5. Architecture-validation process

- Locked ADRs (001–005) consulted before boundary changes.
- `ArchitectureTests` enforce module dependency rules; TASK-15 required a **negative proof** (temporary forbidden dependency fails, then reverted).
- Diagrams and `architecture.md` cross-checked against `src/Modules/` layout — target vs implemented labelled explicitly.
- [architecture-review-simulation.md](./architecture-review-simulation.md) documents rejection of premature eight-service decomposition.

AI did **not** approve architecture — it drafted options; human facilitator aligned with ADR-001 Modular Monolith decision.

---

## 6. Test-validation process

| Layer | Validation |
| ----- | ---------- |
| Domain | xUnit in `TransferOrchestration.Domain.Tests` — 81 tests at TASK-21 baseline |
| Integration | Real PostgreSQL — persistence, concurrency, Outbox, fraud, stuck transfers |
| Architecture | 12 dependency/composition tests |
| CI | GitHub Actions on PR SHAs (TASK-16–20 evidence URLs in TASK files) |

Integration tests run twice in fresh processes where TASKs required order-independence proof (TASK-18). AI-generated tests treated as suspect until green locally.

---

## 7. Incorrect AI suggestion actually rejected

**Suggestion:** Introduce a shared `AppDbContext` (or equivalent global DbContext) to “simplify cross-module queries.”

**Why rejected:** Violates locked module boundaries ([AGENTS.md](../AGENTS.md), [ADR-001](./adr/ADR-001-architecture-style.md)). Account financial state must stay inside `AccountBalanceDbContext`; Transfer workflow inside `TransferManagementDbContext`. Architecture tests and review rules flag cross-module DbContext access as a **Blocker**.

**Evidence:** Listed as unacceptable in [ai-assisted-engineering.md](./ai-assisted-engineering.md) §12; `ArchitectureTests` enforce forbidden dependencies (TASK-15 negative proof).

---

## 8. Generated result actually substantially modified

**Area:** Fraud screening (TASK-19).

**Initial direction:** Synchronous fraud call in `TransferSubmissionService` with binary approved/rejected outcome before durable scheduling — insufficient for timeout/unavailability recovery.

**Human-modified result:** Durable `RequestFraudScreening` process action, `FraudScreeningResult` with explicit timeout/unavailable/manual-review outcomes, `FraudScreeningProcessStep` with external call outside DB transaction, bounded retry policy, and domain tests proving `FraudRejected` cannot continue.

**Evidence:** TASK-19 implementation scope; commits on `feature/fraud-screening-resilience` / PR #29; `FraudScreeningWorkflowTests`, `TransferFraudScreeningTests`.

---

## 9. Decisions that remained the candidate’s own

- Modular Monolith over eight microservices ([architecture-review-simulation.md](./architecture-review-simulation.md)).
- Account as sole financial concurrency boundary; Reservation not an Aggregate Root.
- Payment timeout → `SubmissionStatusUnknown`, never blind resubmit ([ADR-002](./adr/ADR-002-process-coordination.md)).
- Transactional Outbox with at-least-once semantics — no exactly-once claims.
- Stuck-transfer **query-based** observability without background alert worker (TASK-20).
- Legacy routing deferred to migration phases with honest partial verification ([DEBT-007](./technical-debt-prioritisation.md)).

---

## 10. Risks introduced by AI

| Risk | Manifestation | Mitigation |
| ---- | ------------- | ---------- |
| Hallucinated types/paths | Broken imports, failing CI | Grep + compile before accept |
| Windows-only assumptions | Linux CI failures (`2a705e0`, `b9f9d1b`) | Cross-platform path helpers; CI on `ubuntu-latest` |
| Over-broad refactors | TASK scope violation | One TASK per PR; human diff review |
| False “tests passed” claims | Evidence without commands | Require pasted dotnet test output in TASK files |
| Exactly-once wording | Misleading docs | Explicit rejection; Ubiquitous Language review |

---

## 11. Example where AI accelerated delivery

**TASK-15 architecture tests:** Cursor helped scaffold `ArchitectureTestHelpers` and composition-root tests quickly from existing project structure. Human fixed Linux `ProjectReference` path parsing (`b9f9d1b`) after CI failure. Net: dependency enforcement landed in one TASK instead of multi-day boilerplate writing.

---

## 12. Example where AI increased review effort

**TASK-20 integration tests:** AI-generated test host setup initially left hosted workers running, causing flaky parallel behaviour. Human added `TestServiceCollectionExtensions` to disable workers and corrected seed helpers for domain rules (commits `a24360e`, `9ed8a3c` on feature branch before merge #30). AI sped scaffolding; human spent extra cycles on test-host lifecycle review.

---

## 13. Team AI-usage rules (summary)

Full policy: [ai-assisted-engineering.md](./ai-assisted-engineering.md).

- AI output is a **proposal**, not evidence.
- No secrets, production PII, or JWT keys in prompts.
- Financial/security/architecture changes need human specialist review.
- One TASK per branch; tests ship with behaviour.
- Build must be warning-free; PostgreSQL where mandated.
- PRs note AI-assisted sections and verification commands.

---

## 14. Candidate accountability statement

I am accountable for every merged change in this repository. AI tools assisted drafting and exploration; I read TASK files and ADRs, inspected the codebase, ran build and test commands, and captured truthful evidence in TASK and PR records. I do not claim AI independently verified financial correctness, security, architecture approval, or production readiness. Review Blockers override AI suggestions — including rejected global DbContext shortcuts and revised fraud durability design.

---

## Related documents

- [ai-assisted-engineering.md](./ai-assisted-engineering.md) — reusable team policy
- [team-engineering-model.md](./team-engineering-model.md) §17
- [requirement-to-evidence.md](./requirement-to-evidence.md)
