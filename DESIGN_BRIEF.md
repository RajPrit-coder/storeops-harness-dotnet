# Harness Design Brief — StoreOps (.NET)

**Feature demonstrated:** shift handover bulk update — `PATCH /api/activities/bulk-status` (spec §3.4).
**Run evidence:** `PROMPT.md`, `.harness/reviews/` (spec, 3 contracts, summaries, feedback, run logs), `DEPLOYMENT.md`.
**Outcome:** 3 sprints, 17 acceptance criteria, every sprint PASS in one iteration (scores 100 / 100 / 94).

---

## A. Intent decomposition

### From one prompt to three sprints
The developer typed one sentence (`PROMPT.md`). The Planner turned it into `spec.md` with 13 numbered
business rules (BR-1…BR-13), an API table listing every error code, and an architecture-impact table naming
the layer of every new type. The developer reviewed it and typed `APPROVED`; nothing else was asked of them.

The `sprint-decomposition` skill tells the Planner to cut at **architectural seams**, so that every sprint
ends with the application working and `eng/verify.ps1` green:

| Sprint | Seam | Rules | ACs | Why the boundary is here |
|---|---|---|---|---|
| 1 | Contract + validation + happy path | BR-1…BR-6 | 6 | Fixes the request/response shape and the "one event per real change" rule that everything later builds on |
| 2 | Per-item failure handling | BR-7…BR-10 | 6 | Partial failure, store scoping, the associate permission rule and "DONE cannot be reopened" only make sense once there is a result per item to put `FAILED` into |
| 3 | Audit trail + handover note | BR-11…BR-13 | 5 | An audit entry is written only for `UPDATED` items, so it depends on sprint 2 having defined which items those are |

A rule is never split across sprints, and each contract is self-contained (it restates the facts it needs
from earlier sprints under "Background"), so a fresh Generator needs only its own contract.

### Making acceptance criteria testable
Every AC has three parts, uses real seeded ids and tokens, and carries its id into the test name
(`AC-2.1` → a test containing `AC2_1`), which lets the Evaluator check it mechanically (hard gate HG-B2).
The skill bans vague words ("correctly", "properly", "handles gracefully") and requires every THEN to state
**the status/code, the resulting state, and the events** — and, for rejections, what must *not* change.
That last clause is aimed directly at failure mode F3 (tests that check only the status code).

### Example contract entry (sprint 2, verbatim)
```
### AC-2.1 Missing and other-store activities fail as NOT_FOUND, others still update  (BR-8, BR-7)
GIVEN act-A exists in store-001 (created by dev-token-store-manager) and act-B exists in store-002
      (created by dev-token-store-002-manager), both TODO; act-missing does not exist
WHEN  dev-token-store-manager sends PATCH /api/activities/bulk-status
      { "status": "DONE", "activityIds": ["act-B", "act-missing", "act-A"] }
THEN  200; requested = 3, updated = 1, unchanged = 0, failed = 2;
      results[0] = { act-B, FAILED, error.code = NOT_FOUND };
      results[1] = { act-missing, FAILED, error.code = NOT_FOUND };
      results[2] = { act-A, UPDATED, TODO → DONE };
      act-B is still TODO (checked as dev-token-store-002-manager) with unchanged updatedAt; act-A is DONE;
      exactly one ActivityStatusChanged event (for act-A).
```
Each clause maps to an assertion: response counts and per-item results, the other store's activity read back
by its own manager, and the event count from the recording event bus.

---

## B. Governance framework

### Skill-file strategy
Seven skills, split by who needs them. Shared files are short because every agent pays for them; depth lives
in files only one role reads (`.harness/skills/README.md` lists ~token sizes).

| Skill | Read by | StoreOps-specific content (not generic advice) |
|---|---|---|
| `app-context` | Planner, Generator, Monitor | 5 modules, §3.3 types with exact wire values (`IN_PROGRESS`, `STORE_MANAGER`), the 9 endpoints, seeded tokens/ids, `verify.ps1` |
| `architecture-principles` | all four | §3.5 rules R1–R6, each with the failure mode it stops and the test that enforces it; the allowed module-dependency table; protected files |
| `sprint-decomposition` | Planner | seams for StoreOps features, AC format, banned words, "every rejection states what must not change" |
| `coding-conventions` | Generator | where each piece goes (Routes/Service/Repository/Contracts/Record), role naming the architecture tests rely on, which `AppError` for which situation, StyleCop traps |
| `how-to-test` | Generator | `ModuleTestContext`, `RecordingEventBus`, `TestApi` helpers, AC-id test names, state + event assertions, coverage thresholds 80/70/60/70 |
| `how-to-review` | Evaluator | evidence rules (PASS needs file:line), StoreOps diff smells mapped to check IDs |
| `grading-criteria` | Evaluator | 3 dimensions, 5 hard gates, 16-item checklist, the verdict formula, a worked example |

The rules in skill files are backed by **code that fails the build or the tests**: banned APIs
(`BannedSymbols.txt`), and IL-level architecture tests (Mono.Cecil) that see inside lambdas and async methods.
The skill tells the Generator the rule; the test makes sure the rule holds.

### The review archive as an audit trail
`.harness/output/` is the scratch area (gitignored). After every iteration the orchestrator copies the
Evaluator's feedback to `.harness/reviews/sprint-N-iter-K-evaluator-feedback.md`; after the verdict it
archives the contract, generator summary and final feedback, the Monitor writes `sprint-N-run-log.md` and a
row in `quality-trend.md`, and the sprint is committed as `sprint-N: <title> (<VERDICT>, <K> iteration(s))`.
Anyone with repository access can read it; it changes only through commits, so `git log -- .harness/reviews`
is the history of every acceptance decision.

How it surfaces a recurring issue: `quality-trend.md` lists failed check IDs per sprint. In this run, sprint 3
shows `C2` — the first deduction of the run — and its run log names the skill files that should have
prevented it (`how-to-test`, `grading-criteria`) with the exact rule to add. If `C2` appeared again in a later
sprint, the trend table would show the same ID twice and point at the same skill file.

### One rule, and what breaks without it
> **R1 (architecture-principles):** "Contract interfaces contain read methods only, and the implementation
> must not write or publish. A method named `FindAndMarkDoneAsync` is still a write → FAIL."

Cross-module reads in StoreOps go through a module's query contract (`IActivityQueries`, `IStaffDirectory`,
`IProgrammeQueries`). Without this rule a Generator asked for "Alerts should close the activity" can add a
read-named method to `IActivityQueries` that saves the activity, and call it from Alerts. Every name-based
check passes — the dependency is "through the contract" — yet Alerts now writes to Activities directly:
failure mode F4, and the "users/reports are read-only" guarantees are gone. This was not hypothetical: an
independent review of the scaffold wrote exactly that probe and it passed. The rule is now enforced by
`ReadOnlyContractTests`, which follows each contract method's IL and fails on any repository write or event
publish it can reach.

### Relationship to CI
The harness precedes and feeds CI; it does not replace it. The Evaluator's hard gates run `eng/verify.ps1`,
the same script `.github/workflows/verify.yml` runs on every push. Harness files live in `.harness/`, CI in
`.github/`; the harness catches problems while the Generator can still fix them cheaply, CI is the final gate.

---

## C. Non-determinism strategy

### Dimensions and weights
| Dimension | Weight | Why for StoreOps |
|---|---|---|
| A. Architecture compliance | 40% | All four client failure modes are architectural; a feature that works but breaks a boundary is rejected by the standards team |
| B. Functional correctness | 35% | Every GIVEN/WHEN/THEN must hold, including partial failure and store scoping |
| C. Test quality | 25% | Tests keep the *next* AI change honest; status-code-only tests (F3) protect nothing |

### Hard gates — commands, not opinions
| Gate | Command | Failure mode | Why it cannot be a soft check |
|---|---|---|---|
| HG-A1 | architecture tests | F1, F2, F4, layering | A boundary break is a binary fact; a reviewer "mostly satisfied" with layering would let F1 through |
| HG-A2 | `git status --porcelain` on protected paths | weakening the gates | The cheapest way for an AI to pass is to edit the test; any edit must fail, however reasonable it looks |
| HG-B1 | `dotnet build` (0 warnings) | F2 via banned APIs, nullable | The compiler already decided; re-judging it adds only variance |
| HG-B2 | `dotnet test --filter AC<s>_<n>` per AC | ACs claimed but unproven | "MET" in a summary is a claim; a passing test with the AC id is evidence |
| HG-C1 | `eng/verify.ps1` | F3 coverage, format | Coverage thresholds are numbers |

### Turning variable output into a binary result
LLM-assessed checklist items (16, worth 100 points) are each PASS or FAIL. A PASS **must cite evidence**
(`path:line` or a passing test name); no evidence is FAIL `[UNVERIFIED]`. A gate that fails and then passes
on re-run is `FLAKY` = FAIL. Four items are critical (★ A1, A2, A4, C1 — one per client failure mode).
The verdict is a fixed formula: any hard gate or ★ item fails → FAIL; else score ≥ 85 with every dimension
≥ 60% → PASS; ≥ 70 with every dimension ≥ 50% → CONDITIONAL_PASS; otherwise FAIL. A missing or malformed
`VERDICT:` line is treated as FAIL by the orchestrator. Given the same check results, the verdict is always
the same.

### Worked example — sprint 3 of the real run
The Generator's output varied in many ways the Evaluator ignored (helper names, test layout). What decided
the verdict: all five hard gates passed (e.g. HG-B2: AC3_1…AC3_5 each had passing tests); 15 checklist items
passed with cited lines (e.g. A2: save at `ActivityService.cs:282` → audit `:283` → publish `:295`); **C2 failed**
— two per-item rejections in the AC3_2 tests did not assert the rejected activities were unchanged.
Score = 100 − 6 = 94 (A 40, B 35, C 19). No hard gate or ★ item failed; 94 ≥ 85; C 19 ≥ 15 → `VERDICT: PASS`.
Had the same gap been in C1 (★), the verdict would have been FAIL regardless of the 94 points.

### Escalation path
Trigger: a third FAIL in the same sprint, or an Evaluator that produces no valid verdict three times. The
orchestrator writes `.harness/output/escalation.md` (archived to reviews) containing the sprint, "3 of 3"
iterations, last score, each blocking gate/check with file:line, one line per attempt, and the decision
needed (relax an AC, split the sprint, fix by hand, abandon). It does **not** commit the sprint's code.
Recipient: the developer who started the run; the tech lead when an architecture gate is involved. Expected
response: decide, then restart the run from the Planner with revision notes. (Not triggered in this run.)

---

## D. Architectural decisions

### D1 — Every agent runs as a fresh subagent; files are the only handoff
- **Alternatives:** one long session switching roles; a persistent Generator that keeps its context across retries.
- **Rationale:** the Evaluator must judge the diff, not be persuaded by the Generator's reasoning; context
  stays flat across sprints (the orchestrator reads only line 1 of the feedback); a retry sees only the latest
  feedback. Measured cost: ~68k tokens for sprint 3 (Generator 27k, Evaluator 28k, Monitor 12k).
- **Assumption:** Claude Code's Agent tool gives each subagent a clean context and the handoff files carry
  everything a role needs — which is why contracts restate their background facts.

### D2 — Architecture rules are enforced by IL-level tests used as hard gates, not by LLM judgment
- **Alternatives:** LLM-only review against the skill files; a name/namespace-based checker (NetArchTest).
- **Rationale:** LLM review is lenient and variable. A namespace checker was tried first and an independent
  review showed 18 deliberate violations passing it (it cannot see inside async lambdas; `Shared/` and
  `Program.cs` were unchecked). The Mono.Cecil model charges every reference to its outer type; the same
  probes now fail 13 tests.
- **Assumption:** the probes cover the ways an AI is likely to break a rule. A gate is trusted only after it
  has been seen failing on known-bad code.

### D3 — The Generator may not touch the checks (HG-A2), and only critical items override the score
- **Alternatives:** trust the Generator; make every checklist item blocking.
- **Rationale:** weakening a test is the cheapest "fix", so any change to protected paths fails outright.
  Making all 16 items blocking would turn minor findings into retries and burn tokens; only the four items
  tied to client failure modes are critical.
- **Assumption:** non-critical findings on a PASS still get fixed. **This run showed the assumption is weak:**
  sprint 3's C2 finding was recorded but nothing in the loop acted on it; it was fixed by hand afterwards
  (commit `f3a2bc5`). See `REFLECTION.md`.
