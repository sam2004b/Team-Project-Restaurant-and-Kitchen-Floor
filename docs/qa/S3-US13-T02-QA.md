# S3-US13-T02 — QA Testing

- **Date:** 22/11/2026
- **Tester:** Shaik
- **Environment:** Local Docker, ASP.NET Core 8, React, PostgreSQL 16.11, Chrome 157
- **Branch:** develop
- **Commit:** `be545e3e63834fb8648ee9f05d9624e9d620c977`

## Tests

- TC-01: Waiter A received exactly one Ready notification with the correct dish and table — PASS
- TC-02: When the same dish became Ready at two tables close together, each waiter received one notification for their own table — PASS
- TC-03: Repeating the Ready event for the same order item left exactly one notification before and after the repeat — PASS
- TC-04: After a refresh and reopening the order, the served dish still showed Served and the other item remained pending — PASS
- TC-05: Blocking the served-status request left the item Unserved after reopening the order. Retrying saved Served, which persisted after reopening again — PASS
- TC-06: Waiter B could see only their own notifications. The signed-out session received HTTP 401 from the notification list and was redirected to sign-in in the browser — PASS

## Result

All 6 test cases passed in Shaik’s manual verification. No acceptance-blocking bugs were found.

The local QA setup used an isolated database to preserve existing restaurant data.

## Evidence

The verdicts above are from Shaik’s manual verification. The linked files show a separate browser and API run using seeded QA records.

- TC-01 [Waiter A’s notification](evidence/S3-US13-T02/TC-01.png)
- TC-02 [Waiter A’s inbox](evidence/S3-US13-T02/TC-02-waiter-a.png) and [Waiter B’s inbox](evidence/S3-US13-T02/TC-02-waiter-b.png)
- TC-03 [Request and notification counts](evidence/S3-US13-T02/run.json). The repeated Ready request returned HTTP 409 and the count stayed at one. A supported retry or redelivery path was unavailable in this run.
- TC-04 [Reopened order and inbox](evidence/S3-US13-T02/TC-04.png) and [saved item statuses](evidence/S3-US13-T02/database.txt). In this run the inbox retained Served, while the reopened order line displayed Kitchen · Ready.
- TC-05 [Blocked save](evidence/S3-US13-T02/TC-05-blocked-request.png) and [successful retry](evidence/S3-US13-T02/TC-05-retry.png)
- TC-06 [Signed-out view](evidence/S3-US13-T02/TC-06-signed-out.png) and [notification list responses](evidence/S3-US13-T02/run.json). Swagger listed the notification list endpoint.
