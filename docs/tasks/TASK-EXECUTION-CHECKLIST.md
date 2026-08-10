# Task Execution Checklist

Use this checklist with every TASK file.

## Before Starting
- [ ] Previous task is Done.
- [ ] Previous task branch is merged.
- [ ] `main` is pulled and clean.
- [ ] New branch matches the task file.
- [ ] Baseline build is green.

## During Implementation
- [ ] Work stays inside the task scope.
- [ ] No locked ADR is contradicted.
- [ ] No unrelated refactoring is mixed into the task.
- [ ] Tests are written with the behavior, not postponed.
- [ ] No secret is committed.

## Before Marking Done
- [ ] Required Tests pass.
- [ ] Full solution build has 0 warnings / 0 errors.
- [ ] Existing tests have no regressions.
- [ ] Verification Procedure completed.
- [ ] Acceptance Criteria checked.
- [ ] Evidence captured.
- [ ] `git diff` contains only intended scope.
- [ ] PR clearly describes outcome and verification.

## Review Finding Classification
Each finding must be labeled:
- Blocker
- Non-blocking improvement
- Preference

Only a Blocker prevents starting the next task.
