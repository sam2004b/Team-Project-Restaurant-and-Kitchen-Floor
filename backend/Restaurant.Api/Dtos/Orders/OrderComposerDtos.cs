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
}

public sealed class UpdateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [MaxLength(500)]
    public string? SpecialRequest { get; set; }
}

public sealed record OrderItemDetailsDto(
    int Id,
    int DishId,
    string DishName,
    decimal UnitPrice,
    int Quantity,
    string? SpecialRequest,
    int? KitchenSubmissionId = null);

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
    int? SeatNumber = null);
