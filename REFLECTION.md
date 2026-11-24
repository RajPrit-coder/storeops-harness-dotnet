# Reflection — demonstration run (shift handover bulk update)

## What the harness did well
- **The developer typed one prompt and one word.** From `@planner …` and `APPROVED`, the harness produced a
  13-rule spec, 3 sprint contracts with 17 GIVEN/WHEN/THEN criteria, working code, 3 commits and a complete
  audit trail, with no manual step in between.
- **No client failure mode reached the code.** All hard gates passed in every sprint; the generated feature
  keeps every rule — the audit trail stayed inside Activities instead of becoming a cross-module write, the
  existing `ActivityStatusChanged` event drives the Alerts escalation (no `NotificationService` import),
  per-item failures use `AppError` codes (`NOT_FOUND`, `FORBIDDEN`, `BUSINESS_RULE_VIOLATION`), and other
  stores' activities are reported `NOT_FOUND` and left untouched — verified again in the running container.
- **The Evaluator found a real gap with line-level evidence.** In sprint 3 it flagged check C2 at
  `ActivityBulkStatusServiceTests.cs:387` and `ActivityBulkStatusRoutesTests.cs:424`: two rejected items were
  never asserted unchanged. The Monitor traced it to the skill files that should have prevented it.

## Where it fell short
1. **A finding on a PASS goes nowhere.** C2 is not a critical item, so 94 points meant `VERDICT: PASS` and the
   orchestrator moved on. The required fix sat in the feedback file; it was applied by hand after the run
   (commit `f3a2bc5`), outside the harness — exactly the kind of untracked change the harness exists to avoid.
   This is the assumption behind decision D3 in the Design Brief, and it did not hold.
2. **The retry loop was never exercised.** Every sprint passed on iteration 1, so this run does not show the
   FAIL → feedback → fix path. The gates were proven to fail with deliberate probes before the run, but not by
   a real Generator mistake during it.
3. **The Monitor's recommendations need an owner.** It proposed two skill-file changes (per-item failures count
   as rejections in `how-to-test` and in the C2 row of `grading-criteria`). Skill files are protected during
   a run, so nothing applies them; between runs it depends on someone remembering.
4. **Token cost was underestimated.** The skills index estimated ~23k tokens of instructions per sprint; the
   Monitor's estimate for sprint 3 was ~68k, because the Evaluator reads the full diff (~38k characters).

## One concrete improvement
**Route "PASS with required fixes" through one more Generator iteration when iterations remain.** Change the
routing table in `CLAUDE.md` §4: if line 1 is `VERDICT: PASS` but the feedback lists any FAIL item and K < 3,
re-run the Generator with that feedback, then the Evaluator again; accept the sprint only when the list is
empty or K = 3 (then record the rest as carried debt). In this run that would have cost one extra
Generator + Evaluator pair (~55k tokens) and the C2 fix would have been made, reviewed and archived inside the
harness instead of in a manual commit. Second step: a short "skill-file review" between runs in which the
developer accepts or rejects each Monitor recommendation, recorded in `JOURNAL.md`.
