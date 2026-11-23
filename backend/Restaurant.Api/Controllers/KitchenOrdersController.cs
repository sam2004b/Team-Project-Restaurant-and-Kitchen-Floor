using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Orders;

namespace Restaurant.Api.Controllers;

/// <summary>
/// Durable Chef-only kitchen feed backed by committed submissions.
/// Reconnecting clients can recover missed batches, even after payment.
/// </summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.ChefOnly)]
[Route("chef/orders")]
public sealed class KitchenOrdersController : ControllerBase
{
    public sealed class UpdateKitchenItemStatusRequest
    {
        public string? Status { get; set; }
    }
    private readonly RestaurantAccessService _access;
    private readonly OrderDetailsQueryService _orders;
    private readonly RestaurantDbContext _db;
    private readonly IKitchenUpdateNotifier _kitchenUpdates;

    public KitchenOrdersController(
        RestaurantAccessService access,
        OrderDetailsQueryService orders,
        RestaurantDbContext db,
        IKitchenUpdateNotifier kitchenUpdates)
    {
        _access = access;
        _orders = orders;
        _db = db;
        _kitchenUpdates = kitchenUpdates;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;
        return Ok(await _orders.GetKitchenFeedAsync(restaurantId, ct));
    }

    // Batch feed for kitchen UI: each batch contains only newly sent items.
    [HttpGet("submissions")]
    public async Task<IActionResult> Submissions(CancellationToken ct)
    {
        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;
        return Ok(await _orders.GetKitchenSubmissionsAsync(restaurantId, ct: ct));
    }

    [HttpGet("{orderId:int}/submissions")]
    public async Task<IActionResult> OrderSubmissions(int orderId, CancellationToken ct)
    {
        if (orderId <= 0) return BadRequest(new { error = "Invalid order ID." });
        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;
        return Ok(await _orders.GetKitchenSubmissionsAsync(restaurantId, orderId, ct));
    }

    /// <summary>Advance one submitted dish line through its kitchen preparation states.</summary>
    [HttpPatch("items/{orderItemId:int}/status")]
    public async Task<IActionResult> UpdateItemStatus(
        int orderItemId,
        [FromBody] UpdateKitchenItemStatusRequest request,
        CancellationToken ct)
    {
        if (orderItemId <= 0)
            return BadRequest(new { error = "Invalid order item ID." });

        var requestedStatus = request.Status?.Trim();
        if (requestedStatus is not ("Preparing" or "Ready"))
            return BadRequest(new
            {
                error = "Status must be Preparing or Ready. New is the initial status."
            });

        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;

        // Only committed/submitted lines are visible to the kitchen and may advance.
        var item = await _db.OrderItems
            .Include(i => i.Order)
            .SingleOrDefaultAsync(i =>
                i.Id == orderItemId &&
                i.Order.RestaurantId == restaurantId &&
                i.KitchenSubmissionId != null, ct);

        if (item is null) return NotFound();

        var now = DateTime.UtcNow;
        if (item.PreparationStatus == DishPreparationStates.Pending &&
            requestedStatus == "Preparing")
        {
            item.PreparationStatus = DishPreparationStates.Preparing;
            item.PreparationStartedAtUtc = now;
        }
        else if (item.PreparationStatus == DishPreparationStates.Preparing &&
                 requestedStatus == "Ready")
        {
            item.PreparationStatus = DishPreparationStates.Ready;
            item.ReadyAtUtc = now;

            // Create one durable notification for the waiter currently
            // assigned to this table. The unique database index on
            // (OrderItemId, WaiterStaffMembershipId) prevents duplicates.
            var assignedWaiterId = await _db.TableStaffAssignments
                .Where(a =>
                    a.RestaurantId == restaurantId &&
                    a.DiningTableId == item.Order.DiningTableId &&
                    a.StaffMembership.RestaurantId == restaurantId &&
                    a.StaffMembership.Status == MembershipStatus.Active &&
                    a.StaffMembership.Role == StaffRoleNames.Waiter &&
                    a.DiningTable.RestaurantId == restaurantId &&
                    a.DiningTable.IsActive)
                .Select(a => (int?)a.StaffMembershipId)
                .SingleOrDefaultAsync(ct);

            if (assignedWaiterId.HasValue)
            {
                bool alreadyNotified = await _db.WaiterNotifications.AnyAsync(n =>
                    n.OrderItemId == item.Id &&
                    n.WaiterStaffMembershipId == assignedWaiterId.Value, ct);

                if (!alreadyNotified)
                {
                    _db.WaiterNotifications.Add(new WaiterNotification
                    {
                        RestaurantId = restaurantId,
                        DiningTableId = item.Order.DiningTableId,
                        OrderId = item.OrderId,
                        OrderItemId = item.Id,
                        WaiterStaffMembershipId = assignedWaiterId.Value,
                        CreatedAtUtc = now
                    });
                }
            }
        }
        else
        {
            return Conflict(new
            {
                error = $"Invalid preparation status transition from {ToKitchenStatus(item.PreparationStatus)} to {requestedStatus}."
            });
        }

        // Persist an immutable lifecycle event in the same SaveChanges call
        // as the preparation-state mutation.
        if (!int.TryParse(User.FindFirstValue(StaffSessionClaims.MembershipId), out var actorId) || actorId <= 0)
            return Unauthorized();
        _db.OrderHistoryEvents.Add(new OrderHistoryEvent
        {
            RestaurantId = restaurantId,
            DiningTableId = item.Order.DiningTableId,
            OrderId = item.OrderId,
            OrderItemId = item.Id,
            ActorStaffMembershipId = actorId,
            EventType = OrderHistoryEventTypes.DishPreparationChanged,
            OccurredAtUtc = now,
            BeforeValueJson = JsonSerializer.Serialize(new { status = requestedStatus == "Ready" ? "Preparing" : "New" }),
            AfterValueJson = JsonSerializer.Serialize(new { status = requestedStatus })
        });

        await _db.SaveChangesAsync(ct);
        await _kitchenUpdates.NotifyKitchenChangedAsync(ct);

        return Ok(new
        {
            item.Id,
            item.OrderId,
            item.DishId,
            status = ToKitchenStatus(item.PreparationStatus),
            item.PreparationStartedAtUtc,
            item.ReadyAtUtc
        });
    }

    private static string ToKitchenStatus(string status) =>
        status == DishPreparationStates.Pending ? "New" : status;

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Details(int orderId, CancellationToken ct)
    {
        if (orderId <= 0)
            return BadRequest(new { error = "Invalid order ID." });

        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;
        var order = await _orders.GetKitchenOrderAsync(restaurantId, orderId, ct);
        return order is null ? NotFound() : Ok(order);
    }

    private async Task<(int RestaurantId, IActionResult? Error)>
        GetAuthorizedRestaurantAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(StaffSessionClaims.RestaurantId),
                out int restaurantId) || restaurantId <= 0)
            return (0, Unauthorized());

        if (!await _access.CanChangeDishAvailabilityAsync(User, restaurantId, ct))
            return (0, Forbid());

        return (restaurantId, null);
    }
}
