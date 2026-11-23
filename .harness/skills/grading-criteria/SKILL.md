# Skill: grading-criteria (StoreOps)

## Purpose
The evaluation framework: dimensions, weights, deterministic hard gates, the checklist and the verdict
formula. Given the same check results, this file always produces the same verdict. Read by: Evaluator.

## Dimensions
| Dimension | Weight | Why it matters for StoreOps |
|---|---|---|
| **A. Architecture compliance** | 40% | The client's four failure modes are all architectural; a feature that works but breaks a boundary is rejected by the standards team. |
| **B. Functional correctness** | 35% | Every GIVEN/WHEN/THEN in the sprint contract must actually hold — including the partial-failure and store-scoping rules. |
| **C. Test quality** | 25% | Tests are what keep the next AI change honest; tests that only check status codes (F3) are worthless. |

## Hard gates — any failure ⇒ `VERDICT: FAIL`, whatever the score
Each is a command with a binary result; run them in this order from the repo root.

| ID | Dim | Command | PASS when | Failure mode guarded |
|---|---|---|---|---|
| HG-A1 | A | `dotnet test StoreOps.slnx -c Release --filter "FullyQualifiedName~StoreOps.Api.Tests.Architecture"` | `Failed: 0` | F1, F2, F4, layering |
| HG-A2 | A | `git status --porcelain -- CLAUDE.md .harness/agents .harness/skills eng tests/StoreOps.Api.Tests/Architecture BannedSymbols.txt .editorconfig stylecop.json Directory.Build.props Directory.Packages.props coverage.runsettings .github` | output is empty | weakening the gates |
| HG-B1 | B | `dotnet build StoreOps.slnx -c Release --nologo` | `0 Warning(s)` and `0 Error(s)` | F2 (banned APIs), nullable, StyleCop |
| HG-B2 | B | for each AC id in the contract: `dotnet test StoreOps.slnx -c Release --no-build --filter "FullyQualifiedName~AC<s>_<n>"` | every AC: ≥ 1 test ran and `Failed: 0` | ACs claimed but not proven |
| HG-C1 | C | `pwsh eng/verify.ps1` | last line `VERIFY: PASS` (format, all tests, coverage ≥ 80/70/60/70) | F3, coverage |

## Checklist (LLM-assessed; PASS needs cited evidence — see how-to-review)
| ID | Pts | Check | PASS when |
|---|---|---|---|
| **A1** ★ | 8 | Module boundary | All cross-module use is via `Contracts`; contract methods added are reads with no save/publish (F1) |
| **A2** ★ | 8 | Event bus | Each cross-module side effect is `PublishAsync` of an event in the publisher's `Contracts`; no `NotificationService`/`ReportService` import outside their module (F4) |
| A3 | 7 | Thin routes | Route handlers only bind, call own service, return `TypedResults`; no domain `if`s |
| **A4** ★ | 7 | Error contract | Only AppError subtypes; the semantic type matches (another store → NotFound, role → Forbidden, state → Conflict/BusinessRule, input → Validation with field) (F2) |
| A5 | 5 | Repository purity | Repository changes are data access only |
| A6 | 5 | Layer reporting | Summary's Files table names the correct layer for every changed file |
| B1 | 10 | AC outcomes | Every AC's THEN status/code is produced as specified |
| B2 | 8 | Resulting state | State after each AC matches (updated items changed, rejected items unchanged) |
| B3 | 7 | Events | Exactly the events the contract names are published, with the stated fields, only for real changes |
| B4 | 5 | Wire format | Enum values SCREAMING_SNAKE, error envelope, 200/201/204 + Location as specified |
| B5 | 5 | No scope creep | Existing endpoints and tests unchanged unless the contract says so |
| **C1** ★ | 8 | Business assertions | Every AC test asserts the business outcome (state/events), not only the status code (F3) |
| C2 | 6 | Rejections prove no change | Every rejection test asserts unchanged state and no events |
| C3 | 5 | Service tests | New service logic tested via `ModuleTestContext`, events via `RecordingEventBus` |
| C4 | 3 | Determinism | No real clock/sleep; `ManualTimeProvider` advanced where order matters |
| C5 | 3 | Right level | Rules in service tests; routing/auth/JSON in HTTP tests |
Totals: A = 40, B = 35, C = 25. ★ = critical item (maps to a client failure mode).

## Verdict formula
1. Any hard gate FAIL → **FAIL**.
2. Any ★ critical item FAIL → **FAIL**.
3. Score = sum of points of PASS items (0–100); dimension score = sum within the dimension.
4. Score ≥ 85 **and** every dimension ≥ 60% of its weight (A ≥ 24, B ≥ 21, C ≥ 15) → **PASS**.
5. Else score ≥ 70 **and** every dimension ≥ 50% (A ≥ 20, B ≥ 17.5, C ≥ 12.5) → **CONDITIONAL_PASS**.
6. Otherwise → **FAIL**.
Ambiguity fallback: an item you cannot decide is FAIL `[UNVERIFIED]`; a malformed feedback file is FAIL (orchestrator rule).

## Worked example
Hard gates all PASS. Checklist: everything PASS except A6 (summary called `ActivityAuditEntry.cs` "Service",
it is a Record: −5) and C4 (test relies on two creates having different timestamps without advancing the
clock: −3). No ★ item failed. Score = 92 (A 35, B 35, C 22). 92 ≥ 85, A 35 ≥ 24, B 35 ≥ 21, C 22 ≥ 15
→ `VERDICT: PASS`, with A6 and C4 listed as required fixes for the record.
Same change but C1 failed (a partial-failure test only checks `200 OK`): ★ critical → `VERDICT: FAIL`,
regardless of the 84 points.
