namespace Restaurant.Api.Models;

/// <summary>
/// Immutable financial history entry for a bill. Multiple entries allow
/// partial payments; a refund is a NEW entry, not an edit of a payment.
/// No external payment provider is integrated in this model task.
/// </summary>
public sealed class PaymentRecord
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    public int RecordedByStaffMembershipId { get; set; }
    public StaffMembership RecordedByStaffMembership { get; set; } = null!;

    public string Kind { get; set; } = PaymentRecordKinds.Payment;
    public string Method { get; set; } = PaymentRecordMethods.Cash;
    public decimal Amount { get; set; }
    public string? ExternalReference { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class PaymentRecordKinds
{
    public const string Payment = "Payment";
    public const string Refund = "Refund";
}

public static class PaymentRecordMethods
{
    public const string Cash = "Cash";
    public const string Card = "Card";
    public const string Other = "Other";
}
