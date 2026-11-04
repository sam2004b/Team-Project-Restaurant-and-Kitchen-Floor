namespace Restaurant.Api.Dtos.Settings;

// Nullable value types let the controller distinguish omitted values
// from valid explicit values such as IsActive=false.
public sealed record UpdateRestaurantProfileRequest(
    string? Name,
    string? Address);

public sealed record SaveRestaurantSettingsRequest(
    int? DefaultSeatCapacity);

public sealed record CreateDiningTableRequest(
    int? TableNumber,
    int? SeatCount,
    bool? IsActive);

public sealed record UpdateDiningTableRequest(
    int? TableNumber,
    int? SeatCount,
    bool? IsActive);

public sealed record SaveTableAssignmentRequest(
    int? StaffMembershipId);

// Stable API response types: no EF entities or sensitive Identity details.
public sealed record RestaurantSettingsView(
    int RestaurantId,
    int DefaultSeatCapacity,
    DateTime UpdatedAtUtc);

public sealed record StaffAssignmentView(
    int Id,
    int StaffMembershipId,
    string? UserName,
    string Role,
    string MembershipStatus,
    DateTime AssignedAtUtc);

public sealed record DiningTableView(
    int Id,
    int RestaurantId,
    int TableNumber,
    int SeatCount,
    bool IsActive,
    StaffAssignmentView? Assignment);
