# Run log — Sprint 2: Per-item failure handling — store scope, permissions, reopen rule
| Field | Value |
|---|---|
| Sprint ID | sprint-2 |
| Feature | Shift handover bulk update (`PATCH /api/activities/bulk-status`): per-item failure handling for missing / other-store (NOT_FOUND), associate permission (FORBIDDEN) and DONE→BLOCKED reopen rule (BUSINESS_RULE_VIOLATION); BR-7, BR-8, BR-9, BR-10 |
| Date | 2026-11-23 |
| Final verdict | PASS |
| Iterations used | 1 of 3 |
| Escalation flag | NO |
| Final score | 100/100 (A 40 · B 35 · C 25) |
| Estimated token cost | ~91.3k tokens (method below) |

### Token-cost estimate (per agent)
`tokens ≈ (chars read + chars written) ÷ 4 × 1.5`

| Agent invocation | Chars read | Chars written | Estimate |
|---|---|---|---|
| Planner | — | — | not invoked this sprint (run-level Planner cost recorded with sprint 1) |
| Generator, iter 1 | 59,616 — agent file 2,510 + 4 skills (app-context 4,530, architecture-principles 4,950, coding-conventions 4,094, how-to-test 4,050) + contract 7,750 + the 3 files it changed, pre-change size (11,350 + 11,921 + 8,461) | 31,029 — added diff lines 25,795 + generator-summary 5,234 | ~34.0k |
| Evaluator, iter 1 | 115,690 — agent file 2,295 + 3 skills (architecture-principles 4,950, how-to-review 3,060, grading-criteria 5,177) + contract 7,750 + summary 5,234 + `git diff HEAD` 30,823 + the 3 changed files, post-change size (13,315 + 23,028 + 20,058) | 6,686 — evaluator-feedback | ~45.9k |
| Monitor | 26,662 — agent file 2,258 + app-context 4,530 + contract 7,750 + summary 5,234 + iter-1 feedback 6,686 + quality-trend 204 | ~3,820 — this run log 3,760 + one trend row 57 | ~11.4k |
| **Total** | | | **~91.3k** |

Real `/cost` figure: not supplied by the developer.

## Iteration history
| Iteration | Verdict | Score | Failed checks |
|---|---|---|---|
| 1 | PASS | 100/100 (A 40 · B 35 · C 25) | none |

## Quality trend notes
- Second consecutive sprint passed first time at 100/100 (sprint 1: PASS, 1 iteration, 100). No regression.
- All hard gates (HG-A1, HG-A2, HG-B1, HG-B2, HG-C1) passed; `pwsh eng/verify.ps1` → `VERIFY: PASS`, 207 tests, coverage Service 100% / Route 100% / Shared 95.7% / Overall 99.2%.
- The four target failure modes did not appear: no cross-module repository import (F1, A1), only `AppError`
  subtypes with a narrowed `catch (AppError)` and no broad `catch (Exception)` (F2, A4), every AC test checks
  per-item outcome, stored state and exact events (F3, C1/C2), and the Alerts side effect still goes only
  through `ActivityStatusChanged` (F4, A2).
- No check ID failed in this sprint or more than once across the run so far.
- The sprint went wider than the contract required in two places: an extra test showing FORBIDDEN takes
  precedence over BUSINESS_RULE_VIOLATION, and the save/publish moved outside the `try` so a failure after a
  save cannot be reported as a per-item failure.
- Evaluator observation (did not fail a check): the HTTP test for AC-2.4 checks there is no act-A ESCALATION
  via `/api/alerts`, but has no positive control in the same test. The service test supplies the positive
  control (`ActivityBulkStatusServiceTests.cs:288-291`).

## Skill-file recommendations
- none required (no check failed).
- Optional, low priority: `.harness/skills/how-to-test/SKILL.md` — when an HTTP test checks that a side effect
  is absent (e.g. no notification), add a positive control in the same test (a sibling item that does produce
  it) so the absence cannot pass because nothing ran. Reason: Evaluator's iteration-1 observation on AC-2.4
  (C2 passed, but the HTTP-level assertion depends on the service test for its control).

## Carried debt (CONDITIONAL_PASS only)
- n/a — verdict PASS.
