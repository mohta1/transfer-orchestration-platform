# Post-TASK-18 Challenge Gap Assessment and Remediation Plan

**Repository:** `mohta1/transfer-orchestration-platform`  
**Reviewed baseline:** `main` at `0a60171987920afc71048d6630d23feb2903e147` (PR #28 merged)  
**Source of truth:** `Mohsen Taheri Backend Engineering Challenge 1.md`  
**Purpose:** close the remaining strict-brief gaps without redesigning the verified baseline.

## 1. Executive decision

The repository is strong but should not yet be treated as fully compliant with the literal challenge brief. Four documentation deliverables are materially incomplete, and two failure-handling areas need code if the submission is intended to withstand a strict line-by-line review.

Recommended sequence:

1. **TASK-19 — Fraud Screening Resilience** — code, migrations only if strictly necessary, and PostgreSQL tests.
2. **TASK-20 — Stuck-Transfer Operations and Observability Baseline** — minimal operational query/detection plus focused structured telemetry and tests.
3. **TASK-21 — Leadership Deliverables and Architecture Review** — documentation and diagram truthfulness only.
4. **TASK-22 — Final Compliance Re-audit** — clean-room verification and README/evidence refresh only.

Use one branch and one PR per task. Never work directly on `main`, never merge automatically, and do not start the next task before the prior PR is reviewed and merged.

## 2. Corrections to the Cursor gap analysis

| Cursor claim | Correct interpretation from the brief |
|---|---|
| §29 contains eight PO concerns | **Incorrect.** The brief lists exactly **six** concerns. |
| Runtime Prometheus/OpenTelemetry metrics are mandatory | **Too strong.** §24 requires a focused observability baseline and asks the candidate to **identify metrics**; it explicitly says full monitoring infrastructure is not required. |
| Legacy runtime ACL must be implemented | **Not required for this challenge.** Incremental modernization must be designed/documented; a complete Legacy integration is a non-goal. |
| Fraud timeout is merely optional | **Not defensible as-is.** A complete Fraud engine is not required and Fraud integration in the critical vertical slice is optional, but Scenario E is a required failure scenario and current code/documentation contradict one another. |
| Stuck-transfer worker specifically is mandatory | **Too prescriptive.** The brief requires detection, operational visibility, investigation, and auditable recovery. A worker is one option, not the requirement itself. |

## 3. Finding classification

### Blockers for strict brief compliance

| ID | Finding | Evidence | Required response |
|---|---|---|---|
| B-01 | Fraud timeout/temporary unavailability is not represented or durably recoverable | `DecisionOutcome` contains only `Approved` and `Rejected`; `TransferSubmissionService` calls Fraud synchronously before scheduling durable work | TASK-19 code and tests |
| B-02 | Explicit domain test “Fraud-rejected Transfer cannot continue” is missing | §23 Domain Test #5; current proof is integration-oriented | Add focused domain tests in TASK-19 |
| B-03 | Stuck Transfer is not actually queryable/detectable by Operations in runtime | Required Scenario K; diagrams/docs claim operational detection, but no corresponding query/detector endpoint exists | TASK-20 code and tests |
| B-04 | §27 exact team leadership deliverable is incomplete | Exact team composition, Definition of Ready, mentoring, escalation, and release readiness are required | TASK-21 documentation |
| B-05 | §29 eight-week trade-off exercise is missing in required form | Six named concerns must use four exact classifications with risk, mitigation, owner, release impact, and follow-up condition | TASK-21 documentation |
| B-06 | §30 dedicated Architecture Review Simulation is missing | Required structured response to the eight-Microservice/Kafka/database proposal | TASK-21 documentation |
| B-07 | §34 requires a maximum-two-page AI-Assisted Engineering **Report**, not only a policy | Current file is a useful policy but lacks the candidate-specific report fields | TASK-21 documentation |

### Strong improvements that should be included while closing blockers

| ID | Finding | Response |
|---|---|---|
| I-01 | §24 structured observability baseline is incomplete in runtime code | Add narrowly scoped structured logs/instrumentation for state changes, external duration/outcome, retry/reconciliation attempts, concurrency conflicts, and safe identifiers in TASK-20. |
| I-02 | Some architecture/diagram wording presents planned Fraud retry and stuck detection as implemented facts | Relabel target/design statements and distinguish implemented baseline from future/target behavior in TASK-21. |
| I-03 | `docs/tasks/REMAINING-TASKS.md` is stale | Remove it if redundant, or replace it with an explicit archived notice that points to the authoritative roadmap. Do not leave false task statuses. |
| I-04 | `CompensationRequired` has relatively weak coverage | Add narrowly scoped tests only if they do not require inventing an undefined compensation policy; otherwise keep it as truthful debt/non-goal. |

### Non-blocking and intentionally out of scope

- Full Fraud engine or real Fraud provider.
- Full Legacy runtime integration/routing.
- Kafka/RabbitMQ.
- Prometheus/Grafana/OpenTelemetry backend deployment.
- Kubernetes/cloud deployment.
- Full accounting ledger.
- Production identity provider.

## 4. TASK-19 — Fraud Screening Resilience

**Recommended branch:** `feature/fraud-screening-resilience`  
**Suggested PR title:** `feat(fraud): make screening failures durably recoverable`

### Objective

Close required Fraud Scenario E without implementing a complete Fraud engine. Model Fraud outcomes explicitly, prevent silent progression, make timeout/unavailability recoverable with bounded durable retries, and escalate unresolved screening to Manual Review.

### Mandatory design constraints

- Preserve the Modular Monolith, module-owned persistence, and Process Manager.
- Do not call a slow Fraud dependency inside a database transaction.
- Do not treat timeout or temporary unavailability as Approved or Rejected.
- Do not proceed to Balance Reservation without a definitive Fraud approval.
- Use a stable screening request identity so retries cannot create duplicate business effects.
- Retry policy must be bounded, configurable, UTC-safe, restart-safe, and deterministic in tests.
- Persist recovery state before returning/losing control.
- Do not hold an Account reservation while Fraud remains unresolved; Fraud remains before Reservation.
- Escalation after exhaustion must result in `ManualReviewRequired` or an equally explicit justified state.
- Do not invent Fraud scoring rules or a real provider.

### Required implementation

1. Replace the shared binary `DecisionOutcome` use for Fraud with a Fraud-specific result model supporting:
   - `Approved`;
   - `Rejected`;
   - `ManualReviewRequired`;
   - `Timeout`;
   - `TemporarilyUnavailable`.
2. Keep authorization and daily-limit decisions separate from Fraud semantics.
3. Add a durable Process Manager action/step for Fraud screening rather than relying on an unrecoverable synchronous call.
4. Persist attempt count, next-attempt time, and stable request identity using the existing durable process model where possible.
5. Add bounded retry options with startup validation.
6. On approval, transition to `PendingBalanceReservation` and schedule `ReserveBalance` exactly once.
7. On rejection, transition to `FraudRejected`, complete the workflow, and never reserve funds.
8. On manual-review result or exhausted transient attempts, transition to `ManualReviewRequired` and create no reservation.
9. Preserve HTTP idempotency, replay, correlation, concurrency, and cancellation semantics.
10. Update only the minimum API response semantics needed to represent an accepted-but-processing screening workflow truthfully.

### Required tests

- Domain: `RejectForFraud` transitions to `FraudRejected`.
- Domain: `FraudRejected` cannot request Balance Reservation, external submission, settlement, or completion.
- Domain: timeout/unavailable does not produce `FraudRejected` or approval.
- Integration/PostgreSQL: Fraud timeout leaves durable recoverable work and no reservation.
- Integration/PostgreSQL: temporary unavailability retries with the same stable screening identity.
- Integration/PostgreSQL: retries are bounded and escalate to Manual Review.
- Integration/PostgreSQL: restart rediscovers pending Fraud work.
- Integration/PostgreSQL: duplicate worker delivery/claim cannot perform screening progression twice.
- Integration/PostgreSQL: Fraud rejection never reaches reservation.
- Integration/PostgreSQL: Fraud approval schedules one reservation and preserves existing idempotency behavior.
- Regression: all existing 240 tests remain green, adjusted only where the public workflow result legitimately changes.

Do not use arbitrary sleeps or EF Core InMemory for durable/retry/restart proof.

### Acceptance evidence

- Requirement-to-code-to-test matrix for Scenario E and Domain Test #5.
- Focused test commands and actual results.
- Full solution totals, 0 warnings/errors.
- Two fresh-process PostgreSQL integration runs.
- Diff review proving no full Fraud engine, new service, or broker was introduced.

## 5. TASK-20 — Stuck-Transfer Operations and Observability Baseline

**Recommended branch:** `feature/operations-observability`  
**Suggested PR title:** `feat(operations): expose stuck transfers and focused telemetry`

### Objective

Satisfy Scenario K and the focused §24 observability baseline with the smallest operational capability that is honest, secure, and testable.

### Mandatory design constraints

- Do not assume that “stuck” always means failed.
- Detection is based on configurable state age and explicit eligible non-terminal states.
- Use UTC and controlled time.
- Keep business recovery in existing audited manual-operation paths.
- Do not allow Operations to edit module tables directly.
- Do not expose customer/account-sensitive values.
- Do not build a complete monitoring platform.

### Required implementation

1. Add a deliberate TransferManagement query contract that returns stuck-work projections without exposing EF types.
2. Define and validate stuck thresholds/options.
3. Query eligible non-terminal Transfer/Process records based on authoritative process state and `UpdatedAtUtc`/due-work state.
4. Expose an operator-only operational endpoint/read model, for example `GET /api/operations/stuck-transfers`, with bounded paging/limit.
5. Include only safe fields required for investigation: Transfer ID, state, process status/action, age timestamps, attempt count, correlation ID, and reason/category where available.
6. Ensure customer principals cannot access it.
7. Preserve existing audited manual reject/confirm-settlement commands as recovery actions; do not add arbitrary state editing.
8. Add focused structured telemetry for:
   - state transition or workflow-step outcome;
   - safe Transfer/Correlation/Causation identifiers;
   - masked or hashed Account identifier if logged at all;
   - safe idempotency-key fingerprint, never raw key;
   - Fraud/Payment external-call duration and outcome;
   - retry and reconciliation attempts;
   - Outbox result/backlog observation;
   - concurrency conflicts;
   - manual actions;
   - unknown/stuck status.
9. Identify the complete required metric catalogue in documentation. Runtime Meter/Counter instruments may be added if small and dependency-free, but Prometheus/OpenTelemetry infrastructure is not required.

### Required tests

- PostgreSQL: old eligible non-terminal process appears in the stuck query.
- PostgreSQL: recent process does not appear.
- PostgreSQL: completed/rejected/cancelled process does not appear.
- PostgreSQL: threshold boundary is deterministic.
- PostgreSQL: result is bounded and ordered deterministically.
- API: unauthenticated request returns 401.
- API: customer request returns 403.
- API: operator request succeeds.
- API: no account/customer-sensitive fields leak.
- PostgreSQL: an operator can investigate then invoke an existing audited recovery command.
- Audit actor still comes from authenticated `sub`.
- Structured logging tests prove required fields and secret/raw-key masking.
- Existing health, security, reconciliation, Outbox, and full solution tests remain green.

### Acceptance evidence

- Scenario K requirement-to-code-to-test mapping.
- Deployed API example using an operator token.
- Persisted audit evidence for recovery.
- Observability field/metric matrix showing “implemented log,” “implemented metric,” or “identified only.”
- Full build/test and CI results.

## 6. TASK-21 — Leadership Deliverables and Architecture Review

**Recommended branch:** `docs/challenge-leadership-compliance`  
**Suggested PR title:** `docs: complete challenge leadership deliverables`

### Objective

Complete §§27, 29, 30, and 34 exactly as requested, correct stale artifacts, and make diagrams/doc claims align with the post-TASK-20 implementation.

### Required deliverables

#### A. Team engineering model (§27)

Update `docs/team-engineering-model.md` to explicitly describe this exact team:

- Three Backend Developers.
- One QA Engineer.
- One Product Owner.
- One shared DevOps/Platform Engineer.

Add dedicated, concrete sections for:

- responsibility boundaries;
- design review;
- PR review expectations;
- Definition of Ready;
- Definition of Done;
- testing ownership;
- architecture decisions;
- technical debt;
- incident learning;
- mentoring;
- knowledge sharing;
- escalation path;
- release readiness;
- avoidance of Tech Lead knowledge bottleneck.

#### B. Eight-week technical-debt trade-off (§29)

Add a dedicated section to `docs/technical-debt-prioritisation.md` covering exactly these six concerns:

1. Legacy account queries are slow.
2. Fraud integration has no idempotency support.
3. Payment-network documentation is incomplete.
4. Existing logs contain account numbers.
5. Automated integration tests are limited.
6. Broker cluster is not production-ready.

For each, use one or more of the exact brief classifications where necessary:

- `Must resolve before release`;
- `Can be mitigated temporarily`;
- `Can be postponed`;
- `Requires business decision`.

Include risk, mitigation, owner, release impact, and follow-up condition. Explain prioritization within the fixed eight-week window. Do not invent production measurements.

#### C. Architecture Review Simulation (§30)

Create `docs/architecture-review-simulation.md` with a direct review of the proposed eight Microservices, one Kafka topic and one database per service.

Use explicit sections:

- Strengths.
- Risks.
- Unnecessary complexity.
- Alternative design.
- Recommended initial boundaries.
- Conditions for later extraction.
- Operational implications.
- Team-size implications.
- Data-consistency implications.
- Final recommendation.

Align with ADR-001 through ADR-005. Do not add a sixth ADR.

#### D. AI-Assisted Engineering Report (§34)

Preserve the reusable policy, but add a separate candidate-specific report, maximum two pages, at `docs/ai-assisted-engineering-report.md`.

It must truthfully include:

- tools actually used;
- tasks delegated;
- important prompt patterns;
- generated-code review process;
- architecture validation;
- test validation;
- one incorrect suggestion actually rejected;
- one generated result actually substantially modified;
- decisions retained by the candidate;
- AI risks;
- one acceleration example;
- one increased-review-effort example;
- team AI rules;
- candidate responsibility statement.

Do not fabricate personal examples. If the exact example is uncertain, obtain it from the candidate before writing the final report.

#### E. Stale and contradictory documentation

- Resolve `docs/tasks/REMAINING-TASKS.md`: archive with an unmistakable superseded notice or remove it if nothing links to it and repository conventions permit removal.
- Update `docs/requirement-to-evidence.md` with new Scenario E, Domain Test #5, Scenario K, and §24 evidence.
- Correct any statement that claims a target behavior is currently implemented when it is only planned.
- Keep exactly five ADRs and eight mandatory diagrams.

### Diagram work

No new mandatory diagram is needed. The existing Event Storming and state diagrams already contain Fraud timeout/manual review and stuck-transfer concepts.

Perform only factual corrections:

- Ensure `deployment-runtime.drawio` distinguishes implemented runtime components from target/future monitoring capabilities.
- Ensure `target-architecture.drawio` is clearly labeled as target architecture.
- Ensure `event-storming.drawio` remains a domain-discovery model, not a claim that every sticky note is implemented.
- Update diagram references/legends only when required by TASK-19/TASK-20 changes.

### Validation

- Required documents substantive and within requested size limits.
- AI report maximum two pages under a documented rendering assumption.
- Exactly five ADRs.
- Exactly eight mandatory diagrams.
- Draw.io XML valid.
- All relative links resolve.
- No TODO/TBD/placeholders.
- No unverified SLA, compliance, exactly-once, or production-readiness claim.
- Full build/test and CI remain green.

## 7. TASK-22 — Final Compliance Re-audit

**Recommended branch:** `release/challenge-compliance-final`  
**Suggested PR title:** `docs: finalize strict challenge compliance evidence`

This task starts only after TASK-19 through TASK-21 are merged.

### Scope

- Re-read the original challenge brief line by line.
- Re-run all domain/integration/architecture tests.
- Re-run clean Docker/Compose validation.
- Re-run README demo.
- Update final counts and evidence.
- Verify the six §29 concerns, §30 simulation, §27 team model, and §34 report.
- Verify Scenario E, Scenario K, Domain Test #5, and §24 baseline.
- Confirm exactly five ADRs and eight diagrams.
- Confirm no secret/generated artifact.
- Confirm final PR CI on the exact branch SHA.
- Do not claim final `main` verification until the user merges and `main` is checked afterward.

No new product feature or architecture redesign is allowed in TASK-22.

## 8. Final recommendation

For a normal take-home review, the existing submission may already score strongly. For strict compliance with the supplied brief, complete TASK-19, TASK-20, and TASK-21 before sending it.

Do **not** spend time implementing Legacy runtime integration, a full Fraud engine, Kafka, Kubernetes, or a full metrics backend. Those would expand scope without resolving the most important scoring gaps.
