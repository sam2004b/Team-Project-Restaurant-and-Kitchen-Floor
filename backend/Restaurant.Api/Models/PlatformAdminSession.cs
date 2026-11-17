using Microsoft.AspNetCore.Identity;
namespace Restaurant.Api.Models;

public sealed class PlatformAdminSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public IdentityUser User { get; set; } = null!;

    public string? SecurityStamp { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}
