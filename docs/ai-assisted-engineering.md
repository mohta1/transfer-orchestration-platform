# AI-Assisted Engineering

**Status:** Active policy for this repository
**Principle:** AI tools accelerate work; **humans remain accountable** for merged output

This policy governs use of AI coding assistants (including Cursor, Copilot, ChatGPT, and similar) on the Transfer Orchestration Platform. It complements [engineering-standards.md](./engineering-standards.md) and [AGENTS.md](../AGENTS.md).

AI output is a **proposal**, not evidence. An AI tool must never approve its own security, financial, architecture, or release decisions.

---

## 1. Appropriate AI-Assisted Activities

| Activity | Appropriate use |
| -------- | ---------------- |
| **Planning** | Draft TASK breakdowns, explore options — must be validated against TASK files and ADRs |
| **Code generation** | Boilerplate, tests, mappings — must match existing conventions after inspection |
| **Test generation** | Scenarios from requirements — must use real PostgreSQL where mandated |
| **Documentation** | Draft docs from verified code/tests — must be fact-checked before merge |
| **Review** | First-pass review checklist — human reviewer decides Blocker vs preference |
| **Investigation** | Search assistance, log/stack trace explanation — verify against source |

---

## 2. Mandatory Human Accountability

Every merged change requires a **human author** who:

1. Read the relevant TASK, ADRs, and existing implementation.
2. Ran `dotnet build` and `dotnet test` (with PostgreSQL for integration tests).
3. Confirmed the diff scope matches one TASK.
4. Captured truthful evidence in the TASK file / PR description.
5. Accepts review accountability for financial and security correctness.

---

## 3. Grounding Requirements

Before generating or accepting AI suggestions:

- Read [AGENTS.md](../AGENTS.md) and the current **TASK file** completely.
- Read applicable **locked ADRs** (all five under [docs/adr/](./adr/)).
- **Inspect the repository** — do not assume files, APIs, or test names exist.
- Use [docs/ubiquitous-language.md](./ubiquitous-language.md) for domain terms.
- Prefer **small, scoped diffs** aligned with one TASK branch.

---

## 4. Workflow Expectations

| Rule | Rationale |
| ---- | --------- |
| One TASK per branch/PR | Scope control and reviewability |
| Tests with behaviour | AI-generated code without tests is incomplete |
| Warning-free build | TreatWarningsAsErrors is non-negotiable |
| Evidence capture | TASK Done requires verified commands/results |
| No TASK-18 work early | README/demo/submission gate are separate |

---

## 5. Security and Privacy Boundaries

### 5.1 Prohibited inputs to AI tools

Do **not** submit to external or shared AI services:

- Production or shared-environment **secrets**, JWT signing keys, API tokens
- **Customer PII**, full account numbers, raw Idempotency Keys from production
- **Production logs** with sensitive content
- **Credentials** from `.env`, secret managers, or CI secrets
- Restricted or confidential organisational data not already in the public repo

Local-only AI (fully offline, no data egress) may reduce exposure but does **not** remove human review obligations.

### 5.2 Prohibited outputs in commits

- AI-generated **secrets**, passwords, or signing keys — even "example" keys that look real
- Unsafe test fixtures using production-like credentials
- Absolute **local machine paths** in documentation (`C:\Users\...`, `/home/...`)

Use placeholders from [.env.example](../.env.example) patterns only.

### 5.3 Incident handling if sensitive data is exposed

1. **Revoke/rotate** exposed credentials immediately.
2. **Remove** sensitive content from prompts/history where the tool allows.
3. **Notify** security/platform owner per organisational policy.
4. **Do not commit** the exposed material; scrub from branches if accidentally staged.

---

## 6. High-Risk Areas — Extra Verification

Human verification is **mandatory** for AI-generated changes in:

| Area | Verify |
| ---- | ------ |
| **SQL / migrations** | Constraint names, rollback, schema ownership, no cross-schema FK abuse |
| **Concurrency** | Optimistic tokens, unique constraints, bounded retry, no double financial effect |
| **Financial invariants** | Available balance ≥ 0, one reservation per Transfer, Consume/Release idempotency |
| **Security policy** | 401/403/404 semantics, role checks, audit actor from claims |
| **Retry / Outbox** | At-least-once only; poison-message bounds; atomic outbox commit |
| **Infrastructure** | Docker/CI changes reproducible; no secrets in YAML |

Run the specific integration tests cited in [requirement-to-evidence.md](./requirement-to-evidence.md) — do not trust AI claims of green CI.

---

## 7. Known AI Risks

| Risk | Mitigation |
| ---- | ---------- |
| **Hallucinated APIs/files** | Grep/read before import; compile and test |
| **Fabricated test results** | Run tests locally; paste actual counts |
| **Invented business rules** | Cross-check Ubiquitous Language and Transfer aggregate |
| **Unverified production claims** | Quality attributes remain targets; no SLA/certification language |
| **Exactly-once wording** | Reject; use at-least-once + idempotency |
| **Cross-module DbContext access** | Architecture tests must pass |
| **Dependency/license issues** | Human reviews new NuGet packages |

---

## 8. Dependency and Licensing Review

AI-suggested packages require human check:

- License compatible with project use
- Minimal dependency footprint
- No duplicate library for same concern
- Not added for speculative future TASKs

---

## 9. Traceability

PRs for substantial AI-assisted work should note:

- TASK ID
- Which sections were AI-drafted vs human-written
- Verification commands run
- Any specialist review (security/financial) obtained

TASK evidence sections record **commands and outcomes**, not "AI verified."

---

## 10. Reviewer Expectations

Reviewers treat AI-authored PRs like any other PR. Additional scrutiny for:

- Financial and concurrency paths
- Migration safety
- Secret leakage in diffs or docs
- Vague evidence ("implemented as described")

Classify findings: **Blocker**, **Non-blocking improvement**, **Preference**.

---

## 11. Specialist Review Triggers

Require designated human specialist review when changes touch:

- Account reservation or balance invariants
- Payment Network submission after timeout
- Outbox/consumer deduplication semantics
- Manual operations and audit identity
- Authentication/authorisation policy
- Locked ADR boundaries or migration routing

---

## 12. Examples

### Acceptable

- AI drafts `engineering-standards.md` section after agent read ADRs and test projects; human fixes ADR-004 link and runs full test suite.
- AI generates additional PostgreSQL concurrency test **from** existing `AccountReservationContractTests` patterns; human runs tests and confirms constraint behaviour.
- AI suggests Problem Details wording; human confirms status codes match `SecurityBoundaryTests`.

### Unacceptable

- AI adds `AppDbContext` "to simplify queries" across modules.
- AI retries Payment Network submission automatically after timeout.
- AI marks requirement "Verified" in matrix without citing test method.
- AI commits `.env` with generated JWT key for "local testing."
- AI approves PR claiming "production-ready SLA achieved."

---

## 13. AI-Assisted Definition of Done

AI-assisted work is done only when the human author confirms:

- [ ] TASK scope and ADRs read
- [ ] Repository inspected; no hallucinated references
- [ ] Build: 0 warnings, 0 errors
- [ ] Tests pass (PostgreSQL for integration)
- [ ] No secrets or local paths in diff
- [ ] Documentation claims match code/tests
- [ ] PR evidence is from **executed** commands, not AI assertion
- [ ] Specialist review obtained where triggered
- [ ] Review findings classified; no unresolved Blockers

---

## 14. Related Documents

- [engineering-standards.md](./engineering-standards.md)
- [team-engineering-model.md](./team-engineering-model.md)
- [requirement-to-evidence.md](./requirement-to-evidence.md)
