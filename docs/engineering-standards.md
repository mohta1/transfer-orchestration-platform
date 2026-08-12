# Engineering Standards

**Status:** Active baseline for the Transfer Orchestration Platform challenge repository
**Applies to:** All contributors, reviewers, and AI-assisted change authors working in this repository

This document describes standards **actually enforced or deliberately required** by this repository. It references authoritative ADRs and domain documents rather than duplicating them in full.

Quality attributes in [architecture.md](./architecture.md) are **engineering targets**, not claims of measured production SLAs. This repository does not claim regulatory certification, full production readiness, or exactly-once delivery.

---

## 1. Purpose and Applicability

These standards keep the Modular Monolith financially correct, recoverable, testable, and migration-safe while the platform coexists with Legacy systems.

| Category | Meaning |
| -------- | ------- |
| **Enforced rule** | Violation is a review **Blocker** or causes CI/build failure |
| **Repository convention** | Expected practice; deviations need explicit justification |
| **Challenge-scope decision** | Locked for this challenge unless a genuine blocker is discovered |
| **Future consideration** | Documented direction, not current implementation |

Authoritative sources, in order of precedence for this repository:

1. [AGENTS.md](../AGENTS.md)
2. Locked ADRs under [docs/adr/](./adr/)
3. [docs/ubiquitous-language.md](./ubiquitous-language.md)
4. Verified implementation and tests

---

## 2. Architecture and Module Structure

### 2.1 Locked system shape (enforced)

- **Incremental Hybrid** at system level; **Modular Monolith** for the new Transfer capability ([ADR-001](./adr/ADR-001-architecture-style.md)).
- **One PostgreSQL database** with **module-owned schemas** and **module-specific DbContexts**.
- **No global `AppDbContext`**.
- **No unnecessary microservices** or service-per-noun decomposition.
- **API remains the composition root** (`src/TransferOrchestration.Api/`).

Current modules:

| Module | Responsibility | DbContext / schema |
| ------ | -------------- | ------------------ |
| TransferManagement | Transfer lifecycle, Process Manager, Outbox, Reconciliation records, HTTP idempotency | `TransferManagementDbContext` → `transfer_management` |
| AccountBalance | Account, Balance Reservation, financial concurrency | `AccountBalanceDbContext` → `account_balance` |
| PaymentNetwork | Payment Network Anti-Corruption Layer | *(no DbContext)* |
| Notification | Idempotent Integration Event consumer | `NotificationDbContext` → `notification` |
| AuditOperations | Correlation, manual-operation audit | `AuditOperationsDbContext` → `audit_operations` |
| BuildingBlocks | Shared primitives only | *(none)* |

`src/Modules/Reconciliation/` is a **placeholder project** for future extraction; durable Reconciliation behaviour currently lives in TransferManagement ([technical-debt-prioritisation.md](./technical-debt-prioritisation.md)).

### 2.2 Module ownership and encapsulation (enforced)

Forbidden:

- One module accessing another module's **DbContext** or private tables.
- TransferManagement depending on AccountBalance Infrastructure.
- Notification querying TransferManagement persistence directly.
- Domain depending on Infrastructure.
- Business rules inside API endpoint handlers.

Allowed:

- Deliberate **Contracts** projects/folders and module registration surfaces.
- Synchronous calls through explicit application contracts (e.g. `IAccountBalanceReservations`).

Architecture tests in `tests/TransferOrchestration.ArchitectureTests/` mechanically enforce these boundaries.

### 2.3 Dependency direction (enforced)

```
API → Application → Domain
Infrastructure → Application, Domain (implements abstractions)
Contracts → minimal shared types only
```

Domain projects must not reference EF Core, Npgsql, ASP.NET Core, or other module Infrastructure assemblies.

---

## 3. Layer Responsibilities

| Layer | Responsibility |
| ----- | ---------------- |
| **Domain** | Aggregates, value objects, invariants, domain events; no persistence or transport |
| **Application** | Use cases, Process Manager steps, orchestration, transaction boundaries at use-case level |
| **Infrastructure** | EF Core mappings, repositories, Outbox store, background workers, external adapters |
| **Contracts** | Stable cross-module interfaces and DTOs |
| **Api** (module) | Module-specific endpoint mapping where owned by the module |
| **Host API** | Composition root: DI registration, security, health, middleware pipeline |

---

## 4. Domain Modeling

### 4.1 Ubiquitous Language (enforced)

Use terms from [docs/ubiquitous-language.md](./ubiquitous-language.md). Notable rules:

- `Accepted` ≠ `Settled`; `Timeout` ≠ `Rejected`.
- `SubmissionStatusUnknown` is for **ambiguous** external outcomes only.
- **Release** returns held funds to Available Balance; **Consume** removes settled funds from Reserved Balance.
- Delivery is **at-least-once**, never exactly-once end-to-end.

### 4.2 Aggregate boundaries (challenge-scope)

| Aggregate Root | Owns |
| -------------- | ---- |
| **Transfer** | Legal lifecycle transitions, external-submission eligibility, fraud/reservation gating |
| **Account** | Available/Reserved Balance, Reservation Reserve/Release/Consume, financial concurrency |

**BalanceReservation** is owned by Account and is **not** an Aggregate Root. Reservation history is not loaded as an unbounded Account collection.

### 4.3 Account as financial concurrency boundary (enforced)

All competing Reserve/Release/Consume operations for an Account must respect optimistic concurrency and database constraints. See [ADR-003](./adr/ADR-003-reservation-concurrency.md).

Implementation: `src/Modules/AccountBalance/Domain/Accounts/Account.cs`.

---

## 5. Persistence and Transactions

### 5.1 EF Core migrations (repository convention)

- Each module owns migrations under its Infrastructure project.
- Migrations are applied deterministically before API startup in Docker Compose ([runtime-setup.md](./runtime-setup.md)).
- Design-time factories exist for all four DbContexts.

### 5.2 Database constraints (enforced)

Final protection includes, at minimum:

- `CHECK (available_balance >= 0)` and `CHECK (reserved_balance >= 0)` on Account.
- Unique reservation per `TransferId`.
- Unique idempotency scope/key.
- Concurrency tokens on Aggregate rows.
- Composite primary key on Processed Messages `(message_id, consumer_name)`.

Evidence: migrations under each module's `Infrastructure/Persistence/Migrations/` and `PersistenceMappingTests`.

### 5.3 Short transaction boundaries (enforced)

- Persist committed business state and related Outbox messages in **one local transaction**.
- Do not hold database transactions open across external I/O where avoidable.
- External submission persists immutable `NetworkSubmissionReference` **before** calling the Payment Network.

### 5.4 Optimistic concurrency (enforced)

Repository writers catch `DbUpdateConcurrencyException` and surface explicit conflict types (e.g. `AccountConcurrencyConflictException`). Bounded reload-and-retry is permitted; unbounded retry is forbidden.

---

## 6. HTTP Idempotency

`POST /api/transfers` requires header **`Idempotency-Key`** (max 200 characters).

| Scenario | Expected behaviour |
| -------- | ------------------- |
| Same scope/key + same payload fingerprint + completed | Replay stored logical result |
| Same scope/key + same fingerprint + in-progress | Consistent in-progress response; no second Transfer |
| Same scope/key + different fingerprint | `409 Conflict`; existing Transfer unchanged |
| Concurrent identical requests | Database uniqueness yields one logical owner |

Implementation: `TransferSubmissionIdempotencyStore`, `TransferSubmissionFingerprint`, `TransferSubmissionService`.

Tests: `TransferSubmissionApiTests`, `PersistenceMappingTests`, `TransferSubmissionFingerprintTests`.

---

## 7. Process Coordination

**Persistent Process Manager** inside TransferManagement ([ADR-002](./adr/ADR-002-process-coordination.md)):

- Durable `TransferProcessState` with claim/lease semantics.
- Background workers rediscover due work after restart.
- Transfer Aggregate owns the business state machine; Process Manager owns coordination metadata and scheduling.

Payment timeout path:

`PendingExternalSubmission → SubmissionStatusUnknown → Reconciliation`

**No blind resubmission** after ambiguous timeout.

---

## 8. Reliable Messaging

**Transactional Outbox** ([ADR-004](./adr/ADR-004-reliable-messaging.md)):

- Business-state change and Outbox Message commit **atomically**.
- `OutboxWorker` claims, dispatches, retries with bounded backoff, and dead-letters poison messages.
- Delivery semantics: **at-least-once** — duplicates are expected.

**Idempotent consumers**:

- Durable `ProcessedMessage` records with unique `(MessageId, ConsumerName)`.
- Duplicate delivery must produce **one** downstream durable effect.

No fire-and-forget background processing for financially relevant work.

---

## 9. Reconciliation and Manual Operations

- Payment **timeout is not rejection**; ambiguous outcomes enter `SubmissionStatusUnknown`.
- Reconciliation uses status enquiry with the stable `NetworkSubmissionReference`; it does not blindly re-submit.
- Escalation to `ManualReviewRequired` is configurable and tested.
- Manual commands require operator authorisation, mandatory reason, and immutable audit records with trusted actor identity.

Implementation: `ReconciliationProcessStep`, `TransferManualOperationsService`, `ManualOperationsEndpoints`, `OperationsAuditWriter`.

---

## 10. Security and Observability

### 10.1 Authentication and authorization (enforced)

- Unauthenticated → `401`.
- Authenticated but unauthorised → `403`.
- Cross-customer resource access → **`404 Not Found`** (conceal existence).
- Manual operations → operator role only.
- Health endpoints remain anonymous.

JWT configuration: `Authentication__Jwt__*` (see [runtime-setup.md](./runtime-setup.md)). Never commit real signing keys.

### 10.2 Trusted actor identity (enforced)

Audit records use authenticated claims, not client-supplied operator headers.

### 10.3 Correlation and causation (repository convention)

- Middleware propagates `X-Correlation-ID` and `X-Causation-ID`.
- Structured logs scope `TransferId`, safe account/idempotency representations.

### 10.4 Logging restrictions (enforced)

Do not log raw JWTs, credentials, unnecessary full account numbers, or sensitive idempotency key material.

### 10.5 Error responses (enforced)

Use RFC 7807 Problem Details via shared helpers (`ApiProblemResults`). Internal exceptions must not leak implementation details to clients.

### 10.6 Health (enforced)

- **Liveness** (`/health/live`): process alive; does not require database.
- **Readiness** (`/health/ready`): PostgreSQL reachable.

---

## 11. Testing Standards

### 11.1 Test categories

| Category | Project | PostgreSQL required |
| -------- | ------- | ------------------- |
| Domain | `TransferOrchestration.Domain.Tests` | No |
| Integration | `TransferOrchestration.IntegrationTests` | **Yes** |
| Architecture | `TransferOrchestration.ArchitectureTests` | No |

### 11.2 Real PostgreSQL requirements (enforced)

Financial concurrency, persistence, migrations, Outbox, restart recovery, and consumer deduplication **must not** use EF Core InMemory as proof of correctness.

Set `TEST_DATABASE_CONNECTION_STRING` for integration tests locally and in CI.

### 11.3 Deterministic-test rules (repository convention)

- Avoid `Thread.Sleep` for timing assertions where possible; use controllable clocks or eligibility queries.
- Destructive integration tests require explicit connection string (fail fast if missing).
- Tests assert persisted final state and constraints, not HTTP status alone.

### 11.4 Minimum challenge coverage (verified baseline)

As of TASK-15 evidence: **51** domain, **177** integration, **12** architecture tests (**240** total). See [requirement-to-evidence.md](./requirement-to-evidence.md).

### 11.5 Architecture testing (enforced)

Forbidden dependencies must fail architecture tests. Negative proof: temporarily introduce a forbidden reference locally, confirm failure, revert.

---

## 12. Build, CI, and Runtime Quality Gates

Every change must preserve:

```bash
dotnet tool restore
dotnet restore TransferOrchestrationPlatform.sln
dotnet build TransferOrchestrationPlatform.sln --no-restore
dotnet test TransferOrchestrationPlatform.sln --no-build
```

Required: **0 build warnings**, **0 build errors**, all applicable tests passing.

CI (`.github/workflows/ci.yml`):

- `build-and-test` job with PostgreSQL service.
- `runtime-verification` job with Docker Compose validation.

Local runtime: [runtime-setup.md](./runtime-setup.md).

---

## 13. Pull Request and Review Expectations

### 13.1 Scope (enforced)

- **One TASK per branch and per pull request**.
- Never work directly on `main`.
- Do not implement future TASK scope early.

### 13.2 Review classification (enforced)

Every finding is exactly one of: **Blocker**, **Non-blocking improvement**, **Preference**. See [AGENTS.md](../AGENTS.md) for financial, messaging, architecture, and test-integrity blockers.

### 13.3 Required reviewers by change type (repository convention)

| Change type | Required reviewer focus |
| ----------- | ----------------------- |
| Financial / reservation / Account | Module owner + concurrency evidence |
| Payment Network / external submission | ACL owner + timeout/reconciliation semantics |
| Outbox / consumers | Reliability owner + idempotency evidence |
| Security / auth | Security-aware reviewer |
| Cross-module contracts | Owners of **both** modules |
| Migrations | Owning module + backward-compatibility check |
| Architecture / ADR | Tech lead or rotating architecture owner |

Details: [team-engineering-model.md](./team-engineering-model.md).

---

## 14. Definition of Done

A TASK is **Done** only when:

1. All Required Tests pass.
2. Verification Procedure completed.
3. All Acceptance Criteria satisfied.
4. Full build: 0 warnings, 0 errors.
5. Evidence captured in the TASK file.
6. `git diff` contains only intended scope.
7. No secrets or local-only artifacts committed.
8. Review findings classified; no unresolved Blockers.

For documentation TASKs, additionally: no empty required documents, relative links resolve, matrix/traceability updated where required.

---

## 15. Controlled Exceptions

Exceptions to these standards require:

1. Documented rationale (ADR addendum is **not** permitted — only the five locked ADRs exist).
2. Explicit reviewer approval for the exception scope.
3. Compensating tests or guards where the exception affects invariants.
4. Entry in the technical-debt register if the exception is temporary.

Examples that are **not** valid exceptions without architecture review:

- Cross-module DbContext access "for convenience".
- TreatWarningsAsErrors disabled to green-build.
- InMemory provider for concurrency proof.
- Claiming exactly-once delivery.

---

## 16. Related Documents

- [architecture.md](./architecture.md) — architecture overview and quality-attribute targets
- [team-engineering-model.md](./team-engineering-model.md) — ownership and collaboration
- [ai-assisted-engineering.md](./ai-assisted-engineering.md) — AI usage policy
- [technical-debt-prioritisation.md](./technical-debt-prioritisation.md) — debt scoring and register
- [requirement-to-evidence.md](./requirement-to-evidence.md) — challenge traceability matrix
- [modernisation-roadmap.md](./modernisation-roadmap.md) — Legacy migration phases
- [runtime-setup.md](./runtime-setup.md) — Docker, migrations, local gates
