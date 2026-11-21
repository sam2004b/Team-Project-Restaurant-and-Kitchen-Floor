using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Dtos.Billing;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Billing;

namespace Restaurant.Api.Controllers;

/// <summary>Waiter-only bill calculation and payment recording endpoints.</summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.WaiterOnly)]
[Route("waiter")]
public sealed class WaiterBillingController : ControllerBase
{
    private readonly RestaurantAccessService _access;
    private readonly WaiterBillingService _billing;

    public WaiterBillingController(
        RestaurantAccessService access,
        WaiterBillingService billing)
    {
        _access = access;
        _billing = billing;
    }

    [HttpPost("orders/{orderId:int}/bill")]
    public async Task<IActionResult> CreateBill(
        int orderId, [FromBody] CreateBillRequest request,
        CancellationToken ct)
    {
        if (orderId <= 0)
            return BadRequest(new { error = "Invalid order ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null)
            return context.Error;

        var result = await _billing.CreateBillAsync(
            context.RestaurantId, context.MembershipId, orderId,
            request.CurrencyCode, ct);
        return result.Status switch
        {
            BillingOperationStatus.Success =>
                CreatedAtAction(nameof(GetBill), new { orderId }, result.Bill),
            BillingOperationStatus.AlreadyExists =>
                Ok(new { alreadyExists = true, bill = result.Bill }),
            _ => BillingError(result)
        };
    }

    [HttpGet("orders/{orderId:int}/bill")]
    public async Task<IActionResult> GetBill(int orderId, CancellationToken ct)
    {
        if (orderId <= 0)
            return BadRequest(new { error = "Invalid order ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null)
            return context.Error;

        var result = await _billing.GetBillAsync(
            context.RestaurantId, context.MembershipId, orderId, ct);
        return result.Status == BillingOperationStatus.Success
            ? Ok(result.Bill)
            : BillingError(result);
    }

    [HttpPost("bills/{billId:int}/payments")]
    public async Task<IActionResult> RecordPayment(
        int billId, [FromBody] RecordPaymentRequest request,
        CancellationToken ct)
    {
        if (billId <= 0)
            return BadRequest(new { error = "Invalid bill ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null)
            return context.Error;

        var result = await _billing.RecordPaymentAsync(
            context.RestaurantId, context.MembershipId, billId,
            request.Amount, request.Method, request.ExternalReference, ct);
        return result.Status switch
        {
            BillingOperationStatus.Success =>
                Ok(new { payment = result.Payment, bill = result.Bill }),
            BillingOperationStatus.AlreadyExists =>
                Ok(new { alreadyRecorded = true, payment = result.Payment, bill = result.Bill }),
            _ => BillingError(result)
        };
    }

    private async Task<(int RestaurantId, int MembershipId, IActionResult? Error)>
        GetContextAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(StaffSessionClaims.RestaurantId),
                out var restaurantId) || restaurantId <= 0 ||
            !int.TryParse(User.FindFirstValue(StaffSessionClaims.MembershipId),
                out var membershipId) || membershipId <= 0)
            return (0, 0, Unauthorized());

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return (0, 0, Forbid());

        return (restaurantId, membershipId, null);
    }

    private IActionResult BillingError(BillingOperationResult result) =>
        result.Status switch
        {
            BillingOperationStatus.NotFound => NotFound(new { error = "Bill or order not found." }),
            BillingOperationStatus.NotAccessible => NotFound(new { error = "Order or bill not found for an assigned table." }),
            BillingOperationStatus.OrderNotPayable => Conflict(new { error = result.Error }),
            BillingOperationStatus.EmptyOrder => Conflict(new { error = result.Error }),
            BillingOperationStatus.InvalidRequest => BadRequest(new { error = result.Error ?? "Invalid billing request." }),
            BillingOperationStatus.AlreadyPaid => Conflict(new { error = "This order has already been paid." }),
            BillingOperationStatus.Overpayment => BadRequest(new { error = result.Error }),
            BillingOperationStatus.DuplicateReference => Conflict(new { error = result.Error }),
            _ => StatusCode(500, new { error = "Unexpected billing operation result." })
        };
}
