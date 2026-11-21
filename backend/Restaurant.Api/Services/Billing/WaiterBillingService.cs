using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Billing;
using Restaurant.Api.Models;

namespace Restaurant.Api.Services.Billing;

/// <summary>
/// Creates immutable bill snapshots from agreed order-line prices and records
/// append-only payments. Restaurant/table assignment is checked on every call.
/// </summary>
public sealed class WaiterBillingService
{
    private readonly RestaurantDbContext _db;

    public WaiterBillingService(RestaurantDbContext db) => _db = db;

    public async Task<BillingOperationResult> CreateBillAsync(
        int restaurantId, int waiterMembershipId, int orderId,
        string currencyCode, CancellationToken ct = default)
    {
        currencyCode = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (orderId <= 0 || currencyCode.Length != 3 ||
            currencyCode.Any(c => c < 'A' || c > 'Z'))
            return new(BillingOperationStatus.InvalidRequest,
                Error: "A valid order ID and three-letter currency code are required.");

        await using var tx = await LockRestaurantAsync(restaurantId, ct);

        var order = await GetAssignedOrderAsync(
            restaurantId, waiterMembershipId, orderId, ct);
        if (order is null)
            return new(BillingOperationStatus.NotAccessible);

        var existing = await _db.Bills.AsNoTracking()
            .AnyAsync(b => b.RestaurantId == restaurantId &&
                           b.OrderId == orderId, ct);
        if (existing)
        {
            var existingBill = await LoadBillAsync(
                restaurantId, waiterMembershipId, orderId, ct);
            await CommitAsync(tx, ct);
            return new(BillingOperationStatus.AlreadyExists, existingBill);
        }

        if (!RestaurantOrderStates.IsActive(order.Status))
            return new(BillingOperationStatus.OrderNotPayable,
                Error: "Only active orders can be billed.");

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .OrderBy(i => i.SequenceNumber)
            .ToListAsync(ct);
        if (items.Count == 0)
            return new(BillingOperationStatus.EmptyOrder,
                Error: "An empty order cannot be billed.");

        // UnitPriceSnapshot is the agreed price at order time; never read
        // current Dish.Price when calculating an existing order.
        var subtotal = decimal.Round(BillTotals.CalculateSubtotal(items), 2,
            MidpointRounding.AwayFromZero);

        // No tax/discount configuration exists in the current repository.
        // Persist explicit zeroes until a server-side pricing policy is added.
        const decimal tax = 0m;
        const decimal discount = 0m;
        var total = BillTotals.CalculateTotal(subtotal, tax, discount);

        var bill = new Bill
        {
            RestaurantId = restaurantId,
            DiningTableId = order.DiningTableId,
            OrderId = order.Id,
            CurrencyCode = currencyCode,
            SubtotalAmount = subtotal,
            TaxAmount = tax,
            DiscountAmount = discount,
            TotalAmount = total,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Bills.Add(bill);
        await _db.SaveChangesAsync(ct);
        var response = await LoadBillAsync(
            restaurantId, waiterMembershipId, orderId, ct);
        await CommitAsync(tx, ct);
        return new(BillingOperationStatus.Success, response);
    }

    public async Task<BillingOperationResult> GetBillAsync(
        int restaurantId, int waiterMembershipId, int orderId,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
            return new(BillingOperationStatus.InvalidRequest);
        var order = await GetAssignedOrderAsync(
            restaurantId, waiterMembershipId, orderId, ct);
        if (order is null)
            return new(BillingOperationStatus.NotAccessible);

        var bill = await LoadBillAsync(
            restaurantId, waiterMembershipId, orderId, ct);
        return bill is null
            ? new(BillingOperationStatus.NotFound)
            : new(BillingOperationStatus.Success, bill);
    }

    public async Task<BillingOperationResult> RecordPaymentAsync(
        int restaurantId, int waiterMembershipId, int billId,
        decimal amount, string method, string externalReference,
        CancellationToken ct = default)
    {
        method = (method ?? string.Empty).Trim();
        externalReference = (externalReference ?? string.Empty).Trim();

        if (billId <= 0 || amount <= 0m ||
            decimal.Round(amount, 2) != amount ||
            method is not (PaymentRecordMethods.Cash or
                PaymentRecordMethods.Card or PaymentRecordMethods.Other) ||
            externalReference.Length is < 1 or > 120)
            return new(BillingOperationStatus.InvalidRequest,
                Error: "Provide a positive amount (max 2 decimal places), valid method and idempotency reference.");

        await using var tx = await LockRestaurantAsync(restaurantId, ct);

        var bill = await _db.Bills.SingleOrDefaultAsync(
            b => b.Id == billId && b.RestaurantId == restaurantId, ct);
        if (bill is null)
            return new(BillingOperationStatus.NotFound);

        var order = await GetAssignedOrderAsync(
            restaurantId, waiterMembershipId, bill.OrderId, ct);
        if (order is null)
            return new(BillingOperationStatus.NotAccessible);

        // Idempotency check occurs before paid-state validation so a retry
        // after a successful payment returns the original payment result.
        var previous = await _db.PaymentRecords.AsNoTracking()
            .SingleOrDefaultAsync(p => p.RestaurantId == restaurantId &&
                p.ExternalReference == externalReference, ct);
        if (previous is not null)
        {
            if (previous.BillId != billId ||
                previous.Amount != amount ||
                previous.Method != method)
                return new(BillingOperationStatus.DuplicateReference,
                    Error: "This payment reference was already used for a different payment.");
            var priorDto = ToPaymentDto(previous);
            var priorBill = await LoadBillAsync(
                restaurantId, waiterMembershipId, bill.OrderId, ct);
            await CommitAsync(tx, ct);
            return new(BillingOperationStatus.AlreadyExists, priorBill, priorDto);
        }

        if (order.Status == RestaurantOrderStates.Closed ||
            order.PaidAtUtc is not null)
            return new(BillingOperationStatus.AlreadyPaid);

        var paid = await _db.PaymentRecords.AsNoTracking()
            .Where(p => p.RestaurantId == restaurantId &&
                        p.BillId == billId &&
                        p.Kind == PaymentRecordKinds.Payment)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var balance = bill.TotalAmount - paid;
        if (balance <= 0m)
            return new(BillingOperationStatus.AlreadyPaid);
        if (amount > balance)
            return new(BillingOperationStatus.Overpayment,
                Error: $"Payment exceeds the remaining balance of {balance:0.00}.");

        var payment = new PaymentRecord
        {
            RestaurantId = restaurantId,
            BillId = billId,
            RecordedByStaffMembershipId = waiterMembershipId,
            Kind = PaymentRecordKinds.Payment,
            Method = method,
            Amount = amount,
            ExternalReference = externalReference,
            RecordedAtUtc = DateTime.UtcNow
        };
        _db.PaymentRecords.Add(payment);

        if (amount == balance)
        {
            order.Status = RestaurantOrderStates.Closed;
            order.PaidAtUtc = payment.RecordedAtUtc;
        }

        await _db.SaveChangesAsync(ct);
        var billDto = await LoadBillAsync(
            restaurantId, waiterMembershipId, bill.OrderId, ct);
        await CommitAsync(tx, ct);
        return new(BillingOperationStatus.Success, billDto, ToPaymentDto(payment));
    }

    private async Task<RestaurantOrder?> GetAssignedOrderAsync(
        int restaurantId, int membershipId, int orderId,
        CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(
            o => o.Id == orderId && o.RestaurantId == restaurantId, ct);
        if (order is null)
            return null;

        var assigned = await _db.TableStaffAssignments.AsNoTracking()
            .AnyAsync(a => a.RestaurantId == restaurantId &&
                a.DiningTableId == order.DiningTableId &&
                a.StaffMembershipId == membershipId &&
                a.DiningTable.IsActive &&
                a.StaffMembership.RestaurantId == restaurantId &&
                a.StaffMembership.Status == MembershipStatus.Active &&
                a.StaffMembership.Role == StaffRoleNames.Waiter, ct);
        return assigned ? order : null;
    }

    private async Task<BillDetailsDto?> LoadBillAsync(
        int restaurantId, int membershipId, int orderId,
        CancellationToken ct)
    {
        var bill = await _db.Bills.AsNoTracking().SingleOrDefaultAsync(
            b => b.RestaurantId == restaurantId && b.OrderId == orderId, ct);
        if (bill is null)
            return null;

        var order = await _db.Orders.AsNoTracking().SingleAsync(
            o => o.Id == orderId && o.RestaurantId == restaurantId, ct);
        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .OrderBy(i => i.SequenceNumber)
            .Select(i => new BillLineDto(
                i.Id, i.DishNameSnapshot, i.Quantity, i.UnitPriceSnapshot,
                i.Quantity * i.UnitPriceSnapshot, i.SpecialRequest))
            .ToListAsync(ct);
        var payments = await _db.PaymentRecords.AsNoTracking()
            .Where(p => p.RestaurantId == restaurantId &&
                        p.BillId == bill.Id &&
                        p.Kind == PaymentRecordKinds.Payment)
            .OrderBy(p => p.RecordedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(ct);
        var paid = payments.Sum(p => p.Amount);
        var balance = Math.Max(0m, bill.TotalAmount - paid);
        var paymentStatus = balance == 0m ? "Paid" :
            paid > 0m ? "PartiallyPaid" : "Unpaid";

        return new BillDetailsDto(
            bill.Id, bill.OrderId, bill.DiningTableId, bill.CurrencyCode,
            bill.SubtotalAmount, bill.TaxAmount, bill.DiscountAmount,
            bill.TotalAmount, paid, balance, paymentStatus,
            bill.CreatedAtUtc, items, payments.Select(ToPaymentDto).ToList());
    }

    private static PaymentDto ToPaymentDto(PaymentRecord payment) =>
        new(payment.Id, payment.Amount, payment.Method,
            payment.ExternalReference, payment.RecordedAtUtc);

    private async Task<IDbContextTransaction?> LockRestaurantAsync(
        int restaurantId, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return null;
        var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.Restaurants.FromSqlInterpolated(
                $"SELECT * FROM restaurants WHERE \"Id\" = {restaurantId} FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private static Task CommitAsync(
        IDbContextTransaction? tx, CancellationToken ct) =>
        tx is null ? Task.CompletedTask : tx.CommitAsync(ct);
}
