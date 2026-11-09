# US-7 — QA Testing

**Date** — 2026-11-09
**Tester** — Shaik
**Environment** — Isolated local Docker Compose (`us7qa`).
**Branch** — `develop`
**Commit** — `1d926219cec3f21c385a80013fa87f62f41a6e23`

## Scope

Login rejection for waiters and chefs using incorrect passwords or deactivated accounts.

## Tests

- TC-02 · US-7 — Login with an incorrect waiter password was rejected — **PASS**
- TC-03 · US-7 — Login with correct credentials for a deactivated waiter was rejected — **PASS**
- TC-06 · US-7 — Login with an incorrect chef password was rejected — **PASS**
- TC-07 · US-7 — Login with correct credentials for a deactivated chef was rejected — **PASS**

## Result

**4 PASS, 0 FAIL, 0 BLOCKED.** All four results were confirmed by the tester.
