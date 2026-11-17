namespace Restaurant.Api.Models;

/// <summary>Allowed kitchen preparation statuses for a single order line.</summary>
public static class DishPreparationStates
{
    public const string Pending = "Pending";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
}
