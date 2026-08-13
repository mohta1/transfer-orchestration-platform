# TASK-21 — Challenge Leadership Documentation and Architecture Review

**Stage:** Post-submission strict challenge compliance
**Recommended branch:** `docs/challenge-leadership-compliance`
**Depends on:** TASK-20 merged into `main`
**Status:** Done

---

## 1. Cursor execution instruction

Complete this documentation task autonomously. Inspect authoritative sources and actual code/tests; draft, validate, self-review, commit, push, and open a PR. Do not merge. This task must add the missing final deliverables without changing product behavior.

## 2. Authoritative sources

Read completely:

1. `AGENTS.md`.
2. Original challenge, especially §§2, 13, 24, 26–35, 37–42.
3. TASK-01 through TASK-20 and evidence.
4. All five ADRs.
5. Every existing document under `docs/` and root README.
6. All eight mandatory Draw.io diagrams.
7. Actual source/tests/runtime/CI required to verify claims.

The original challenge wording controls the required format. Do not substitute generic policy prose for a requested simulation/report.

## 3. Prepare repository

```bash
git status --short
git fetch origin
git switch main
git pull --ff-only origin main
git status --short
git rev-parse HEAD
git rev-parse origin/main
git switch -c docs/challenge-leadership-compliance
```

Verify TASK-20 is merged. Never reset/stash/discard user work.

## 4. Scope

In scope:

- exact §27 team leadership model;
- exact §29 eight-week debt/delivery trade-off for six named concerns;
- dedicated §30 Architecture Review Simulation in `docs/architecture.md` §25;
- candidate-specific §34 AI-Assisted Engineering submission evidence in `docs/ai-assisted-engineering.md` §15 (team policy remains §1–13);
- stale/contradictory documentation cleanup;
- requirement matrix/README/doc map updates;
- factual corrections to existing diagrams;
- validation of five ADRs/eight diagrams/links/build/tests.

Out of scope:

- product code/tests/migrations/runtime behavior;
- new architecture decision;
- sixth ADR;
- ninth mandatory diagram;
- Legacy runtime implementation;
- monitoring platform;
- stylistic rewriting of all documents.

If source behavior remains a Blocker, stop and report it rather than hiding it in prose.

## 5. Baseline documentation audit

Before editing, inventory:

- empty/placeholder/TODO content;
- broken links;
- stale test counts/SHAs/statuses;
- target behavior presented as current behavior;
- duplicated/conflicting terminology;
- unverified SLA/compliance/production claims;
- exact gaps against §§27, 29, 30 and 34;
- diagram claims versus implementation;
- `docs/tasks/REMAINING-TASKS.md` references and staleness.

Produce a deliverable-to-source-to-file-to-validation plan.

## 6. Team engineering model — §27

Update `docs/team-engineering-model.md` with an explicit recommended model for exactly:

- **Three Backend Developers**;
- **One QA Engineer**;
- **One Product Owner**;
- **One shared DevOps/Platform Engineer**.

Do not imply these are known real employees. This is the proposed challenge team.

Include dedicated sections/tables for every required item:

1. Responsibility boundaries by module and role.
2. Design-review process and triggers.
3. PR review expectations and required reviewers.
4. Definition of Ready with verifiable checklist.
5. Definition of Done with verifiable checklist.
6. Testing ownership across developers and QA.
7. Architecture decision process and ADR rules.
8. Technical-debt handling.
9. Incident-learning process.
10. Mentoring approach.
11. Knowledge-sharing practices.
12. Escalation path for product/security/financial/operational ambiguity.
13. Release-readiness criteria.
14. Knowledge distribution and rotation so Tech Lead is not a bottleneck.

Make the model practical for a Modular Monolith and incremental Legacy migration. Do not propose team-per-microservice.

## 7. Eight-week technical-debt trade-off — §29

Update `docs/technical-debt-prioritisation.md` with a clearly titled challenge exercise:

```text
Eight-Week Product Delivery Trade-off
```

Cover exactly these **six**, not eight, concerns:

1. Legacy account queries are slow.
2. Fraud integration has no idempotency support.
3. Payment-network documentation is incomplete.
4. Existing logs contain account numbers.
5. Automated integration tests are limited.
6. Broker cluster is not production-ready.

For each concern, provide:

- exact classification:
  - `Must resolve before release`;
  - `Can be mitigated temporarily`;
  - `Can be postponed`;
  - `Requires business decision`;
- risk;
- mitigation;
- owner;
- release impact;
- follow-up condition.

Where a concern needs a primary classification plus a business decision, explain the release gate clearly rather than evading prioritization.

Include:

- assumptions;
- week-by-week or phase-based allocation within eight weeks;
- critical-path reasoning;
- what is explicitly not built;
- residual-risk acceptance owner;
- release/no-release conditions.

Do not invent latency numbers, incidents, team capacity beyond the stated team, regulations, or SLAs.

## 8. Architecture Review Simulation — §30

Add or maintain in:

```text
docs/architecture.md — §25 Architecture Review Simulation (§30)
```

Do **not** create a separate `docs/architecture-review-simulation.md` file; content lives in the architecture document.

Review this exact proposal:

> Create separate Microservices for Transfer, Account, Reservation, Fraud, Limit, Notification, Audit, and Reconciliation, each with its own Kafka topic and database.

Required content (§25 subsections):

1. Proposal under review.
2. Strengths of the microservice proposal.
3. Risks.
4. Unnecessary complexity initially.
5. Recommended initial boundaries (includes alternative design — Modular Monolith).
6. Conditions that justify later extraction.
7. Operational and team implications.
8. Data consistency and Legacy implications.
9. Final recommendation.

Address:

- financial boundary between Account and Reservation;
- distributed transaction risk;
- network/operational failure modes;
- Kafka does not create exactly-once business effects;
- Outbox remains necessary;
- one database with module-owned schemas initially;
- team cognitive load with three backend developers;
- independent scale/release/ownership evidence required for extraction;
- why Modular Monolith is not permanent dogma;
- how incremental Hybrid coexistence differs from premature new-system Microservices.

Align with ADR-001–005. Do not create another ADR.

## 9. AI-Assisted Engineering Report — §34

Preserve `docs/ai-assisted-engineering.md` §1–13 as the reusable team policy.

Add or maintain candidate submission evidence in:

```text
docs/ai-assisted-engineering.md — §15 Repository AI Practice (§34 submission evidence)
```

Do **not** create a separate `docs/ai-assisted-engineering-report.md` file.

Maximum length for §15: **two rendered A4 pages** equivalent. Use concise prose/tables and document the rendering assumption. If repository tooling cannot render pages, constrain to approximately 900–1,100 words and report the limitation truthfully.

Required content:

1. Tools actually used.
2. Tasks delegated to AI.
3. Important prompt patterns.
4. Generated-code review process.
5. Architecture-validation process.
6. Test-validation process.
7. One incorrect AI suggestion actually rejected.
8. One generated result actually substantially modified.
9. Decisions that remained the candidate’s own.
10. Risks introduced by AI.
11. One example where AI accelerated delivery.
12. One example where AI increased review effort.
13. Team AI-usage rules.
14. Candidate accountability statement.

### Truthfulness constraint

Do not fabricate personal experience. Use repository/PR/task history to establish verifiable workflow facts. If the exact rejected suggestion or heavily modified result cannot be proven, stop and ask the user one concise question identifying the missing personal example. This is the only permitted clarification in this task.

Acceptable evidence includes actual task prompts, diffs, review findings, CI corrections, rejected architectural directions, and human merge decisions. Do not claim the AI independently verified or approved work.

## 10. Stale artifacts and consistency

### `REMAINING-TASKS.md`

Determine whether anything still links to `docs/tasks/REMAINING-TASKS.md`.

- If redundant and unreferenced, remove it.
- If historical retention is useful, replace its top with an unmistakable `Archived / Superseded` notice and point to `00-ROADMAP-INDEX.md`; remove misleading current-status claims.

Do not leave TASK-15 as Not Started.

### Requirement matrix

Update `docs/requirement-to-evidence.md`:

- baseline/final SHA and date from actual verification;
- Scenario E and Fraud domain test evidence from TASK-19;
- Scenario K and observability evidence from TASK-20;
- §§27, 29, 30 and 34 deliverables;
- truthful status counts;
- partial Legacy runtime items remain partial/non-goals;
- no requirement marked Verified solely from prose when executable evidence is required.

### README and documentation map

Add links to:

- Architecture Review Simulation — `docs/architecture.md` §25;
- AI-Assisted Engineering submission evidence — `docs/ai-assisted-engineering.md` §15;
- updated team/debt documents.

Keep commands and counts accurate.

## 11. Diagram validation and corrections

Maintain exactly these eight mandatory diagrams:

1. `event-storming.drawio`
2. `context-map.drawio`
3. `target-architecture.drawio`
4. `migration.drawio`
5. `transfer-happy-path.drawio`
6. `timeout-reconciliation-sequence.drawio`
7. `transfer-state-diagram.drawio`
8. `deployment-runtime.drawio`

No new mandatory diagram is required.

Review and minimally correct:

- `target-architecture.drawio` clearly says target architecture;
- `deployment-runtime.drawio` distinguishes currently implemented runtime from target/future monitoring or Legacy components;
- `event-storming.drawio` is labeled domain discovery and not proof every sticky note is implemented;
- Fraud retry/manual review matches TASK-19;
- stuck detection/operations matches TASK-20;
- terminology matches Ubiquitous Language;
- no exactly-once claim;
- editable valid Draw.io XML remains.

Do not redesign for aesthetics alone.

## 12. Architecture document length and claims

The challenge recommends a maximum of ten pages excluding diagrams/ADRs. Assess `docs/architecture.md` honestly.

- Do not delete required reasoning merely to chase an unverifiable page count.
- Reduce duplication by linking detailed ADR/team/debt documents.
- §30 simulation content belongs in `architecture.md` §25 (not a separate file).
- §34 submission evidence belongs in `ai-assisted-engineering.md` §15 (policy in §1–13).
- Keep all 24 required architecture topics discoverable.
- Keep quality attributes as targets/assumptions, not production SLA claims.
- Clearly label implemented/current versus target/future behavior.

## 13. Required validation

Run checks for:

- empty/TODO/TBD/FIXME/placeholder files;
- broken relative links and case;
- exactly five ADRs;
- exactly eight mandatory diagrams;
- valid non-empty Draw.io XML;
- all §27 headings;
- all six §29 concerns and exact classifications;
- all §30 content in `architecture.md` §25 (nine subsections);
- every §34 report item in `ai-assisted-engineering.md` §15 and length limit;
- terminology consistency;
- no fabricated metrics/SLA/compliance/production claims;
- no exactly-once wording;
- no secrets/tokens/absolute local paths;
- no product-code/test/runtime/dependency changes.

Then run:

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
git diff --check
```

Required: 0 warnings/errors/failures and no required skips.

## 14. Self-review

Review for generic leadership prose, wrong team composition, seven/eight §29 concerns, missing exact classification, architecture preference without trade-offs, fabricated AI examples, policy substituted for report, >2-page report, target/current confusion, new ADR, missing diagram, broken link, stale count, hidden blocker, production code change, or unrelated rewrite.

Classify findings and fix every Blocker.

## 15. Evidence

Capture:

- baseline SHA/branch;
- files created/updated/removed;
- team-model requirement checklist;
- six-concern §29 matrix;
- architecture-review §25 subsection checklist;
- AI-report §15 item/length verification;
- stale-file resolution;
- requirement-matrix counts;
- ADR/diagram names/counts/XML result;
- link/placeholder/secret checks;
- build/test totals;
- CI result;
- confirmation no product behavior changed;
- self-review findings.

Only mark Done after all evidence passes.

## 16. Commit and PR

Suggested commit:

```text
docs: complete challenge leadership deliverables
```

Push:

```bash
git push -u origin docs/challenge-leadership-compliance
```

Open PR against `main`; do not merge. Wait for final-SHA CI and report PR/CI URLs, matching local/remote SHA, and blockers.
