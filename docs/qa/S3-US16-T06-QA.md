# S3-US16-T06 Manual Order History and Audit Trail Testing

Date: 25 November 2026
Tester: Shaik
Environment: Local React waiter app, ASP.NET Core 8, PostgreSQL 16.11, and Chrome in an isolated Docker Compose setup\
Recorded build: `develop` at `941ef36be3ea2556ad9b9f78b2fc8ef554f1e240`

## Test results

* TC-01: Completed an order, refreshed history, and reopened it. Verified the same order ID, items, and quantities, with status Closed — PASS
* TC-02: Cancelled an eligible order, refreshed history, and reopened it. Verified the Cancelled status and confirmed that the original order details and cancellation event remained available — PASS
* TC-03: Changed an order and then cancelled it. Verified that each audit entry showed the action, the staff member who performed it, and the recorded time in UTC. Checked the before and after values where available — PASS
* TC-04: Made two changes in quick succession and reloaded the order history. Verified that both entries remained distinct and in chronological order, with the correct staff member and UTC timestamp — PASS
* TC-05: Verified that the history filters included orders on the matching boundary and excluded orders outside it. Also verified that a filter with no matches showed a clear empty state — PASS
* TC-06: Interrupted a history request and verified that it showed an error. Restored the connection, retried, and confirmed that the correct order and audit history returned — PASS
* TC-07: Verified that the assigned waiter could open the history list, order details, and audit timeline. Confirmed that a restricted user and a signed-out session were denied access according to the documented rules — PASS
* TC-08: Verified that Cafe 8’s test order was absent from Bistro 21’s history while signed in to Bistro 21. Requested the known order ID through the history API and received HTTP 404 with no order details or audit entries — PASS

## Summary

All eight manual checks passed, with zero failures, blocked checks, or cases left unrun.
