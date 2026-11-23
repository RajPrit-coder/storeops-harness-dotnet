# StoreOps — Claude Code Development Harness

This repository holds the StoreOps API (.NET 10, ASP.NET Core minimal APIs, in-memory storage) **and** the
harness that governs how new features are added to it. The harness exists to stop four failure modes the
client's standards team saw in earlier AI-generated code:

1. **F1** — importing another module's repository instead of going through its service contract
2. **F2** — raw `throw new Exception(...)` instead of the `AppError` hierarchy
3. **F3** — tests that assert HTTP status codes but not the business rule
4. **F4** — writing to a sibling module directly instead of publishing an event on the bus

Outside a harness run (normal questions, small fixes), just follow `.harness/skills/architecture-principles/SKILL.md`
and run `pwsh eng/verify.ps1` before saying something is done.

---

## 1. Entry prompt

A harness run starts **only** when the developer's message begins with `@planner`:

```
@planner <feature description in one or two sentences>
```

Example (the demonstration run, see `PROMPT.md`):
`@planner Add shift handover bulk update — PATCH /api/activities/bulk-status …`

## 2. Agents and how each is invoked

| Agent | Definition | Writes |
|---|---|---|
| Planner | `.harness/agents/planner.agent.md` | `.harness/output/spec.md`, `.harness/output/sprint-N-contract.md` |
| Generator | `.harness/agents/generator.agent.md` | code in `src/` + `tests/`, `.harness/output/generator-summary.md` |
| Evaluator | `.harness/agents/evaluator.agent.md` | `.harness/output/evaluator-feedback.md` |
| Monitor | `.harness/agents/monitor.agent.md` | `.harness/reviews/sprint-N-run-log.md`, `.harness/reviews/quality-trend.md` |

**Every agent runs as a fresh subagent** (Agent tool, `subagent_type: general-purpose`). The orchestrator — you,
the main session — never does an agent's work itself. Use exactly this prompt, filling the brackets:

```
You are the StoreOps harness <ROLE>. Read .harness/agents/<role>.agent.md and follow it exactly.
Sprint: <N> of <TOTAL>. Iteration: <K> of 3.
Inputs: <the input files listed for this role in its agent file, as paths>.
Do not read any other handoff file. Reply with one line: DONE <path of the file you wrote>.
```

## 3. Sequence

```
@planner <feature>
   │
   ▼
[Planner] ──► spec.md (line 1: STATUS: AWAITING APPROVAL) + sprint-1..N-contract.md
   │
   ▼  STOP. Show the developer the spec summary and the sprint list. Wait.
   │  Developer replies APPROVED  → set spec.md line 1 to STATUS: APPROVED, continue.
   │  Developer replies anything else → send it to a NEW Planner subagent as revision notes, repeat.
   ▼
for each sprint N = 1..TOTAL:
   K = 1
   loop:
      [Generator] (inputs: contract N; plus evaluator-feedback.md if K > 1)
      [Evaluator] (inputs: contract N, generator-summary.md, the working-tree diff)
      archive  evaluator-feedback.md → .harness/reviews/sprint-N-iter-K-evaluator-feedback.md
      route on the VERDICT line (section 4)
   [Monitor]  → run-log + quality trend
   archive  contract, generator-summary, final evaluator-feedback  → .harness/reviews/sprint-N-*.md
   commit   "sprint-N: <sprint title> (<VERDICT>, <K> iteration(s))"
after the last sprint: copy .harness/output/spec.md → .harness/reviews/spec.md, commit, report to the developer.
```

The developer's only active steps are the `@planner` prompt and typing `APPROVED`. Everything between is
autonomous; the developer re-enters only at an escalation.

## 4. Routing logic (machine-readable markers)

The orchestrator reads **only line 1** of `.harness/output/evaluator-feedback.md`. It must be exactly one of:

| Line 1 | Action |
|---|---|
| `VERDICT: PASS` | run Monitor, archive, commit, next sprint |
| `VERDICT: CONDITIONAL_PASS` | same as PASS; the Monitor records the open items as carried debt |
| `VERDICT: FAIL` | if K < 3: K = K + 1, re-run Generator with this feedback. If K = 3: escalate (section 5) |
| anything else / file missing | treat as `FAIL` with blocking issue "Evaluator produced no valid verdict" |

Never override a verdict, never edit the Evaluator's file, never "fix it yourself" between iterations.

## 5. Iteration limit and escalation

**Maximum 3 Generator iterations per sprint.** After the 3rd `FAIL`, write `.harness/output/escalation.md`,
copy it to `.harness/reviews/sprint-N-escalation.md`, run the Monitor (escalation flag = YES), **do not
commit the sprint's code**, and stop. Format:

```markdown
# ESCALATION — Sprint <N>: <title>
- Iterations used: 3 of 3
- Last verdict: FAIL (score <x>/100)
- Blocking issue(s): <each failing hard gate / check ID with file:line, copied from the last feedback>
- What was tried: <one line per iteration, from the three feedback files>
- Decision needed from the developer: <e.g. relax AC-3, split the sprint, fix by hand, abandon>
- Recipient: the developer who started the run (owner of PROMPT.md); tech lead if an architecture gate is involved
```

Then tell the developer: "Escalated sprint <N> — see .harness/output/escalation.md".

## 6. Context scoping (keep each agent's window small and clean)

- **One subagent per role per iteration.** Nothing carries over between invocations except files on disk.
- **Inputs are explicit.** Each agent file lists exactly which skill files and handoff files it may read.
  The Evaluator never sees the Generator's reasoning — only the contract, the summary and the diff.
- **A retry sees only the latest feedback**, not the history of earlier iterations.
- **Contracts are self-contained.** A Generator needs `sprint-N-contract.md`, not the whole spec.
- **The orchestrator keeps no detail.** It reads verdict line 1 and file names, nothing else, so its own
  context stays flat across long runs.
- **Between sprints**, `.harness/output/generator-summary.md` and `evaluator-feedback.md` are deleted after
  archiving, so sprint N+1 cannot accidentally read sprint N's files.
- **Skill-file depth vs token budget:** shared skills are kept short (they are read by every agent);
  depth goes into the role-specific files that only one agent reads. Approximate sizes are listed in
  `.harness/skills/README.md`.

## 7. Handoff files

| File | Written by | Read by | In git? |
|---|---|---|---|
| `.harness/output/spec.md` | Planner | orchestrator, developer | no (archived to reviews at the end) |
| `.harness/output/sprint-N-contract.md` | Planner | Generator, Evaluator, Monitor | no (archived) |
| `.harness/output/generator-summary.md` | Generator | Evaluator, Monitor | no (archived) |
| `.harness/output/evaluator-feedback.md` | Evaluator | orchestrator (line 1), Generator on retry, Monitor | no (archived) |
| `.harness/output/escalation.md` | orchestrator | developer | no (archived) |
| `.harness/reviews/*` | orchestrator / Monitor | humans, future Monitor runs | **yes — the audit trail** |

## 8. Protected files — no agent may change these during a run

`CLAUDE.md`, `.harness/agents/`, `.harness/skills/`, `eng/`, `tests/StoreOps.Api.Tests/Architecture/`,
`BannedSymbols.txt`, `.editorconfig`, `stylecop.json`, `Directory.Build.props`, `Directory.Packages.props`,
`coverage.runsettings`, `.github/`. The Evaluator fails any sprint that touches them (hard gate HG-A2), so a
Generator can never pass by weakening the checks.

## 9. Relationship to CI/CD

The harness **precedes and feeds** CI; it does not replace it. The Evaluator's automated hard gates run the
same script CI runs — `pwsh eng/verify.ps1` (build with analyzers + StyleCop + banned APIs, format check,
all tests including the architecture tests, per-layer coverage gate). The harness catches problems while the
Generator can still fix them cheaply; `.github/workflows/verify.yml` re-runs the identical script on every
push and pull request as the non-negotiable gate before `main`. Harness files live in `.harness/`, CI
configuration in `.github/`, and neither depends on the other.

## 10. Everyday commands

```bash
pwsh eng/verify.ps1                      # everything the Evaluator and CI run (Release config)
dotnet run --project src/StoreOps.Api    # http://localhost:5000 (Development: no token needed)
```
