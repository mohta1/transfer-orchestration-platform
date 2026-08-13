# TASK-19 — Fraud Screening Resilience and Recoverable Failure Handling

**Stage:** Post-submission strict challenge compliance
**Recommended branch:** `feature/fraud-screening-resilience`
**Depends on:** TASK-18 merged into `main`
**Status:** Done

---

## 1. Cursor execution instruction

Implement this task completely in the existing `transfer-orchestration-platform` repository.

Work autonomously from repository inspection through planning, implementation, tests, verification, self-review, commit, push, and pull-request creation. Do not merge the pull request. The user performs merges manually.

This file is the complete execution instruction. Do not ask for additional design direction unless an unavoidable business choice would materially change financial correctness. Prefer the smallest implementation consistent with the original challenge, existing architecture, and locked ADRs.

## 2. Authoritative sources

Before modifying any file, read completely:

1. `AGENTS.md`.
2. The original `Mohsen Taheri Backend Engineering Challenge 1.md`, especially §§5.4, 6, 7, 12 Scenario E, 15, 19, 20, 23 and 24.
3. `docs/tasks/00-ROADMAP-INDEX.md`.
4. `docs/tasks/TASK-04-http-idempotency.md` through `TASK-08-payment-network-acl.md`.
5. `docs/tasks/TASK-11-reconciliation.md`, `TASK-14-security-boundary.md`, `TASK-15-test-hardening.md`, and `TASK-18-final-challenge-review.md`.
6. All five locked ADRs, especially ADR-002, ADR-003 and ADR-004.
7. `docs/architecture.md`, `docs/ubiquitous-language.md`, `docs/event-storming-summary.md`, `docs/engineering-standards.md`, and `docs/requirement-to-evidence.md`.
8. Existing Transfer domain, submission flow, Process Manager, workers, persistence, idempotency, account reservation, reconciliation, test fixtures, and PostgreSQL infrastructure.

If this file conflicts with an authoritative business rule or locked ADR, follow the authoritative source and report the discrepancy.

## 3. Repository preparation

Run:

```bash
git status --short
git fetch origin
git switch main
git pull --ff-only origin main
git status --short
git rev-parse HEAD
git rev-parse origin/main
```

Verify:

- working tree has no unrelated changes;
- local `main` equals `origin/main`;
- TASK-18 is merged and marked Done;
- baseline build and all tests are discoverable;
- current Fraud contract contains only the behavior actually present in source.

Do not reset, discard, overwrite, or silently stash user changes. Stop and report an exact blocker if the repository cannot be prepared safely.

Create:

```bash
git switch -c feature/fraud-screening-resilience
```

Never work directly on `main`.

## 4. Problem statement

The original challenge defines Fraud outcomes:

- Approved;
- Rejected;
- ManualReviewRequired;
- Timeout;
- TemporarilyUnavailable.

Required Scenario E says a Fraud timeout must not silently proceed, the workflow must remain recoverable, retry must be bounded and safe, and Manual Review may be triggered where justified.

The current implementation uses the shared binary `DecisionOutcome` (`Approved`/`Rejected`) and invokes Fraud synchronously in `TransferSubmissionService`. Timeout/unavailability is therefore not explicitly represented or durably scheduled. Existing architecture documents describe bounded retry/manual review more strongly than current code proves.

Also, §23 Domain Test #5 requires an explicit domain proof that a Fraud-rejected Transfer cannot continue.

## 5. Objective

Implement a minimal but complete, durable Fraud-screening workflow that:

- models every required Fraud result;
- never treats timeout/unavailability as approval or definitive rejection;
- never reaches Balance Reservation without Fraud approval;
- retries transient outcomes with bounded durable scheduling;
- survives application restart;
- prevents duplicate Fraud progression;
- escalates to Manual Review after an explicit manual-review result or exhausted transient retries;
- preserves existing HTTP idempotency, financial safety, correlation, and architecture boundaries.

Do not implement a complete Fraud engine or real external provider.

## 6. Plan before editing

Before production-code changes, produce a concise requirement-to-code-to-test plan covering:

- current submission transaction boundaries;
- current Transfer Fraud transitions;
- Process Manager actions/state and due-work dispatchers;
- persistence fields already reusable for attempts and scheduling;
- stable Fraud request identity;
- configuration/validation;
- each required unit and PostgreSQL test;
- migration need or proof that no migration is needed;
- API/replay implications;
- explicit out-of-scope items.

Do not begin with speculative refactoring.

## 7. Required domain and contract design

1. Do not reuse a binary decision enum for Fraud.
2. Introduce a Fraud-specific result model with exactly the required semantic outcomes:
   - `Approved`;
   - `Rejected`;
   - `ManualReviewRequired`;
   - `Timeout`;
   - `TemporarilyUnavailable`.
3. Keep customer authorization and daily-limit decision types separate.
4. Define a stable Fraud screening request identifier derived from durable business identity, preferably Transfer ID or a persisted immutable screening reference.
5. Retries must reuse the same identity.
6. A definitive Fraud rejection transitions the Aggregate to `FraudRejected`.
7. `FraudRejected` must reject Balance Reservation, external submission, settlement, completion, and normal workflow continuation.
8. Manual-review escalation must use the established `ManualReviewRequired` state without weakening operator authorization/audit.
9. Timeout/unavailability must not be represented as `FraudRejected`.
10. No domain type may depend on EF Core, hosted services, ASP.NET, or provider SDK types.

## 8. Required durable workflow

Implement Fraud screening as durable Process Manager work rather than an unrecoverable one-shot submission call.

Required sequence:

1. Validate and claim HTTP idempotency as today.
2. Create Transfer and persistent Process State.
3. Complete customer authorization and daily-limit behavior consistently with existing semantics.
4. Transition Transfer to `PendingFraudScreening`.
5. Persist a due Fraud-screening action before relying on the external outcome.
6. A worker/dispatcher claims due work using existing optimistic-concurrency/lease conventions.
7. Call the Fraud port outside a database transaction.
8. Apply result in a short transaction:
   - Approved → `PendingBalanceReservation`, schedule one `ReserveBalance` action.
   - Rejected → `FraudRejected`, complete process, no reservation.
   - ManualReviewRequired → `ManualReviewRequired`, no reservation.
   - Timeout/TemporarilyUnavailable below maximum attempts → persist attempt and next retry.
   - Timeout/TemporarilyUnavailable at maximum attempts → `ManualReviewRequired`, no reservation.
9. Restart must rediscover due work from PostgreSQL.
10. Duplicate or concurrent dispatch must not progress the Transfer or schedule Reservation twice.

Reuse `TransferProcessState` attempt/scheduling fields where their semantics remain correct. Add persistence only when necessary. Do not introduce a second workflow engine or generic job framework.

## 9. Retry configuration

Add repository-consistent strongly typed options for:

- maximum transient Fraud attempts;
- initial retry delay;
- maximum retry delay or explicit fixed delay if justified;
- claim/lease duration if not safely reusable.

Requirements:

- fail startup for missing/invalid critical values;
- UTC-safe calculations;
- bounded retry;
- deterministic test seam using `TimeProvider`;
- no arbitrary `Thread.Sleep` or `Task.Delay` in tests;
- no production-length waiting in tests;
- no hard-coded production endpoint, secret, credential, or token.

## 10. HTTP and idempotency semantics

Preserve `Idempotency-Key` behavior:

- same key/same payload does not create a second Transfer or screening workflow;
- same key/different payload remains conflict;
- concurrent duplicates create one durable Transfer/process;
- replay returns the established Transfer identity;
- a pending Fraud decision is represented truthfully as accepted/processing, not falsely completed or rejected;
- no retry creates duplicate daily-limit, reservation, or external-payment effects.

If the existing response outcome must change to represent asynchronous Fraud processing, make the smallest compatible change and update README/API examples and tests truthfully. Do not add a new public endpoint without requirement evidence.

## 11. Required tests

### Domain tests

Add explicit named tests proving:

1. Fraud rejection transitions `PendingFraudScreening` to `FraudRejected`.
2. Fraud-rejected Transfer cannot request Balance Reservation.
3. Fraud-rejected Transfer cannot begin external submission.
4. Fraud-rejected Transfer cannot settle or complete.
5. Invalid repeated Fraud decisions are rejected.
6. Timeout/unavailability result does not itself create a definitive rejection transition.

### Application/unit tests

Prove:

- result-to-action mapping;
- bounded-attempt boundary;
- backoff calculation;
- configuration validation;
- cancellation propagation.

### PostgreSQL integration tests

Add deterministic tests proving:

1. Approved Fraud schedules exactly one Reservation action.
2. Rejected Fraud creates no Reservation and cannot progress.
3. Manual-review result escalates with no Reservation.
4. Timeout leaves durable recoverable work.
5. Temporarily unavailable leaves durable recoverable work.
6. Retry uses the same stable screening identity.
7. Retry count and next-attempt timestamp persist correctly.
8. Maximum attempts are bounded and escalate to Manual Review.
9. Restart with a fresh host/service provider rediscovers pending Fraud work.
10. Concurrent duplicate workers/claims produce one transition and one next action.
11. HTTP same-key replay during pending Fraud processing creates no duplicate work.
12. Same-key/different-payload conflict remains unchanged.
13. Authorization/daily-limit/Fraud ordering remains correct.
14. Fraud rejection cannot reach Account Reservation.
15. Correlation survives worker/retry/restart processing.

Use real PostgreSQL. EF Core InMemory and mocked persistence are not proof of durable behavior.

## 12. Regression constraints

Preserve:

- Account financial invariants;
- short transactions;
- module-owned schemas and DbContexts;
- HTTP idempotency;
- genuine concurrency;
- Payment timeout/reconciliation behavior;
- Transactional Outbox;
- at-least-once delivery;
- security `401`/`403`/`404` semantics;
- trusted audit actor;
- health/runtime/CI behavior;
- architecture tests.

Do not claim exactly-once delivery.

## 13. Documentation updates within this task

Update only documentation directly affected by Fraud behavior:

- `README.md` limitations/demo wording if necessary;
- `docs/architecture.md` current-vs-target claims;
- `docs/ubiquitous-language.md` only if implementation semantics need clarification;
- `docs/requirement-to-evidence.md` for Scenario E and Domain Test #5;
- this task’s evidence file if repository conventions require it.

Do not implement TASK-20 or TASK-21 documentation scope early.

## 14. Verification

Run focused tests after every slice, then:

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
```

Required:

- 0 warnings;
- 0 errors;
- 0 failed tests;
- 0 required skipped tests.

Also run:

- focused Fraud domain tests;
- focused Fraud PostgreSQL tests;
- existing submission/idempotency tests;
- existing Process Manager/restart tests;
- Account reservation tests;
- architecture tests;
- complete PostgreSQL integration suite twice in fresh test processes;
- `git diff --check`.

## 15. Self-review

Inspect the diff for:

- timeout treated as approval/rejection;
- Fraud call inside DB transaction;
- retry without durable state;
- unbounded retry;
- duplicate Reservation scheduling;
- unstable screening reference;
- loss of correlation;
- restart depending on memory;
- arbitrary test sleeps;
- hidden production secrets;
- cross-module DbContext access;
- API business rules;
- weakened idempotency/security;
- full Fraud-engine scope creep;
- unrelated TASK-20/TASK-21 work.

Classify each finding as `Blocker`, `Non-blocking improvement`, or `Preference`. Fix every Blocker.

## 16. Completion evidence

Capture:

- baseline main SHA;
- branch;
- requirements-to-code-to-test matrix;
- files changed;
- migrations and rationale, or explicit no-migration rationale;
- outcome contract;
- retry policy;
- stable screening identity;
- focused test results;
- restart/concurrency evidence;
- full test totals by project;
- repeated PostgreSQL results;
- build warning/error totals;
- ADR/module-boundary review;
- self-review findings;
- confirmation that TASK-20/TASK-21 were not implemented.

Only mark this task Done after all requirements pass.

## 16.1 Completion evidence (2026-08-13)

- **Baseline main SHA:** `0a60171` (TASK-18 merged)
- **Branch:** `feature/fraud-screening-resilience`
- **No migration:** reuses `TransferProcessState` attempt/scheduling fields
- **Outcome contract:** `FraudScreeningResult` (`Approved`, `Rejected`, `ManualReviewRequired`, `Timeout`, `TemporarilyUnavailable`); `DecisionOutcome` retained for authorization/daily limit only
- **Stable screening identity:** `TransferId` in `FraudScreeningRequest`
- **Retry policy:** bounded exponential backoff via `FraudScreeningRetryPolicy`; default max 3 transient attempts; escalates to `ManualReviewRequired`
- **Test totals:** Domain 66, Integration 189, Architecture 12 — **267 total**, 0 failures (two PostgreSQL integration runs)
- **Build:** 0 warnings, 0 errors
- **TASK-20/TASK-21:** not implemented

---

## 17. Commit, push and PR

Run:

```bash
git status --short
git diff --check
git diff --stat main...HEAD
git diff main...HEAD
```

Ensure no secrets, generated artifacts, logs, test output, unrelated changes, or future-task work are included.

Suggested commit:

```text
feat(fraud): make screening failures durably recoverable
```

Push:

```bash
git push -u origin feature/fraud-screening-resilience
```

Open a PR against `main`. Do not merge it. Wait for CI on the final remote SHA and report:

- PR URL;
- CI URL/result;
- local SHA;
- remote SHA;
- confirmation SHAs match;
- unresolved blocker, if any.
