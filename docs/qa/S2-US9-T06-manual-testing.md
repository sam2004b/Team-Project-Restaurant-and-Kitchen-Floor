# S2-US9-T06 — QA Testing

**Date** — 07/11/2026

**Tester** — Shaik

**Environment** — Isolated local Docker Compose stack, PostgreSQL 16, ASP.NET Core

**Branch** — `develop`

**Commit** — `578226fd24f62dc30730d65f9f4a898a8941e427`

## Scope

Restaurant table settings persistence, duplicate table-number prevention, seat-count validation, and assignment or reservation effects.

## Tests

- TC-01 — Table 1 changed from four to six seats and retained six after reopening settings — **PASS**
- TC-02 — Duplicate table number 12 was rejected with a field-specific message and no duplicate record — **PASS**
- TC-03 — Zero and negative seat counts were rejected with field-specific messages and no invalid saved changes — **PASS**
- TC-04 — Table 12 appeared according to its current assignment or reservation state — **PASS**

## Result

**4 PASS, 0 FAIL, 0 BLOCKED.**
