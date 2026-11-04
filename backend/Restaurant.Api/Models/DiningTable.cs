
namespace Restaurant.Api.Models;

/// <summary>
/// A numbered dining table belonging to a restaurant.
/// </summary>
public class DiningTable
{
    public int Id { get; set; }

    public int RestaurantId { get; set; }

    public RestaurantAccount Restaurant { get; set; }
        = null!;

    public int TableNumber { get; set; }

    public int SeatCount { get; set; }

    // Whether this table is configured for service.
    public bool IsActive { get; set; } = true;

    // Optional current employee assignment.
    public TableStaffAssignment? StaffAssignment
    { get; set; }
}
