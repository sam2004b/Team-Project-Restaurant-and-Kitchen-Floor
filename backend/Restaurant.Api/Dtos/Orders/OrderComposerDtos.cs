using System.ComponentModel.DataAnnotations;

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
    string? SpecialRequest);

public sealed record OrderDetailsDto(
    int Id,
    int RestaurantId,
    int DiningTableId,
    int CreatedByStaffMembershipId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    IReadOnlyList<OrderItemDetailsDto> Items);
