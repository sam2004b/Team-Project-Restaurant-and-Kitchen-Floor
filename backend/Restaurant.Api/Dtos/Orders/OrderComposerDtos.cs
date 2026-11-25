using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Restaurant.Api.Dtos.Orders;

public sealed class AddOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int DishId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [MaxLength(500)]
    public string? SpecialRequest { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class UpdateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [MaxLength(500)]
    public string? SpecialRequest { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class RemoveOrderItemRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class CancelOrderRequest
{
    public bool Confirm { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record OrderItemDetailsDto(
    int Id,
    int DishId,
    string DishName,
    decimal UnitPrice,
    int Quantity,
    string? SpecialRequest,
    int? KitchenSubmissionId = null,
    string PreparationStatus = "New",
    DateTime? PreparationStartedAtUtc = null,
    DateTime? ReadyAtUtc = null,
    string ServiceStatus = "Unserved",
    DateTime? ServedAtUtc = null);

public sealed record OrderDetailsDto(
    int Id,
    int RestaurantId,
    int DiningTableId,
    int CreatedByStaffMembershipId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    IReadOnlyList<OrderItemDetailsDto> Items,
    DateTime? PaidAtUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? SeatNumber = null);

public sealed record KitchenSubmissionDto(
    int Id,
    int OrderId,
    int DiningTableId,
    DateTime SentAtUtc,
    IReadOnlyList<OrderItemDetailsDto> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? SeatNumber = null,
    // Public number configured by the restaurant administrator (not the PK).
    int? TableNumber = null);
