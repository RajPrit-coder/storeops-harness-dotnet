# Skill: sprint-decomposition (StoreOps)

## Purpose
How the Planner turns a StoreOps feature into sprints and acceptance criteria that a Generator can build
and an Evaluator can check without guessing. Read by: Planner.

## Sizing
- **1–3 sprints per feature; 3–7 ACs per sprint.** One Generator iteration must be able to finish a sprint.
- Each sprint ends with the app working and `eng/verify.ps1` green — never leave a half-built endpoint.
- Draw sprint boundaries at **architectural seams**, in this order:
  1. *Contract + happy path*: request/response DTOs, service method, route, success tests.
  2. *Rules + failure handling*: validation, permissions, store scoping, partial failures, error codes.
  3. *Cross-module effects*: new events, subscribers in other modules, audit records.
  Merge sprints when the feature is small; never split one rule across two sprints.

## Acceptance criteria
Format — always all three parts, always concrete:
```
### AC-<sprint>.<n> <name>  (BR-x)
GIVEN <seeded state with real ids/tokens from app-context, plus anything the test must create first>
WHEN  <exact HTTP request (method, path, body) or service call>
THEN  <status + error code> AND <resulting state> AND <events published or "no events">
```
Rules:
- Name it so a test can carry the id: AC-1.2 → test name contains `AC1_2`.
- THEN must be observable from a test. Forbidden words: "correctly", "properly", "appropriately", "works",
  "handles gracefully".
- Every **rejection** AC says what must stay unchanged (status of the other activities, no event) — this is
  what stops tests that only check the status code (F3).
- Every **cross-module** effect is stated as an event, with publisher, payload fields and subscriber (F4).
- Use the existing error codes (`VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND`, `CONFLICT`,
  `BUSINESS_RULE_VIOLATION`) and wire enum values (`DONE`, `BLOCKED`) exactly.
- Include at least one AC for: another store's resource (→ NOT_FOUND / skipped), and a forbidden role or
  inactive user where the feature has permissions.

## Example (good)
```
### AC-1.3 Activities of another store are not updated  (BR-4)
GIVEN act-A exists in store-001 and act-B exists in store-002
WHEN  dev-token-store-manager (store-001) sends PATCH /api/activities/bulk-status
      { "status": "DONE", "activityIds": ["act-A", "act-B"] }
THEN  200; results[act-A].outcome = UPDATED, results[act-B].outcome = FAILED with code NOT_FOUND;
      act-B is still TODO; exactly one ActivityStatusChanged event (for act-A)
```
Bad: "THEN the endpoint handles other stores properly." — nothing a test can check.

## Spec checklist before writing STATUS: AWAITING APPROVAL
- [ ] Every business rule BR-n is covered by ≥1 AC.
- [ ] The API table lists every error code the feature can return.
- [ ] Architecture impact names the layer of every new type and every event.
- [ ] Nothing in the plan touches protected files or adds a module dependency not allowed by R1.
