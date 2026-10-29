# US-1-T06 — QA Testing

- **Date:** 29/10/2026
- **Tester:** Shaik
- **Environment:** Local API, PostgreSQL, Chrome
- **Branch:** develop
- **Commit:** 27685fc

## Tests

- TC-01: Empty menu displayed “No dishes available” with no selectable dishes — PASS
- TC-02: Exactly 12 available dishes displayed with correct names and prices — PASS
- TC-03: Menu entries appeared once without missing or duplicated dish IDs — PASS
- TC-04: Dish marked unavailable was excluded from the API menu and became unselectable on the frontend after refresh — PASS
- TC-05: Exactly 50 available dishes displayed correctly, with scrolling reaching the complete menu — PASS

## Result

All 5 test cases passed. No acceptance-blocking bugs were found.

API and frontend behavior were verified using recorded HTTP requests and browser checks. Testing used an isolated database, preserving the original restaurant data.

Mobile and cross-browser testing were not performed.
