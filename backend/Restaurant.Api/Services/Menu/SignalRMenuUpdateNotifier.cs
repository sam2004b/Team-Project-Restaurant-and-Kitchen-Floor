
using Microsoft.AspNetCore.SignalR;
using Restaurant.Api.Hubs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Menu;

public sealed class SignalRMenuUpdateNotifier
    : IMenuUpdateNotifier
{
    private readonly IHubContext<MenuHub> _hub;
    private readonly ILogger<SignalRMenuUpdateNotifier> _logger;

    public SignalRMenuUpdateNotifier(
        IHubContext<MenuHub> hub,
        ILogger<SignalRMenuUpdateNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyMenuChangedAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.All.SendCoreAsync(
                "MenuChanged",
                Array.Empty<object>(),
                cancellationToken);
        }
        catch (Exception ex)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Unable to broadcast live menu update.");
        }
    }
}
