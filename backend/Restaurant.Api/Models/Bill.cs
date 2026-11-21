namespace Restaurant.Api.Models;

/// <summary>
/// Persisted bill for one order. Amounts are snapshots, calculated from
/// order-item price snapshots when a later billing workflow issues the bill.
/// A table can have more than one order, therefore bills are per order.
/// </summary>
public sealed class Bill
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    public int OrderId { get; set; }
    public RestaurantOrder Order { get; set; } = null!;

    // Set explicitly by the billing workflow; no assumed restaurant currency.
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal SubtotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PaymentRecord> Payments { get; set; }
        = new List<PaymentRecord>();
}

/// <summary>
/// Pure calculations for later billing services. Never use the current menu
/// price: existing order items contain the price agreed when ordered.
/// </summary>
public static class BillTotals
{
    public static decimal CalculateSubtotal(IEnumerable<RestaurantOrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        decimal subtotal = 0m;
        foreach (var item in items)
        {
            if (item.Quantity <= 0 || item.UnitPriceSnapshot <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(items),
                    "Every billed line must have positive quantity and price.");
            }

            subtotal = checked(subtotal + item.Quantity * item.UnitPriceSnapshot);
        }

        return subtotal;
    }

    public static decimal CalculateTotal(decimal subtotal, decimal tax, decimal discount)
    {
        if (subtotal < 0m || tax < 0m || discount < 0m || discount > subtotal + tax)
        {
            throw new ArgumentOutOfRangeException(nameof(discount),
                "Bill amounts must be nonnegative and the discount cannot exceed the charges.");
        }

        return checked(subtotal + tax - discount);
    }
}
