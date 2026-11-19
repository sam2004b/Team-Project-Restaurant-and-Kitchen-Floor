namespace Restaurant.Api.Models;

/// <summary>
/// Durable waiter inbox entry for one ready dish line. The recipient is the
/// waiter assigned when the notification is created; table reassignment does
/// not silently rewrite notification history.
/// </summary>
public sealed class WaiterNotification
{
    public int Id { get; set; }

    // Composite foreign keys enforce that order, table, item, and waiter
    // belong to the same restaurant/order. Never trust client-supplied IDs.
    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    public int OrderId { get; set; }
    public int OrderItemId { get; set; }
    public int WaiterStaffMembershipId { get; set; }

    public RestaurantOrder Order { get; set; } = null!;
    public RestaurantOrderItem OrderItem { get; set; } = null!;
    public StaffMembership WaiterStaffMembership { get; set; } = null!;

    // ReadAtUtc is the single source of truth for unread/read state.
    // This does not assert successful push delivery to a device.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
    public bool IsRead => ReadAtUtc.HasValue;
}
