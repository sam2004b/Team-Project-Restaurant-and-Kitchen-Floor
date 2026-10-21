namespace Restaurant.Api.Services.Orders;
public interface IOrderAvailabilityService
{
    Task<OrderValidationResult> ValidateAsync(
        IReadOnlyCollection<OrderItemCandidate>? items,
        CancellationToken cancellationToken = default);
}