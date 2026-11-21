using System.ComponentModel.DataAnnotations;

namespace Restaurant.Api.Dtos.Billing;

public sealed class CreateBillRequest
{
    [Required, RegularExpression("^[A-Za-z]{3}$")]
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class RecordPaymentRequest
{
    [Range(typeof(decimal), "0.01", "9999999999.99")]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^(Cash|Card|Other)$")]
    public string Method { get; set; } = "Cash";

    // Required idempotency key: retries with the same key do not charge twice.
    [Required, StringLength(120, MinimumLength = 1)]
    public string ExternalReference { get; set; } = string.Empty;
}

public sealed record BillLineDto(
    int OrderItemId,
    string DishName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? SpecialRequest);

public sealed record PaymentDto(
    long Id,
    decimal Amount,
    string Method,
    string? ExternalReference,
    DateTime RecordedAtUtc);

public sealed record BillDetailsDto(
    int Id,
    int OrderId,
    int DiningTableId,
    string CurrencyCode,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal BalanceDue,
    string PaymentStatus,
    DateTime CreatedAtUtc,
    IReadOnlyList<BillLineDto> Items,
    IReadOnlyList<PaymentDto> Payments);

public enum BillingOperationStatus
{
    Success,
    AlreadyExists,
    NotFound,
    NotAccessible,
    OrderNotPayable,
    EmptyOrder,
    InvalidRequest,
    AlreadyPaid,
    Overpayment,
    DuplicateReference
}

public sealed record BillingOperationResult(
    BillingOperationStatus Status,
    BillDetailsDto? Bill = null,
    PaymentDto? Payment = null,
    string? Error = null);
