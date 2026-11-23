# Architecture journal

Design decisions, trade-offs and insights captured while building the StoreOps harness (optional
deliverable, spec §6). Newest entries at the bottom. *Review and reword these in your own voice — reviewers
will ask about them.*

## 2026-11-16 — Generating the baseline
- The bootstrap prompt alone ("9 endpoints as specified in Section 3.6") produced an invented API and domain
  model. Almost every correction came from not having Section 3 in context. **Insight:** the Planner and
  Generator must get §3.3–3.6 through skill files, never through a pointer to a document.
- Full correction log: `docs/bootstrap-corrections.md`.

## 2026-11-16 — Gates must be proven to fail
- An independent review wrote 18 deliberate violations; all of them passed the first architecture tests
  (`VERIFY: PASS`). The name-based checker could not see inside async lambdas; `Shared/` and `Program.cs`
  were unchecked. Rebuilt the checks on an IL model (Mono.Cecil). The same probes now fail 13 tests.
- The coverage gate once reported 0/0 lines as PASS because of a path mismatch.
- **Insight:** an automated hard gate is only trustworthy after you have watched it fail on known-bad code.
  This became the Evaluator rule "a gate that inspected nothing is a FAIL".

## 2026-11-23 — Harness design decisions
| # | Decision | Alternatives considered | Why | Assumption |
|---|---|---|---|---|
| D1 | Demo feature: shift handover bulk update | regional rollup, SLA breach alerting, planogram templates | stays mostly in one module, no background timer, still exercises layering, partial failure, events and audit | one module is enough to show all four failure-mode gates |
| D2 | Evaluator: 3 dimensions, Architecture 40 / Correctness 35 / Tests 25 | 2 dims 50/50; 4 dims with a subjective "code quality" | every client failure mode lands in A or C; no dimension is pure taste | the standards team ranks boundary breaks above functional bugs |
| D3 | Every agent is a fresh subagent; handoff only via files | one session switching roles | the Evaluator cannot be biased by the Generator's reasoning; context stays flat across sprints | Claude Code's Agent tool gives each subagent a clean context |
| D4 | Hard gates are commands (tests, build, git status), checklist items need cited evidence or FAIL | pure LLM scoring | same check results ⇒ same verdict; leniency cannot pass a boundary break | the architecture tests cover what they claim (proven by probes) |
| D5 | Generator may not touch the checks (HG-A2 on protected paths) | trust the Generator | the cheapest way for an AI to "pass" is to weaken the gate | the harness files are committed before a run |
| D6 | Deployment: local Docker | Azure App Service | spec minimum, no cloud subscription needed | Docker Desktop can be installed on the machine |
