# TASK-20 — Stuck-Transfer Operations and Focused Observability

**Stage:** Post-submission strict challenge compliance
**Recommended branch:** `feature/operations-observability`
**Depends on:** TASK-19 merged into `main`
**Status:** Done

---

## 1. Cursor execution instruction

Implement this task completely and autonomously in `transfer-orchestration-platform`. Inspect, plan, implement, test, verify, self-review, commit, push, and open a pull request. Do not merge. No further explanation should be required.

## 2. Authoritative sources

Read completely before editing:

1. `AGENTS.md`.
2. Original challenge §§7.2 item 24–26, 7.3, 12 Scenario K, 19, 24 and 25.
3. `docs/tasks/00-ROADMAP-INDEX.md`.
4. TASK-11 through TASK-19 and their evidence.
5. All five ADRs, especially ADR-002 and ADR-004.
6. Architecture, Ubiquitous Language, Event Storming, engineering standards, team model, technical debt, requirement matrix, runtime setup, README, and all eight diagrams.
7. Process State persistence/query code, Reconciliation, Outbox, AuditOperations, manual endpoints, security policies, correlation middleware, workers, logging, and tests.

Follow authoritative sources if a conflict exists and report it.

## 3. Prepare repository

```bash
git status --short
git fetch origin
git switch main
git pull --ff-only origin main
git status --short
git rev-parse HEAD
git rev-parse origin/main
```

Verify TASK-19 is merged and no unrelated change exists. Stop rather than reset/stash/discard user work.

Create:

```bash
git switch -c feature/operations-observability
```

## 4. Problem and objective

Scenario K requires a Transfer stuck in an intermediate state to be:

- detected;
- visible in operational monitoring;
- investigable by an operator;
- recoverable through auditable actions.

Current documentation/diagrams model this target, but runtime does not expose a deliberate stuck-work query/detector. §24 also requires a focused structured-observability baseline, while full monitoring infrastructure is explicitly not required.

Implement the smallest secure operational capability that satisfies these outcomes without a generic workflow dashboard, direct database editing, or a full metrics platform.

## 5. Plan before editing

Create a requirement-to-code-to-test plan covering:

- authoritative timestamps/state used for age;
- eligible non-terminal states/actions;
- threshold configuration;
- module query contract;
- operator API policy;
- safe projection;
- interaction with existing manual actions/audit;
- logging/instrumentation gaps against every §24 field;
- tests and PostgreSQL setup;
- migration need;
- explicit non-goals.

## 6. Stuck definition

Define “stuck” deliberately:

- based on UTC and a validated configurable state-age threshold;
- applies only to explicitly eligible non-terminal workflow states;
- distinguishes scheduled future work from overdue work;
- excludes terminal Completed/Rejected/Cancelled/FraudRejected states;
- handles waiting/unknown/reconciliation/manual-review states intentionally;
- does not label a correctly delayed retry as stuck before its due time;
- does not mutate Transfer state merely because it is listed;
- yields deterministic ordering.

Document assumptions and revisit conditions. Do not invent a production SLA. The configured value is a challenge/demo operational threshold.

## 7. Query contract and persistence

1. Introduce a deliberate TransferManagement contract for querying stuck workflows.
2. Do not expose EF Core types, DbContext, entities, or private tables.
3. Query only TransferManagement-owned persistence.
4. Use an efficient bounded projection and appropriate index only if query evidence shows it is required.
5. Result fields may include only:
   - Transfer ID;
   - Transfer state;
   - process status/current step/next action;
   - attempt count;
   - created/updated/next-attempt timestamps;
   - calculated age or threshold-crossed timestamp;
   - correlation ID;
   - safe operational category/reason.
6. Do not expose raw account/customer identity, idempotency key, token, payload, connection details, or provider secrets.
7. Bound maximum page size/count and validate inputs.
8. Use cancellation and `AsNoTracking` where appropriate.

## 8. Operational API

Expose an explicit operator-only route, preferably:

```text
GET /api/operations/stuck-transfers
```

Requirements:

- `Operator` policy required;
- missing authentication → existing Problem Details `401`;
- authenticated Customer → `403`;
- Operator → `200` with safe bounded read model;
- correlation response behavior preserved;
- no business rule in endpoint;
- no arbitrary state-edit endpoint;
- no anonymous fallback;
- no cross-customer/customer-facing route.

Existing manual reject/confirm-settlement commands remain the only supported recovery actions unless the original challenge and current state model prove another minimal audited command is necessary.

## 9. Detection mechanism

At minimum, on-demand query detection must be complete and testable. A periodic detector/background worker may be added only if required to make stuck work observable without polling and if it remains small.

If a worker is added:

- use durable PostgreSQL state as source of truth;
- avoid duplicate audit/noise through idempotent detection;
- use controlled time and bounded batches;
- do not create irreversible effects;
- do not introduce a new queue/broker;
- do not turn detection into automatic financial recovery.

Do not add a worker merely because the task title mentions detection.

## 10. Focused observability baseline

Create a matrix for every §24 field and classify it as `Implemented structured log`, `Implemented metric`, `Persisted/queryable evidence`, or `Identified metric only`.

Close material runtime gaps for:

- structured logging;
- Correlation ID;
- Causation ID;
- Transfer ID;
- safe/masked Account identifier where truly needed;
- safe representation/fingerprint of Idempotency Key, never raw key;
- state-transition/workflow-step outcomes;
- Fraud and Payment external-call duration/outcome;
- retry attempts;
- reconciliation attempts/outcomes;
- Outbox publication status/backlog observation;
- concurrency conflicts;
- manual actions;
- unknown submission status;
- stuck-work detection.

Security rules:

- never log bearer tokens, signing keys, passwords, connection strings, raw account numbers, raw Idempotency Keys, PII, SQL text containing values, or external secrets;
- prefer `LoggerMessage`/structured templates consistent with the repository;
- logging failures must not alter business behavior;
- avoid high-cardinality runtime metrics containing Transfer/Account IDs.

Identify these challenge metrics in documentation:

- submitted/completed/rejected Transfers;
- duplicates and idempotency conflicts;
- insufficient balance;
- Fraud rejection/failure;
- Manual Review;
- Payment timeout/unknown status;
- Reconciliation success/attempts;
- Outbox backlog/retries;
- stuck Transfers;
- old active reservations;
- end-to-end latency.

Prometheus, Grafana, an OpenTelemetry collector, and a full monitoring stack are out of scope. Small dependency-free `System.Diagnostics.Metrics` instruments are optional only when they add clear evidence without destabilizing the baseline.

## 11. Manual recovery and audit

Prove the operator can:

1. discover an eligible stuck/manual-review Transfer;
2. inspect safe operational context;
3. invoke an existing authorized recovery command when state permits;
4. create an immutable audit record with authenticated `sub`, reason, before/after state, correlation/causation and timestamp.

Client-supplied actor headers/body must not impersonate the operator. Detection itself must not falsely create a successful recovery audit record.

## 12. Required tests

### Query/domain/application tests

- threshold validation;
- eligible-state classification;
- scheduled future retry not prematurely stuck;
- exact threshold boundary;
- UTC enforcement;
- bounded maximum result count;
- deterministic order.

### PostgreSQL tests

1. Old eligible process appears.
2. Recent eligible process does not appear.
3. Future scheduled work does not appear.
4. Overdue due work appears according to policy.
5. Completed/Rejected/Cancelled/FraudRejected states do not appear.
6. Waiting/SubmissionStatusUnknown/ManualReview behavior matches documented policy.
7. Multiple results are ordered and bounded.
8. Query projection contains no sensitive fields.
9. Restart/new host yields the same detection from durable state.
10. Concurrent detection does not mutate or duplicate business/audit effects.

### API/security tests

- no credentials → 401 Problem Details;
- Customer → 403;
- Operator → 200;
- malformed token → 401;
- correlation header/body behavior;
- response schema and content type;
- no account/customer/token/raw-key leakage.

### Audit/recovery tests

- discovered Transfer can use an existing valid manual recovery path;
- authenticated actor reaches audit;
- client actor impersonation fails;
- denied request changes no business/audit/Outbox state.

### Observability tests

- required structured fields exist for representative workflow/external/retry/reconciliation/Outbox/manual/stuck events;
- tokens, signing keys, raw account values and raw idempotency keys are absent;
- logging does not change behavior;
- controlled duration values can be asserted without sleeping.

Use real PostgreSQL for persistence, detection, audit and restart assertions.

## 13. Documentation

Update:

- `README.md` operational endpoint and limitations;
- `docs/architecture.md` observability/current behavior;
- `docs/engineering-standards.md` if needed;
- `docs/requirement-to-evidence.md` for Scenario K and §24;
- `docs/technical-debt-prioritisation.md` only for genuinely changed debt;
- relevant API `.http` examples using safe placeholders.

Do not perform TASK-21 leadership-document scope or diagram redesign.

## 14. Verification

Run focused slices, then:

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
```

Required: 0 warnings/errors/failures/skipped-required tests.

Also run:

- focused stuck-query tests;
- focused operator/security tests;
- focused audit/log-redaction tests;
- complete PostgreSQL suite twice in fresh processes;
- architecture tests;
- Compose health/runtime verification if API/config changed;
- `git diff --check`.

## 15. Self-review

Check:

- false positives/negatives in stuck definition;
- unbounded query;
- sensitive projection/logging;
- endpoint authorization gaps;
- direct table editing;
- unaudited recovery;
- metric cardinality;
- arbitrary sleeps;
- worker noise/duplicate effects;
- cross-module DbContext access;
- unverified production SLA claims;
- Prometheus/platform scope creep;
- TASK-21 work introduced early.

Classify findings exactly and fix every Blocker.

## 16. Evidence and completion

Capture:

- baseline SHA/branch;
- stuck definition and assumptions;
- files/migration changes;
- endpoint-policy matrix;
- Scenario K mapping;
- §24 field/metric matrix;
- API examples;
- PostgreSQL/restart/audit evidence;
- redaction evidence;
- test totals and repeated runs;
- build warnings/errors;
- self-review;
- confirmation no full monitoring stack or future documentation task was implemented.

Only mark Done after all checks pass.

## 17. Commit and PR

Verify final diff and absence of secrets/artifacts. Suggested commit:

```text
feat(operations): expose stuck transfers and focused telemetry
```

Push:

```bash
git push -u origin feature/operations-observability
```

Open PR against `main`; do not merge. Wait for CI on final SHA and report PR/CI URLs, local/remote SHAs and blockers.

## 18.1 Completion evidence (2026-08-13)

- **Baseline main SHA:** `80cdc40` (TASK-19 merged)
- **Branch:** `feature/operations-observability`
- **No migration:** reuses existing `transfers`, `transfer_process_states`, `reconciliation_records`
- **Stuck definition:** UTC age from latest transfer/process update; default threshold 600s; excludes terminal states and future scheduled work
- **Endpoint:** `GET /api/operations/stuck-transfers` (Operator policy)
- **Observability:** `OperationalTelemetry` structured logs; logging failures do not alter business behavior
- **Build:** 0 warnings, 0 errors
- **TASK-21:** not implemented
