# US-8 — QA Testing

**Date** — 2026-11-09
**Tester** — Shaik
**Environment** — Isolated local Docker Compose (`us8qa`), PostgreSQL 16, ASP.NET Core 8.
**Branch** — `develop`
**Commit** — `1d926219cec3f21c385a80013fa87f62f41a6e23`

## Scope

Backend API checks for the eight US-22 and US-38 cases covering assigned tables and orders, reassignment, direct access denial, and empty work lists.

## Tests

- **TC-01 — PASS.** Ivan’s table list returned tables 3 and 12.
- **TC-02 — PASS.** A direct request for Maria’s table returned HTTP 404 with no table data.
- **TC-03 — PASS.** After reassignment, table 12 disappeared from Ivan’s list and appeared in Maria’s.
- **TC-04 — PASS.** Ivan’s table list returned an empty array after his assignments were removed.
- **TC-05 — PASS.** Ivan’s order list returned only the orders for tables 3 and 12.
- **TC-06 — PASS.** A direct request for Maria’s order returned HTTP 404 with no order data.
- **TC-07 — PASS.** The same open order disappeared for Ivan and became accessible to Maria after reassignment.
- **TC-08 — PASS.** Ivan’s order list returned an empty array when he had no assigned work.

## Result

**Backend API — 8 PASS, 0 FAIL, 0 BLOCKED.**
