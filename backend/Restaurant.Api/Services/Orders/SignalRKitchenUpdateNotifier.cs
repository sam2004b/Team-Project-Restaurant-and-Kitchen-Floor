using Microsoft.AspNetCore.SignalR;
using Restaurant.Api.Hubs;

namespace Restaurant.Api.Services.Orders;

public sealed class SignalRKitchenUpdateNotifier : IKitchenUpdateNotifier
{
    private readonly IHubContext<KitchenHub> _hub;
    private readonly ILogger<SignalRKitchenUpdateNotifier> _logger;

    public SignalRKitchenUpdateNotifier(
        IHubContext<KitchenHub> hub,
        ILogger<SignalRKitchenUpdateNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyKitchenChangedAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.All.SendCoreAsync(
                "KitchenOrdersChanged",
                Array.Empty<object>(),
                cancellationToken);
        }
        catch (Exception ex)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Unable to broadcast live kitchen order update.");
        }
    }
}
