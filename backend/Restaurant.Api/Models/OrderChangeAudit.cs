namespace Restaurant.Api.Models;

/// <summary>
/// Durable record of an amendment or cancellation. Original item IDs are
/// historical identifiers, not foreign keys: draft lines may be deleted.
/// Future APIs must insert this record in the same transaction as the change.
/// </summary>
public sealed class OrderChangeAudit
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    public int OrderId { get; set; }

    // Null means an order-level change; otherwise identifies the affected line.
    // No FK to order_items, so deletion never destroys the original record.
    public int? OrderItemId { get; set; }

    // Actual employee performing the operation (not necessarily order creator).
    public int ChangedByStaffMembershipId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    // JSONB snapshots include the order header and every original dish line.
    // A cancelled order/line has no after snapshot.
    public string BeforeSnapshotJson { get; set; } = string.Empty;
    public string? AfterSnapshotJson { get; set; }

    public RestaurantOrder Order { get; set; } = null!;
    public StaffMembership ChangedByStaffMembership { get; set; } = null!;
}

public static class OrderChangeTypes
{
    public const string Amended = "Amended";
    public const string Cancelled = "Cancelled";
}
