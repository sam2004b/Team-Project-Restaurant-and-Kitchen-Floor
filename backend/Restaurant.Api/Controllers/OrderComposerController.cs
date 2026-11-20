using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Dtos.Orders;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Orders;

namespace Restaurant.Api.Controllers;

/// <summary>Waiter-only order composer. No restaurant or membership IDs are client-supplied.</summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.WaiterOnly)]
[Route("waiter")]
public sealed class OrderComposerController : ControllerBase
{
    private readonly RestaurantAccessService _access;
    private readonly OrderDraftPersistenceService _drafts;
    private readonly OrderDetailsQueryService _queries;
    private readonly IKitchenUpdateNotifier _kitchenUpdates;

    public OrderComposerController(
        RestaurantAccessService access,
        OrderDraftPersistenceService drafts,
        OrderDetailsQueryService queries,
        IKitchenUpdateNotifier kitchenUpdates)
    {
        _access = access;
        _drafts = drafts;
        _queries = queries;
        _kitchenUpdates = kitchenUpdates;
    }

    // Legacy route kept for clients that have not added per-seat selection yet.
    [HttpPost("tables/{tableId:int}/orders/open")]
    public Task<IActionResult> Open(int tableId, CancellationToken ct) =>
        OpenCore(tableId, null, ct);

    // Preferred route: each seat has its own active order, even at a shared table.
    [HttpPost("tables/{tableId:int}/seats/{seatNumber:int}/orders/open")]
    public Task<IActionResult> OpenSeat(int tableId, int seatNumber, CancellationToken ct) =>
        OpenCore(tableId, seatNumber, ct);

    private async Task<IActionResult> OpenCore(int tableId, int? seatNumber, CancellationToken ct)
    {
        if (tableId <= 0) return BadRequest(new { error = "Invalid table ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var result = await _drafts.GetOrCreateOpenAsync(
            context.RestaurantId, context.MembershipId, tableId, seatNumber, ct);
        if (result.Status == OrderWriteStatus.NotAccessible)
            return NotFound(new { error = "Assigned table not found." });
        if (result.Status is OrderWriteStatus.InvalidSeat or
            OrderWriteStatus.SeatRequired or OrderWriteStatus.AmbiguousActiveOrders)
            return WriteError(result);

        var details = await _queries.GetForWaiterAsync(
            context.RestaurantId, context.MembershipId, result.OrderId!.Value, ct);
        var body = new
        {
            orderId = result.OrderId,
            seatNumber = details?.SeatNumber,
            status = details?.Status ?? "Open",
            reused = result.Status == OrderWriteStatus.ExistingOpen
        };

        return result.Status == OrderWriteStatus.Created
            ? CreatedAtAction(nameof(Details), new { orderId = result.OrderId }, body)
            : Ok(body);
    }

    [HttpGet("orders/{orderId:int}/details")]
    public async Task<IActionResult> Details(int orderId, CancellationToken ct)
    {
        if (orderId <= 0) return BadRequest(new { error = "Invalid order ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var order = await _queries.GetForWaiterAsync(
            context.RestaurantId, context.MembershipId, orderId, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("orders/{orderId:int}/submissions")]
    public async Task<IActionResult> Submissions(int orderId, CancellationToken ct)
    {
        if (orderId <= 0) return BadRequest(new { error = "Invalid order ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;
        var submissions = await _queries.GetForWaiterSubmissionsAsync(
            context.RestaurantId, context.MembershipId, orderId, ct);
        return submissions is null ? NotFound() : Ok(submissions);
    }

    [HttpPost("orders/{orderId:int}/items")]
    public async Task<IActionResult> AddItem(
        int orderId, [FromBody] AddOrderItemRequest request, CancellationToken ct)
    {
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var result = await _drafts.AddItemAsync(
            context.RestaurantId, context.MembershipId,
            orderId, request.DishId, request.Quantity, request.SpecialRequest, ct, request.Reason);
        if (result.Status != OrderWriteStatus.ItemAdded)
            return WriteError(result);

        return CreatedAtAction(nameof(Details),
            new { orderId }, new { orderId, itemId = result.ItemId });
    }

    [HttpPut("orders/{orderId:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(
        int orderId, int itemId,
        [FromBody] UpdateOrderItemRequest request, CancellationToken ct)
    {
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var result = await _drafts.UpdateItemAsync(
            context.RestaurantId, context.MembershipId,
            orderId, itemId, request.Quantity, request.SpecialRequest, ct, request.Reason);
        return result.Status == OrderWriteStatus.ItemUpdated
            ? Ok(new { orderId, itemId, updated = true })
            : WriteError(result);
    }

    [HttpDelete("orders/{orderId:int}/items/{itemId:int}")]
    public async Task<IActionResult> RemoveItem(
        int orderId, int itemId, [FromBody] RemoveOrderItemRequest request, CancellationToken ct)
    {
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var result = await _drafts.RemoveItemAsync(
            context.RestaurantId, context.MembershipId,
            orderId, itemId, ct, request.Reason);
        return result.Status == OrderWriteStatus.ItemRemoved
            ? NoContent()
            : WriteError(result);
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int orderId, [FromBody] CancelOrderRequest request, CancellationToken ct)
    {
        if (orderId <= 0) return BadRequest(new { error = "Invalid order ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var result = await _drafts.CancelAsync(
            context.RestaurantId, context.MembershipId, orderId,
            request.Confirm, request.Reason, ct);
        return result.Status == OrderWriteStatus.OrderCancelled
            ? Ok(new { orderId, status = "Cancelled" })
            : WriteError(result);
    }

    [HttpPost("orders/{orderId:int}/send")]
    public async Task<IActionResult> Send(int orderId, CancellationToken ct)
    {
        if (orderId <= 0) return BadRequest(new { error = "Invalid order ID." });
        var context = await GetContextAsync(ct);
        if (context.Error is not null) return context.Error;

        var result = await _drafts.SendAsync(
            context.RestaurantId, context.MembershipId, orderId, ct);
        if (result.Status is not (OrderWriteStatus.Sent or OrderWriteStatus.AlreadySent))
            return WriteError(result);

        // Backwards-compatible kitchenOrder includes only submitted lines.
        // submissionId identifies the newly created (or most recent) batch.
        var kitchenOrder = await _queries.GetKitchenOrderAsync(
            context.RestaurantId, orderId, ct);
        if (result.Status == OrderWriteStatus.Sent)
        {
            await _kitchenUpdates.NotifyKitchenChangedAsync(ct);
        }
        return Ok(new
        {
            orderId,
            status = "Sent",
            alreadySent = result.Status == OrderWriteStatus.AlreadySent,
            submissionId = result.SubmissionId,
            kitchenOrder
        });
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

    private IActionResult WriteError(OrderWriteResult result) => result.Status switch
    {
        OrderWriteStatus.NotAccessible => NotFound(new { error = "Order not found or not assigned." }),
        OrderWriteStatus.ItemNotFound => NotFound(new { error = "Order item not found." }),
        OrderWriteStatus.ItemAlreadySubmitted => Conflict(new { error = "This line was already sent to the kitchen. Add a new line for extra quantity." }),
        OrderWriteStatus.InvalidItem => BadRequest(new { error = result.Error ?? "Invalid item." }),
        OrderWriteStatus.InvalidSeat => BadRequest(new { error = result.Error ?? "Invalid seat." }),
        OrderWriteStatus.SeatRequired => Conflict(new { error = result.Error }),
        OrderWriteStatus.AmbiguousActiveOrders => Conflict(new { error = result.Error }),
        OrderWriteStatus.DishUnavailable => Conflict(new { error = "Dish unavailable or missing." }),
        OrderWriteStatus.EmptyOrder => Conflict(new { error = "Cannot send an empty order." }),
        OrderWriteStatus.OrderNotOpen => Conflict(new { error = "Order is not active, paid, or already closed." }),
        OrderWriteStatus.OrderHasKitchenItems => Conflict(new { error = result.Error }),
        OrderWriteStatus.CancellationNotConfirmed => BadRequest(new { error = "Cancellation must be explicitly confirmed." }),
        OrderWriteStatus.InvalidReason => BadRequest(new { error = result.Error ?? "A reason is required." }),
        _ => StatusCode(500, new { error = "Unexpected order operation result." })
    };
}
