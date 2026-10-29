# US-11-T06 Manual API Testing

Date: 26 October 2026
Tester: Shaik
Environment: Local API, PostgreSQL, Swagger, and curl

## Test results

* TC-01: Created Carbonara with price 450 and four ingredients — PASS (201)
* TC-02: Rejected price of 0 — PASS (400)
* TC-03: Rejected negative price — PASS (400)
* TC-04: Rejected empty name — PASS (400)
* TC-05: Updated price to 500 and changed an ingredient — PASS (200)
* TC-06: Returned 404 for an unknown dish ID — PASS
* TC-07: Excluded an unavailable dish from the menu — PASS
* TC-08: Returned updated dish details correctly — PASS

## Summary

All eight API tests passed in Swagger and were independently verified using curl. No API defects were found.

## Limitations

Frontend testing was not performed.
