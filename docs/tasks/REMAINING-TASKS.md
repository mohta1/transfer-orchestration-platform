# Remaining Tasks — Execution Plan

> **ARCHIVED / SUPERSEDED — 2026-08-13**
> This file is retained for historical context only. **Do not use it for current status.**
> Authoritative task status and roadmap: [00-ROADMAP-INDEX.md](./00-ROADMAP-INDEX.md) and individual `TASK-*.md` files (TASK-01 through TASK-22).
> Requirement traceability: [requirement-to-evidence.md](../requirement-to-evidence.md).

---

# Remaining Tasks — Execution Plan (historical snapshot)

**Last updated:** 2026-08-12  
**Baseline:** TASK-01 through TASK-14 are **Done**.  
**Next official task:** [TASK-15](./TASK-15-test-hardening.md)

**Note:** TASK-15 through TASK-20 are **Done** on `main` as of SHA `20bc709`. TASK-21 and TASK-22 remain. The tables below reflect the August 12 snapshot and are **not current**.

This file consolidates the remaining roadmap work and any open follow-up items from the TASK-14 / PR #24 security review.

> **Execution rule (unchanged):** Do not start the next task until the current task passes Required Tests, Verification Procedure, Acceptance Criteria, and Definition of Done. One task per branch / one task per pull request.

---

## 1. Official Remaining Roadmap (TASK-15 → TASK-18)

| Task | Title | Branch | Depends On | Status | Task file |
|------|-------|--------|------------|--------|-----------|
| TASK-15 | Integration Test Suite and Architecture Enforcement | `feature/test-hardening` | TASK-14 | Not Started | [TASK-15-test-hardening.md](./TASK-15-test-hardening.md) |
| TASK-16 | Runtime Hardening, Docker Compose, and CI | `feature/runtime-ci` | TASK-15 | Not Started | [TASK-16-runtime-ci.md](./TASK-16-runtime-ci.md) |
| TASK-17 | Engineering Delivery Documentation | `docs/engineering-delivery` | TASK-16 | Not Started | [TASK-17-engineering-delivery.md](./TASK-17-engineering-delivery.md) |
| TASK-18 | Final Review, README, Demo Path, and Submission Gate | `release/final-challenge-review` | TASK-17 | Not Started | [TASK-18-final-challenge-review.md](./TASK-18-final-challenge-review.md) |

When TASK-18 is Done, the repository is submission-ready per [00-ROADMAP-INDEX.md](./00-ROADMAP-INDEX.md).

---

## 2. TASK-15 — Integration Test Suite and Architecture Enforcement

**Objective:** Close all mandatory challenge coverage gaps and mechanically enforce Modular Monolith dependency rules.

### In scope (from task file)
- Test inventory and requirement-to-test matrix.
- ≥10 meaningful domain tests.
- ≥12 meaningful integration tests.
- Genuine PostgreSQL concurrency test.
- Restart recovery tests.
- Duplicate delivery / idempotency tests.
- Architecture dependency tests (including one negative proof).
- Keep all security, reconciliation, and API tests green.

### Out of scope
- New features except **blocker fixes**.
- Load testing beyond documented target scenarios.

### Key required tests
- Successful submission.
- Idempotent replay.
- Same key / different payload conflict.
- Concurrent duplicate submission.
- Concurrent reservations.
- Outbox retained on dispatch failure.
- Outbox retry.
- Duplicate settlement idempotent.
- Duplicate consumer one effect.
- Restart recovery.
- Optimistic concurrency conflict.
- Poison retry bounded.

### Verification
```text
dotnet test TransferOrchestrationPlatform.sln
```
Temporarily introduce one forbidden dependency locally; confirm architecture test fails; revert.

### Evidence to capture
- Final test counts.
- Requirement-to-test matrix.
- Full `dotnet test` summary.
- Negative architecture test proof.

---

## 3. TASK-16 — Runtime Hardening, Docker Compose, and CI

**Objective:** Reproducible clean-checkout build/test and Docker Compose runtime; encode gates in CI.

### In scope
- Dockerfile review.
- Docker Compose (API + PostgreSQL).
- Health / readiness wiring.
- Migration / setup strategy documentation.
- `.env.example`.
- Local `dotnet tool restore`.
- GitHub Actions CI (restore, warning-free build, all tests, PostgreSQL integration tests where practical).

### Out of scope
- Kubernetes, cloud deployment, broker infrastructure, production secret manager.

### Key required tests
- Clean local build from deleted `bin/` / `obj/`.
- `docker compose up --build` starts API and PostgreSQL.
- Readiness becomes healthy.
- Persistent DB volume survives restart.
- CI completes successfully.

### Evidence to capture
- CI run summary / link.
- `docker compose ps`.
- Health / readiness output.
- Clean build / test summary.

---

## 4. TASK-17 — Engineering Delivery Documentation

**Objective:** Complete engineering-delivery documentation and requirement-to-evidence traceability.

### In scope
- Complete `engineering-standards.md`.
- Complete `team-engineering-model.md`.
- Complete `ai-assisted-engineering.md`.
- Complete `technical-debt-prioritisation.md`.
- Requirement-to-evidence matrix.
- Architecture doc updates only for verified implementation facts.
- Cross-file consistency review.

### Out of scope
- Stylistic rewrite of locked architecture.
- Additional ADRs.
- Unverified production SLA claims.
- New features.

### Acceptance highlights
- No required document is empty.
- Exactly five ADRs.
- All eight mandatory diagrams present.
- Requirement traceability is reviewable.

---

## 5. TASK-18 — Final Review, README, Demo Path, and Submission Gate

**Objective:** Strict final challenge audit; reviewer-friendly README and demo path; submission only when every blocker has evidence.

### In scope
- Final README (architecture, setup, Docker, migrations, auth/demo approach, API examples, tests, ADR/diagram locations, **known limitations / non-goals**).
- Prerequisites and local / Docker run paths.
- POST / GET examples.
- Idempotency, timeout/reconciliation demos.
- Test commands.
- Challenge compliance checklist.
- Clean-room validation.
- Git hygiene and final consistency audit.

### Required demo / validation scenarios
- Clean restore.
- Warning-free build.
- All tests.
- Docker Compose startup.
- Liveness / readiness.
- Successful transfer.
- Idempotent duplicate.
- Concurrent reservation.
- Timeout / reconciliation.
- Outbox failure / retry.
- Duplicate consumer.
- **Security rejection scenario** (401 / 403 on financial or manual commands).

### Terminal gate
- `0` build warnings / errors.
- All tests pass.
- ≥10 domain and ≥12 integration tests.
- Genuine concurrency passes.
- Exactly five ADRs and eight mandatory diagrams.
- Docker runtime works.
- README works from clean state.
- No blocker contradiction remains.

---

## 6. Open Follow-Up Items (Optional / Later Tasks)

| ID | Item | Classification | Recommended placement |
|----|------|----------------|----------------------|
| B-03 | **`RequireHttpsMetadata = false`** in JWT bearer setup | Preference / hardening (low) | **TASK-16** runtime docs or **TASK-18** limitations |
| B-04 | **Dev signing key in `appsettings.Development.json`** | Acceptable for challenge | **TASK-18** README auth section |

---

## 7. Suggested Execution Order

```text
TASK-15  →  TASK-16  →  TASK-17  →  TASK-18
```

**Path to submission:** TASK-15 through TASK-18.

---

## 8. Quick Status Snapshot

| Area | Done | Remaining |
|------|------|-----------|
| Domain & persistence (TASK-01–03) | ✓ | — |
| Submission & coordination (TASK-04–08) | ✓ | — |
| Reliability & operations (TASK-09–14) | ✓ | — |
| Read resource authorization (PR #24 follow-up) | ✓ | — |
| Test hardening & architecture enforcement | — | TASK-15 |
| Runtime / Docker / CI | — | TASK-16 |
| Engineering delivery docs | — | TASK-17 |
| Final review & submission | — | TASK-18 |

---

## 9. Handoff Notes from TASK-14 (+ PR #24 follow-up)

- JWT bearer auth with Customer / Operator policies is implemented.
- Manual audit actor is derived from JWT `sub`; `X-Operator-ID` is not trusted.
- Customer submission authorization uses `account_id` claim via `AuthenticatedCustomerAuthorization`.
- **GET /api/transfers/{id}** enforces source-account ownership (`404 transfer_not_found` for cross-customer access).
- Security tests exercise production `AuthenticatedCustomerAuthorization` (no allow-all override).
- Health endpoints remain anonymous.
- Full suite after follow-up: **231/231** tests passing; **SecurityBoundaryTests** 16/16.
