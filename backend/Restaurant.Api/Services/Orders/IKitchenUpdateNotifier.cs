namespace Restaurant.Api.Services.Orders;

public interface IKitchenUpdateNotifier
{
    Task NotifyKitchenChangedAsync(CancellationToken cancellationToken = default);
}
