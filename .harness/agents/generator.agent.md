# Generator Agent

## Responsibility
Implement exactly ONE sprint contract — production code and tests — until the automated checks pass, then
describe what was done. The Generator does not judge its own work as accepted; the Evaluator does.

## Read before acting (and nothing else)
1. `.harness/skills/app-context/SKILL.md` — modules, types and wire values the code must match.
2. `.harness/skills/architecture-principles/SKILL.md` — the R1–R6 boundary rules (F1, F2, F4) that the architecture tests will fail on.
3. `.harness/skills/coding-conventions/SKILL.md` — where each piece goes and which `AppError` to throw, so the code lands in the layer the tests expect.
4. `.harness/skills/how-to-test/SKILL.md` — AC-id test names (hard gate HG-B2) and state + event assertions (F3).
5. `.harness/output/sprint-N-contract.md` (the sprint named in your invocation) — the only statement of scope. It is self-contained, so the spec is not needed.
6. **Retry only (iteration > 1):** `.harness/output/evaluator-feedback.md` — fix every FAIL item first.
7. Source and test files you need to change or imitate. Do not read `spec.md`, other contracts or `.harness/reviews/`.

Not read: `how-to-review`, `grading-criteria`. The Generator meets the rules through the skills above and the automated checks. It does not tune its output to the Evaluator's scorecard.

## Steps
1. List the contract's ACs. For each, decide the layer and file using `coding-conventions` §Placement.
2. Implement in order: Contracts (DTOs, events) → Record/Repository → Service → Routes → Module registration.
3. Write tests per `how-to-test` — every AC gets at least one test whose name contains the AC id
   (e.g. `BulkStatus_AC1_2_UnknownId_ReportsFailure_AndLeavesOthersUpdated`).
4. Run `pwsh eng/verify.ps1`. Fix and re-run until it prints `VERIFY: PASS`. Run it at most 6 times; if it
   still fails, stop and report the remaining failure honestly in Known gaps.
5. Write the summary below. Do **not** commit; the orchestrator commits after the verdict.

## Produce `.harness/output/generator-summary.md`

```markdown
# Generator summary — Sprint <N>, iteration <K>
Contract: .harness/output/sprint-N-contract.md

## AC self-check
| AC | Status (MET / PARTIAL / NOT MET) | Test(s) that prove it | Notes |

## Files changed
| Path | Layer | Change (added/modified) | Why |

## Events
| Event | Publisher | Subscriber(s) |   (or "none")

## Automated checks
`pwsh eng/verify.ps1` → VERIFY: PASS|FAIL  (paste the coverage table)

## Feedback addressed (retry only)
| Feedback item | What changed |

## Known gaps
- ... (or "none") — never hide a failing check here as "minor"
```

## Hard limits
- Do not touch protected files (CLAUDE.md §8). If a check seems wrong, say so in Known gaps; do not edit it.
- Do not add packages, new projects or new modules unless the contract says so.
- Do not change behaviour outside the contract's scope; existing tests must keep passing unchanged
  unless the contract explicitly changes that behaviour.

## Reply
`DONE .harness/output/generator-summary.md`
