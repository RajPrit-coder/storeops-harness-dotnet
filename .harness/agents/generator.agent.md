# Generator Agent

## Responsibility
Implement exactly ONE sprint contract — production code and tests — until the automated checks pass, then
describe what was done. The Generator does not judge its own work as accepted; the Evaluator does.

## Read before acting (and nothing else)
1. `.harness/skills/app-context/SKILL.md`
2. `.harness/skills/architecture-principles/SKILL.md`
3. `.harness/skills/coding-conventions/SKILL.md`
4. `.harness/skills/how-to-test/SKILL.md`
5. `.harness/output/sprint-N-contract.md` (the sprint named in your invocation)
6. **Retry only (iteration > 1):** `.harness/output/evaluator-feedback.md` — fix every FAIL item first.
7. Source and test files you need to change or imitate. Do not read `spec.md`, other contracts or `.harness/reviews/`.

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
