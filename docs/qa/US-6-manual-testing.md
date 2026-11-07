# US-6 — QA Testing

**Date** — 07/11/2026

**Tester** — Shaik

**Environment** — Isolated Docker, PostgreSQL 16, ASP.NET Core 8, React and Chrome

**Branch** — `origin/develop`

**Commit** — `578226fd24f62dc30730d65f9f4a898a8941e427`

## Scope

Administrator and Chef dish availability, customer and waiter menu visibility, restoration, existing orders, and repeated updates. Covers eight original acceptance cases from US-12 and US-46. Customers can view the menu but cannot place orders.

## Tests

- TC-01 — Admin marked dish unavailable; waiter add returned `409` — **PASS**
- TC-02 — Customer menu reflected availability — **PASS**
- TC-03 — Sent item remained unchanged after Admin change; preparation unavailable — **BLOCKED**
- TC-04 — Admin restored availability; waiter add returned `201` — **PASS**
- TC-05 — Chef marked dish unavailable; waiter add returned `409` — **PASS**
- TC-06 — Menu APIs reflected Chef change; waiter UI unavailable — **BLOCKED**
- TC-07 — Sent item remained unchanged after Chef change; preparation and completion unavailable — **BLOCKED**
- TC-08 — Repeated update returned `changed:false` without duplicate state — **PASS**

## Result

**5 PASS, 0 FAIL, 3 BLOCKED, 0 NOT RUN.**

Stale selections were rejected without an invalid item write. In the observed concurrent update/add check, the update succeeded and the add returned `409`; the order remained empty. Repeated toggles created no duplicate state.

Backend tests: **27/27 focused** and **81/81 total** passed, including unavailable-dish rejection and send-time validation. [CI for the tested commit](https://github.com/sam2004b/Team-Project-Restaurant-and-Kitchen-Floor/actions/runs/42363535995) passed.

API scenarios were rerun with curl; no independent reviewer rerun was recorded. Blockers are the missing waiter UI and kitchen preparation/completion workflow.
