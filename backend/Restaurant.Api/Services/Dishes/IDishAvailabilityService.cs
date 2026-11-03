
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Dishes;

/// <summary>
/// Result of attempting to update a dish's availability.
/// </summary>
public enum AvailabilityUpdateResult
{
	NotFound,
	Unchanged,
	Updated
}

/// <summary>
/// Shared persistence service for dish availability.
/// </summary>
public interface IDishAvailabilityService
{
	Task<AvailabilityUpdateResult> SetAvailabilityAsync(
		int dishId,
		bool isAvailable,
		CancellationToken cancellationToken = default);
}
