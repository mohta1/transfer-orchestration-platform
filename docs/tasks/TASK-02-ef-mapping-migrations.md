# EF Core Mapping, Database Constraints, and Initial Migrations

**Task ID:** TASK-02
**Stage:** Stage 1 — Domain & Persistence
**Recommended branch:** `feature/ef-mapping-migrations`
**Depends on:** TASK-01
**Status:** Done

---

## 1. Objective

Persist Transfer and Account domain models in module-owned PostgreSQL schemas using EF Core/Npgsql, with database constraints reinforcing critical invariants.

## 2. Why This Task Exists

Domain rules alone are insufficient under concurrency or buggy callers. PostgreSQL must reinforce the most important invariants.

## 3. Scope

### In Scope
- Transfer and Account EF configuration classes.
- Schema-per-module mapping.
- Strongly typed ID value conversions.
- Account/BalanceReservation relationship mapping.
- Account Version as concurrency token.
- Check constraints for non-negative balances.
- Unique reservation constraint by TransferId.
- Explicit decimal precision and currency length.
- Module-specific migration history.
- First meaningful migrations.

### Out of Scope
- Repositories.
- HTTP idempotency.
- Process Manager.
- Outbox.
- External integrations.

## 4. Required Deliverables

- Transfer and Account EF configuration classes.
- Schema-per-module mapping.
- Strongly typed ID value conversions.
- Account/BalanceReservation relationship mapping.
- Account Version as concurrency token.
- Check constraints for non-negative balances.
- Unique reservation constraint by TransferId.
- Explicit decimal precision and currency length.
- Module-specific migration history.
- First meaningful migrations.

## 5. Implementation Requirements

- No EF attributes in Domain.
- Account.Version is a concurrency token.
- Database enforces available_balance >= 0 and reserved_balance >= 0.
- Reservation TransferId is unique.
- Tables live only in the owning module schema.
- Each module has its own EF migration history.

## 6. Required Tests

- Clean database is created from migrations.
- Transfer persists and reloads.
- Account with reservations persists and reloads.
- Duplicate reservation constraint fails.
- Negative balance cannot be committed.
- Concurrency token is present in persistence metadata/update semantics.

## 7. Verification Procedure

1. dotnet build TransferOrchestrationPlatform.sln
2. dotnet tool run dotnet-ef migrations list --project src/Modules/TransferManagement --startup-project src/TransferOrchestration.Api --context TransferManagementDbContext
3. dotnet tool run dotnet-ef migrations list --project src/Modules/AccountBalance --startup-project src/TransferOrchestration.Api --context AccountBalanceDbContext
4. docker compose up -d postgres
5. Apply both migrations to a clean database and inspect schemas/tables/constraints with psql.

## 8. Acceptance Criteria

- [ ] Clean DB creation succeeds using migrations only.
- [ ] Both module schemas exist.
- [ ] No cross-module table ownership.
- [ ] Required unique/check constraints exist.
- [ ] Build/test baseline remains green.

## 9. Definition of Done

This task is **DONE only when all of the following are true**:

- [ ] Every Acceptance Criterion above is checked.
- [ ] Every Required Test exists and passes.
- [ ] `dotnet build TransferOrchestrationPlatform.sln` finishes with **0 warnings and 0 errors**.
- [ ] Existing tests have no regressions.
- [ ] Work remains inside this task's Scope.
- [ ] No locked ADR is contradicted.
- [ ] No secret or local-only artifact is committed.
- [ ] The requested Evidence is captured before merge.
- [ ] The task branch is reviewable independently.
- [ ] Any review finding is classified as Blocker, Non-blocking improvement, or Preference.

## 10. Evidence to Capture Before Moving On

- Migration lists.
- Schema/table listings.
- Constraint inspection output.
- Full build result.

## 11. Handoff to the Next Task

TASK-03 introduces repositories and proves optimistic concurrency conflicts are handled safely.
