namespace Restaurant.Api.Models;

/// <summary>
/// An immutable event describing a single order lifecycle change.
/// Before/after JSON stores values as they were when the event occurred.
/// An optional OrderItemId is historical only: deleted draft items can
/// still be referenced without a foreign key to the mutable order_items table.
/// </summary>
public sealed class OrderHistoryEvent
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    public int OrderId { get; set; }
    public int? OrderItemId { get; set; }

    public int ActorStaffMembershipId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    // Snapshots must be JSON objects when present. Creation may have no
    // previous value; cancellation may have no resulting value.
    public string? BeforeValueJson { get; set; }
    public string? AfterValueJson { get; set; }

    public RestaurantOrder Order { get; set; } = null!;
    public StaffMembership ActorStaffMembership { get; set; } = null!;
}

/// <summary>Known order history events; new types can be added in future tasks.</summary>
public static class OrderHistoryEventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string KitchenSubmitted = "KitchenSubmitted";
    public const string OrderAmended = "OrderAmended";
    public const string OrderCancelled = "OrderCancelled";
    public const string DishPreparationChanged = "DishPreparationChanged";
    public const string DishServed = "DishServed";
    public const string PaymentRecorded = "PaymentRecorded";
    public const string OrderClosed = "OrderClosed";
}
