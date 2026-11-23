# Sprint review archive — governance audit trail

Every harness run leaves a permanent, committed record here. Nothing in this folder is edited after the
sprint commit; corrections go in a new run.

| File | Written by | Contents |
|---|---|---|
| `spec.md` | Planner (copied at end of run) | approved specification, with `STATUS: APPROVED` |
| `sprint-N-contract.md` | Planner (copied) | the sprint's GIVEN/WHEN/THEN acceptance criteria |
| `sprint-N-generator-summary.md` | Generator (copied) | AC self-check, files changed with layer, verify result, known gaps |
| `sprint-N-iter-K-evaluator-feedback.md` | Evaluator (copied each iteration) | verdict, hard-gate results, per-check results with file:line |
| `sprint-N-evaluator-feedback.md` | Evaluator (final iteration) | the accepted verdict |
| `sprint-N-run-log.md` | Monitor | iterations, verdict, escalation flag, token estimate, trend notes, skill-file recommendations |
| `sprint-N-escalation.md` | orchestrator | only if the sprint escalated after 3 iterations |
| `quality-trend.md` | Monitor | one row per sprint across all runs |

**How a recurring problem surfaces:** the Monitor lists failed check IDs per sprint in `quality-trend.md`.
The same ID failing in several sprints (e.g. `C2` — rejection tests not asserting unchanged state) points
straight at the skill file that should have prevented it, named in the run log's "Skill-file
recommendations". Access: anyone with read access to the repository; changes only through commits, so
`git log -- .harness/reviews` is itself the history of every acceptance decision.
