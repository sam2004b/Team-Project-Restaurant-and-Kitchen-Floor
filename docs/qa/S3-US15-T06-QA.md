# S3-US15-T06 Manual Bill and Payment Management Testing

Date: 25 November 2026
Tester: Shaik
Environment: Local React waiter app, ASP.NET Core, PostgreSQL, and Chrome in an isolated setup with simulated payments\
Recorded build: `develop` at `941ef36be3ea2556ad9b9f78b2fc8ef554f1e240`

## Test results

* TC-01: Reopened bill #1 and matched the calculated line totals of 29.00 RUB for two Carbonaras and 6.50 RUB for one Vegetable Soup. The subtotal and final total were 35.50 RUB, with zero tax and discount — PASS
* TC-02: Recorded a 10.00 RUB payment and reopened the bill as Partially Paid with 25.50 RUB due. Order #1 stayed Sent and table #1 kept the same active orders — PASS
* TC-03: Paid the remaining 25.50 RUB and reloaded the bill, order, and table. The bill showed Paid with zero due, order #1 closed, and seat 1 became available. Order #2 stayed Sent on seat 2 — PASS
* TC-04: Received HTTP 400 for zero, −1.00 RUB, 35.51 RUB against 35.50 RUB due, and the unsupported Bitcoin method. The bill, payment history, order, and table stayed unchanged after each rejection — PASS
* TC-05: Retried the payment with the same reference after dropping the first response. Both API responses returned HTTP 200 and the same payment ID. The retry confirmed the payment was already recorded, and the balance fell by 10.00 RUB once — PASS
* TC-06: Reused the successful payment reference with 11.00 RUB instead of 10.00 RUB. The API returned HTTP 409 and the payment history and balance stayed unchanged — PASS
* TC-07: Interrupted the 6.50 RUB submission before it reached the API. Reload showed zero payments and unchanged order and table states. Restored the connection and retried once. One payment persisted, the balance cleared, and order #2 closed — PASS
* TC-08: Created and viewed the bill and recorded payment as the assigned waiter. The chef, unassigned waiter, and signed-out session received HTTP 403, 404, and 401 respectively, with no saved changes after any denied write — PASS

## Summary

I checked all eight cases. All eight passed, with zero failures, blocked checks, or cases left unrun. I ran the nine existing billing tests once, and all nine passed.
