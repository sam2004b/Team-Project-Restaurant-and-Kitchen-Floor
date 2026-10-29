# US-3-T06 — QA Testing

**Date:** 29/10/2026  
**Tester:** Shaik  
**Environment:** Local API, isolated PostgreSQL database, Chrome  
**Branch:** qa/US-1-T06  


## Scope

Customer menu availability only. Customer ordering and order validation are outside the confirmed US-3 scope.

## Tests

- TC-01: Available dish appeared in the customer menu — PASS
- TC-02: Unavailable dish was excluded from the available menu — PASS
- TC-03: Dish marked unavailable became clearly unavailable after refresh — PASS
- TC-04: Restored dish reappeared as available after refresh — PASS

## Result

Totals: **4 PASS, 0 FAIL, 0 BLOCKED**. No functional blockers remain within the confirmed scope.

API and browser checks used an isolated QA database. Original restaurant data remained unchanged.

