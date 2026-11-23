# Skill files — index and token budget

Each skill is `.harness/skills/<name>/SKILL.md`: a purpose statement, then StoreOps-specific rules and examples.

| Skill | Read by | Encodes | ~Tokens |
|---|---|---|---|
| `app-context` | Planner, Generator, Monitor | modules, §3.3 types + wire values, the 9 endpoints, seed ids/tokens, commands | 1,100 |
| `architecture-principles` | all four | §3.5 rules R1–R6 with the failure mode each prevents and the test that enforces it | 1,250 |
| `sprint-decomposition` | Planner | sprint sizing at architectural seams, GIVEN/WHEN/THEN rules, banned vague words | 750 |
| `coding-conventions` | Generator | placement table, role naming, StoreOps patterns, which AppError to throw, StyleCop traps | 1,000 |
| `how-to-test` | Generator | test placement, AC-id naming, state + event assertions, determinism, coverage thresholds | 1,000 |
| `how-to-review` | Evaluator | review procedure, evidence rules, StoreOps diff smells → check IDs | 750 |
| `grading-criteria` | Evaluator | 3 dimensions (40/35/25), 5 automated hard gates, 16-item checklist, verdict formula | 1,300 |

## Per-invocation budget (skills + agent file, before source code)
| Agent | Skills read | ~Tokens |
|---|---|---|
| Planner | app-context, architecture-principles, sprint-decomposition | ~3,800 |
| Generator | app-context, architecture-principles, coding-conventions, how-to-test | ~5,000 |
| Evaluator | architecture-principles, how-to-review, grading-criteria | ~3,900 |
| Monitor | app-context | ~1,700 |

## Depth vs. token budget
- The two **shared** files are read by up to four agents per sprint and once more per retry, so they stay
  short: rules and pointers, no tutorials.
- **Depth lives in the role files** (coding-conventions, how-to-test, grading-criteria) that only one agent reads.
- Nothing is duplicated between files; a file points to another instead of repeating it.
- Rough cost of one sprint with one retry: Planner 1× + (Generator + Evaluator) × 2 + Monitor 1× ≈ 23k
  tokens of instructions, before source code and tool output.
