# S3-US14-T06 Manual Order Modification and Cancellation Testing

Date: 24 November 2026\
Tester: Shaik\
Environment: Local React waiter and kitchen apps, ASP.NET Core, PostgreSQL, and Chrome\
Recorded build: `develop` at `bac527257bbdca51a46ddf6eaba97902f4cf2dbd`

## Test results

* TC-01: Added a second dish, changed its quantity, and removed it. The original dish stayed unchanged — PASS
* TC-02: Saved quantity 1, rejected quantity 0, and verified the order kept its prior saved state after reopening — PASS
* TC-03: Dismissed cancellation and verified the order stayed active. Confirmed cancellation and reopened the order as Cancelled — PASS
* TC-04: Verified the paid order rejected add, update, remove, and cancel and kept its saved items, payment, and status — PASS
* TC-05: Verified modification and cancellation permissions for the assigned waiter, restricted users, and a signed-out session — PASS
* TC-06: Verified the kitchen view synchronised with order edits and cancellation — PASS
* TC-07: Retried the same add and verified one intended addition in the saved order and kitchen — PASS
* TC-08: Repeated cancellation and verified one final cancellation with no repeated kitchen effect — PASS

## Summary

I manually checked all eight cases. All eight passed, with zero failures, blocked checks, or cases left unrun.

[Earlier QA evidence](evidence/S3-US14-T06/) documents the recorded build. These files come from the earlier run.
