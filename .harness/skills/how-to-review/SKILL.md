# Skill: how-to-review (StoreOps)

## Purpose
The procedure the Evaluator follows so that two reviews of the same change reach the same verdict, and
every FAIL tells the Generator exactly what to fix. Read by: Evaluator.

## Procedure
1. **Collect the change.** `git status --porcelain` and `git diff HEAD --stat`, then `git diff HEAD` per file
   (read untracked files in full). This is the evidence — the generator summary is only a claim.
2. **Run the hard gates** from `grading-criteria`, in order, exactly as written. Copy the decisive output line
   (e.g. `0 Warning(s)`, `Failed: 2`, `VERIFY: FAIL at 'coverage'`) into the Evidence column.
3. **Map every changed file to its layer** (Routes/Service/Repository/Contracts/Record/Subscriber/Module/Test)
   and compare with the summary's Files table (check A6).
4. **Score each checklist item** in `grading-criteria`, one at a time, in ID order.
5. **Write the feedback** in the format of `evaluator.agent.md`; line 1 is the verdict.

## Evidence rules (how variable output becomes a binary result)
- A checklist item is **PASS only with cited evidence**: `path:line` of the code that satisfies it, or the
  name of a passing test that proves it. Get line numbers with `grep -n` / `Select-String` on the file.
- No evidence found → **FAIL `[UNVERIFIED]`**, saying what evidence was missing. Never "probably fine".
- Judge against the **contract's ACs and the skill rules**, not personal taste. Style issues the build
  accepts are not failures.
- A hard gate that fails, then passes on an immediate identical re-run, is recorded `FLAKY` and counts as
  **FAIL** — non-deterministic tests are defects.
- If two rules seem to conflict, the architecture rule (R1–R6) wins; note the conflict for the Monitor.

## StoreOps-specific things to look for in the diff
| Look for | Means | Check |
|---|---|---|
| `using StoreOps.Api.Modules.<Other>;` (not `.Contracts`) | cross-module internal import | A1 |
| a new public method on `IActivityQueries`/`IStaffDirectory`/`IProgrammeQueries` that saves | write through a read contract | A1 |
| `NotificationService`/`ReportService` referenced outside its module, or a service calling another module's service | direct side effect | A2 |
| state change with no `PublishAsync`, or event record declared in `Shared/` | missing / hidden event | A2, B3 |
| `if` on status/role/store inside a `*Routes` handler | business logic in route | A3 |
| `throw new Exception`, `Results.BadRequest(...)`, wrong error type (403 for another store) | error contract | A4 |
| status / ownership checks inside `InMemory*Repository` | rule in repository | A5 |
| test that awaits a call and asserts only `StatusCode` | F3 | C1, C2 |

## Writing a FAIL item
`[A3] src/StoreOps.Api/Modules/Activities/ActivityRoutes.cs:48 — R4 routes must not contain business logic —
move the "DONE cannot be reopened" check into ActivityService.BulkUpdateStatusAsync and throw BusinessRuleError.`
One item per problem; most severe first; never "improve the code" without a location and a rule.
