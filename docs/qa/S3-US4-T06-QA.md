# S3-US4-T06 — QA Testing

**Date** — 20/11/2026

**Tester** — Shaik

**Environment** — Isolated Docker, PostgreSQL 16, ASP.NET Core 8, Chrome 157

**Branch** — `develop`

**Commit** — `66792cdeb247411c73e90739c58d6e7313e28cc6`

## Scope

Dish preparation status on submitted orders, including valid and invalid transitions, persistence after reload, role restrictions, waiter visibility, and failed saves.

## Tests

- TC-01 — Chef advanced the dish from New to Preparing, then to Ready — **PASS**
- TC-02 — All seven disallowed transitions were rejected and the saved status stayed the same — **PASS**
- TC-03 — Reloading the order showed the latest saved status after each accepted transition — **PASS**
- TC-04 — Only the same-restaurant Chef could change the status. Other roles and signed-out sessions were denied — **PASS**
- TC-05 — The assigned waiter saw both updates automatically. The unassigned waiter could not see the order — **PASS**
- TC-06 — Shaik confirmed the failed-save check passed. Reloading showed the previous status — **PASS**

## Result

**6 PASS, 0 FAIL, 0 BLOCKED.**
