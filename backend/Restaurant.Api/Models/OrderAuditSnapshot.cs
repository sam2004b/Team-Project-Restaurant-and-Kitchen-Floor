using System.Text.Json;

namespace Restaurant.Api.Models;

/// <summary>Immutable copy of an order line, including the kitchen and price details.</summary>
public sealed record OrderAuditItemSnapshot(
    int Id,
    int DishId,
    int SequenceNumber,
    int Quantity,
    string DishNameSnapshot,
    decimal UnitPriceSnapshot,
    string? SpecialRequest,
    int? KitchenSubmissionId,
    string PreparationStatus,
    DateTime? PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    string ServiceStatus,
    DateTime? ServedAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// Original order details captured BEFORE a future amendment/cancellation.
/// Pass all lines explicitly: callers must query them before mutating anything.
/// </summary>
public sealed record OrderAuditSnapshot(
    int Id,
    int RestaurantId,
    int DiningTableId,
    int? SeatNumber,
    int CreatedByStaffMembershipId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? PaidAtUtc,
    IReadOnlyList<OrderAuditItemSnapshot> Items)
{
    public static OrderAuditSnapshot Capture(
        RestaurantOrder order,
        IEnumerable<RestaurantOrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(items);

        var lines = items.OrderBy(i => i.SequenceNumber).ThenBy(i => i.Id)
            .Select(i =>
            {
                if (i.OrderId != order.Id)
                    throw new ArgumentException(
                        "Every snapshot line must belong to the specified order.",
                        nameof(items));

                return new OrderAuditItemSnapshot(
                    i.Id, i.DishId, i.SequenceNumber, i.Quantity,
                    i.DishNameSnapshot, i.UnitPriceSnapshot, i.SpecialRequest,
                    i.KitchenSubmissionId, i.PreparationStatus,
                    i.PreparationStartedAtUtc, i.ReadyAtUtc,
                    i.ServiceStatus, i.ServedAtUtc, i.CreatedAtUtc);
            }).ToArray();

        return new OrderAuditSnapshot(
            order.Id, order.RestaurantId, order.DiningTableId,
            order.SeatNumber, order.CreatedByStaffMembershipId,
            order.Status, order.CreatedAtUtc, order.SentAtUtc, order.PaidAtUtc,
            lines);
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
