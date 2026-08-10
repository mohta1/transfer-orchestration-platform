\# Transfer Orchestration Platform — Agent Instructions



\## Mission



Implement this backend engineering challenge incrementally using the task files under `docs/tasks/`.



Exactly one TASK may be implemented per pull request.



Do not start a later TASK until the current TASK has been merged into `main`.



\## Task Source of Truth



For every implementation run:



1\. Read `AGENTS.md`.

2\. Read the current TASK file completely.

3\. Read the relevant architecture documents and ADRs.

4\. Inspect the existing implementation before making changes.



The current TASK defines:



\- scope

\- out of scope

\- implementation requirements

\- required tests

\- verification procedure

\- acceptance criteria

\- definition of done



Do not silently weaken or reinterpret these requirements.



\## Locked Architecture



The following decisions are locked unless an explicit blocker is discovered:



\- Incremental Hybrid architecture at system level.

\- Modular Monolith for the new capability.

\- No unnecessary microservices.

\- One PostgreSQL database.

\- Module-owned schemas.

\- Module-specific DbContexts.

\- TransferManagement owns Transfer workflow state.

\- Account is the financial concurrency boundary.

\- BalanceReservation is owned by Account and is not an Aggregate Root.

\- Optimistic concurrency with database constraints and short local transactions.

\- Persistent Process Manager for workflow coordination.

\- Payment timeout is not rejection.

\- Never blindly resubmit an ambiguous payment.

\- Transactional Outbox is mandatory.

\- Delivery semantics are at-least-once.

\- Consumers must be idempotent.

\- Do not claim exactly-once delivery.

\- No fire-and-forget background processing.

\- Manual operations must be auditable.



\## Module Boundaries



Forbidden:



\- TransferManagement accessing AccountBalanceDbContext directly.

\- One module accessing another module's private database tables.

\- Domain depending on Infrastructure.

\- Notification querying TransferManagement persistence directly.

\- A global AppDbContext.

\- Generic shared repositories.

\- Business rules inside API endpoints.

\- Service-per-noun decomposition.



Keep module internals internal by default.



Expose only deliberate module Contracts and module registration surfaces.



\## Scope Control



Implement only the current TASK.



Do not implement future TASKs early.



Allowed:



\- minimal prerequisites strictly necessary to complete the current TASK.



Forbidden:



\- speculative infrastructure,

\- future-task functionality,

\- unrelated refactoring,

\- optional abstractions unrelated to current acceptance criteria.



\## Build Quality Gate



Every implementation must preserve:



```bash

dotnet restore TransferOrchestrationPlatform.sln

dotnet build TransferOrchestrationPlatform.sln --no-restore

dotnet test TransferOrchestrationPlatform.sln --no-build

```



Required result:



\- 0 build warnings

\- 0 build errors

\- all applicable tests passing



Do not disable or weaken analyzers, TreatWarningsAsErrors, tests, or database constraints merely to obtain a green build.



\## Test Rules



Tests must be implemented in the same TASK as the behavior they verify.



Do not postpone a required test unless the TASK explicitly says so.



Financial concurrency tests must use real PostgreSQL.



Persistence, migration, concurrency, Outbox, restart recovery, and consumer-deduplication behavior must not use EF Core InMemory as proof of correctness.



Never delete or weaken a valid existing test merely to make CI pass.



\## Task Completion



Only change:



\*\*Status:\*\* Not Started



to:



\*\*Status:\*\* Done



for the current TASK after:



\- all Required Tests pass,

\- its Verification Procedure succeeds,

\- all Acceptance Criteria are satisfied,

\- the full build has 0 warnings and 0 errors.



Never modify the status of another TASK.



\## Review Finding Classification



Every review finding must be classified as exactly one of:



\- Blocker

\- Non-blocking improvement

\- Preference



A pull request may fail review only when at least one Blocker exists.



Do not fail a TASK solely because of style or design preference.



\## Git Rules



\- One TASK per branch.

\- One TASK per pull request.

\- Never work directly on main.

\- Never merge directly into main.

\- Do not force-push main.

\- Do not modify unrelated TASK files.

\- Do not commit secrets.

\- Do not commit `.env`.

\- Do not commit binaries, test output, local database files, or temporary files.



\## Code Review Rules



\### Financial Correctness



Flag as Blocker if a change can:



\- make AvailableBalance negative,

\- reserve funds more than once for the same Transfer,

\- consume a reservation more than once financially,

\- release a reservation more than once financially,

\- bypass optimistic concurrency,

\- lose a committed financial result.



\### External Payment Safety



Flag as Blocker if:



\- timeout is treated as rejection,

\- ambiguous payment submission is blindly retried,

\- an Internal Bank Transfer is sent to the external payment network,

\- external submission can occur before fraud screening and balance reservation.



\### Reliable Messaging



Flag as Blocker if:



\- required business state and Outbox event are not atomic,

\- a committed result can lose its integration event,

\- code claims exactly-once delivery,

\- a consumer with possible duplicate delivery is not idempotent.



\### Module Architecture



Flag as Blocker if:



\- one module accesses another module's DbContext,

\- one module directly modifies another module's tables,

\- Domain depends on Infrastructure,

\- unnecessary distributed services are introduced.



\### Test Integrity



Flag as Blocker if:



\- current TASK Required Tests are missing,

\- valid tests are removed or weakened without justification,

\- required concurrency is tested only using EF Core InMemory.



Formatting and naming preferences are not Blockers unless they cause deterministic build or analyzer failures.
