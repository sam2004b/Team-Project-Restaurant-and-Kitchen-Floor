using System.ComponentModel.DataAnnotations;

namespace Restaurant.Api.Dtos.Administration;

public sealed class LoginRequest
{
    [Required] public string? Username { get; set; }
    [Required] public string? Password { get; set; }
}

public sealed class CreateRestaurantRequest
{
    [Required, MaxLength(150)] public string? Name { get; set; }
    [Required, MaxLength(300)] public string? Address { get; set; }
}

public sealed class InviteStaffRequest
{
    [Required, EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [Required] public string? Role { get; set; }
}

public sealed class ChangeStaffRoleRequest
{
    [Required] public string? Role { get; set; }
}

public sealed class AcceptInvitationRequest
{
    [Required] public string? Token { get; set; }
    // Required only if the invited person does not already have an Identity account.
    public string? Username { get; set; }
    public string? Password { get; set; }
}
