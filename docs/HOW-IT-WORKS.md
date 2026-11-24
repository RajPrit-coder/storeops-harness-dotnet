# How this repo works — quick reference

A short note to remind me what this repo is and how the harness runs.

## The idea in one line
An **API** (`src/`) plus a **harness**: markdown files that make Claude Code plan, build, check and record
each new feature in the same way every time.

## The three kinds of md files
| File type | What it is | Analogy |
|---|---|---|
| `CLAUDE.md` | the orchestrator: who runs, in what order, what happens on PASS/FAIL | the project manager's playbook |
| `.harness/agents/*.agent.md` | one role each: Planner, Generator, Evaluator, Monitor | job descriptions |
| `.harness/skills/*/SKILL.md` | shared knowledge: domain rules, architecture, how to test, how to grade | the team handbook |

Agents say **what to do**. Skills say **what to know**. `CLAUDE.md` says **who goes next**.

## Sample scenario (simple, made up)
Feature: *"Add a `priority` filter to GET /api/activities"*

1. **Developer:** `@planner Add a priority filter to GET /api/activities`
2. **Planner** writes `spec.md` with one rule ("priority must be LOW/MEDIUM/HIGH") and one sprint contract
   with two ACs, then the run stops.
3. **Developer** reads the spec and types `APPROVED`.
4. **Generator** adds the query parameter, service logic and tests `AC1_1`, `AC1_2`.
5. **Evaluator** runs `eng/verify.ps1` and the architecture tests, and scores the result.
   - Say the line-1 verdict is `VERDICT: FAIL`: the error used `throw new Exception` (failure mode F2).
   - The **Generator** runs again (iteration 2) with that feedback and switches to `ValidationError`.
   - Now the verdict is `VERDICT: PASS`.
6. **Monitor** writes the run log, and the sprint is committed.

If the verdict were still FAIL after 3 iterations, the harness would write `escalation.md` and stop for a human.

## Original scenario (what actually happened in this repo)
Feature: **shift handover bulk update**, `PATCH /api/activities/bulk-status`. The prompt is in `PROMPT.md`.

| Step | Result |
|---|---|
| Planner | spec with 13 business rules and 17 ACs across 3 sprints |
| Developer | `APPROVED` |
| Sprint 1: endpoint, validation, happy path | PASS, score 100, 1 iteration |
| Sprint 2: per-item failures (store scope, permissions, reopen rule) | PASS, score 100, 1 iteration |
| Sprint 3: audit trail + handover note | PASS, score 94, 1 iteration (C2: rejected items not asserted unchanged) |
| After the run | C2 fixed by hand in commit `f3a2bc5`. This is the lesson in `REFLECTION.md`. |
| Deployment | Podman container on http://localhost:5000, evidence in `DEPLOYMENT.md` |
| CI | `.github/workflows/verify.yml` runs the same `eng/verify.ps1` on every push |

The full audit trail is in `.harness/reviews/`: the spec, the contracts, the Generator summaries, the
Evaluator feedback, the run logs and the quality trend.

## Commands I'll need again
```powershell
pwsh eng/verify.ps1                         # build + format + tests + coverage (what CI runs)
dotnet run --project src/StoreOps.Api       # http://localhost:5000, no token in Development
podman compose up -d --build                # container (Production, needs a token)
powershell -ExecutionPolicy Bypass -File docs\deployment\demo-bulk-status.ps1
podman compose down
```

## Where to look
- The why behind the design: `DESIGN_BRIEF.md`, `JOURNAL.md`
- What went well and what didn't: `REFLECTION.md`
- How the baseline code was corrected: `docs/bootstrap-corrections.md`
