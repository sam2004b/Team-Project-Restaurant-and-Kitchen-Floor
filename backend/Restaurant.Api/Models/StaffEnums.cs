
namespace Restaurant.Api.Models;

public static class StaffRoleNames
{
    public const string Administrator = "Administrator";
    public const string Waiter = "Waiter";
    public const string Chef = "Chef";
    public const string ReceptionManager = "Reception Manager";
}

public enum MembershipStatus
{
    Active = 1,
    Revoked = 2
}

public enum InvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3
}
