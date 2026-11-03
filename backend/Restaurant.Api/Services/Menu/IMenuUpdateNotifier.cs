
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Menu;

public interface IMenuUpdateNotifier
{
    Task NotifyMenuChangedAsync(
        CancellationToken cancellationToken = default);
}
