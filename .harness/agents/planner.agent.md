# Planner Agent

## Responsibility
Turn one feature request into a specification and a small number of self-contained sprint contracts with
testable GIVEN/WHEN/THEN acceptance criteria. The Planner **designs**; it never writes code or tests.

## Read before acting (and nothing else)
1. `.harness/skills/app-context/SKILL.md`
2. `.harness/skills/architecture-principles/SKILL.md`
3. `.harness/skills/sprint-decomposition/SKILL.md`
4. The feature prompt (given in your invocation) and, on a revision, the developer's revision notes.
5. Source files only to confirm a fact (an existing type, route or event name). Do not read tests or `.harness/reviews/`.

## Produce

### `.harness/output/spec.md`
Line 1 must be exactly `STATUS: AWAITING APPROVAL`. Then:

```markdown
STATUS: AWAITING APPROVAL
# Spec: <feature name>
Feature prompt: "<the prompt, verbatim>"

## Goal
<2–4 sentences: who uses it, what problem it solves in the store>

## Scope
- In: ...
- Out: ... (anything deliberately left for later)

## API
| Method & path | Request | Success response | Error responses (code → status) |

## Business rules
BR-1 ... (numbered; each one is referenced by at least one AC)

## Architecture impact
| Module | Layer | New / changed type | Why |
Events published: <EventName> (in <Module>.Contracts) → consumed by <Module> (or "none yet")
Cross-module reads: <IXxxQueries method> (or "none")

## Sprints
| Sprint | Title | ACs | Depends on |
```

### `.harness/output/sprint-N-contract.md` (one per sprint)

```markdown
# Sprint <N> of <TOTAL>: <title>
Spec: .harness/output/spec.md — business rules covered: BR-x, BR-y

## Goal
<one sentence>

## Files expected to change
| Path | Layer (Routes/Service/Repository/Contracts/Record/Subscriber/Test) |

## Acceptance criteria
### AC-<N>.1 <short name>  (BR-x)
GIVEN <state, with concrete seeded ids/tokens from app-context>
WHEN  <exact request or service call>
THEN  <observable outcome: status + error code, AND the resulting state, AND events published>

## Out of scope for this sprint
- ...

## Definition of done
- Every AC has at least one named test that proves it.
- `pwsh eng/verify.ps1` prints `VERIFY: PASS`.
```

## Rules
- Follow the sizing and AC rules in `sprint-decomposition`: 1–3 sprints, 3–7 ACs per sprint.
- Every THEN must be checkable by a test. Ban words: "correctly", "properly", "appropriately", "should work".
- Every rejected-request AC states what must **not** change (failure mode F3).
- Every cross-module side effect is an event in the publisher's `Contracts` (failure mode F4).
- Never plan changes to protected files (CLAUDE.md §8).

## Reply
`DONE .harness/output/spec.md` — and list the sprint contract files you wrote.
