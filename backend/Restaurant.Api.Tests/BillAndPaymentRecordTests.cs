using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Xunit;

namespace Restaurant.Api.Tests;

/// <summary>S3-US15-T01: per-order bills and immutable payment history.</summary>
public sealed class BillAndPaymentRecordTests
{
    private static DbContextOptions<RestaurantDbContext> NewOptions() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("bill-payment-" + Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public void BillSubtotalUsesStoredOrderPricesAndQuantities()
    {
        var lines = new[]
        {
            new RestaurantOrderItem { OrderId = 42, Quantity = 2, UnitPriceSnapshot = 12.00m },
            new RestaurantOrderItem { OrderId = 42, Quantity = 1, UnitPriceSnapshot = 4.00m }
        };
        var subtotal = BillTotals.CalculateSubtotal(lines);

        // The priced bill is a snapshot; later menu changes have no effect.
        lines[0].UnitPriceSnapshot = 15.00m;
        Assert.Equal(28.00m, subtotal);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillTotals.CalculateSubtotal(new[]
            {
                new RestaurantOrderItem { Quantity = 0, UnitPriceSnapshot = 5m }
            }));
    }

    [Fact]
    public void BillTotalIncludesTaxAndDiscountAndRejectsInvalidAmounts()
    {
        Assert.Equal(27.80m, BillTotals.CalculateTotal(28.00m, 2.80m, 3.00m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillTotals.CalculateTotal(10m, 0m, 11m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillTotals.CalculateTotal(-10m, 0m, 0m));
    }

    [Fact]
    public async Task SeparatePaymentsPersistWithTheirOriginalMethodsAndReferences()
    {
        var options = NewOptions();
        int billId;
        await using (var db = new RestaurantDbContext(options))
        {
            var bill = new Bill
            {
                RestaurantId = 11, DiningTableId = 21, OrderId = 42,
                CurrencyCode = "USD", SubtotalAmount = 28.00m,
                TaxAmount = 2.80m, DiscountAmount = 3.00m, TotalAmount = 27.80m
            };
            db.Bills.Add(bill);
            await db.SaveChangesAsync();
            billId = bill.Id;
            db.PaymentRecords.AddRange(
                new PaymentRecord
                {
                    RestaurantId = 11, BillId = billId,
                    RecordedByStaffMembershipId = 51,
                    Amount = 15.00m, Method = PaymentRecordMethods.Cash,
                    RecordedAtUtc = new DateTime(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc)
                },
                new PaymentRecord
                {
                    RestaurantId = 11, BillId = billId,
                    RecordedByStaffMembershipId = 51,
                    Amount = 12.80m, Method = PaymentRecordMethods.Card,
                    ExternalReference = "test-card-txn-1",
                    RecordedAtUtc = new DateTime(2026, 11, 21, 11, 2, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        await using var verify = new RestaurantDbContext(options);
        var payments = await verify.PaymentRecords.AsNoTracking()
            .Where(p => p.RestaurantId == 11 && p.BillId == billId)
            .OrderBy(p => p.RecordedAtUtc).ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.Equal(15.00m, payments[0].Amount);
        Assert.Equal(PaymentRecordMethods.Cash, payments[0].Method);
        Assert.Equal(12.80m, payments[1].Amount);
        Assert.Equal("test-card-txn-1", payments[1].ExternalReference);
        Assert.Equal(27.80m, payments.Sum(p => p.Amount));
        Assert.Equal(27.80m, (await verify.Bills.SingleAsync(b => b.Id == billId)).TotalAmount);
    }

    [Fact]
    public async Task RefundIsAnAdditionalRecordNotAnOverwriteOfThePayment()
    {
        var options = NewOptions();
        await using (var db = new RestaurantDbContext(options))
        {
            db.PaymentRecords.Add(new PaymentRecord
            {
                RestaurantId = 11, BillId = 1, RecordedByStaffMembershipId = 51,
                Amount = 20m, Kind = PaymentRecordKinds.Payment,
                Method = PaymentRecordMethods.Card
            });
            db.PaymentRecords.Add(new PaymentRecord
            {
                RestaurantId = 11, BillId = 1, RecordedByStaffMembershipId = 51,
                Amount = 5m, Kind = PaymentRecordKinds.Refund,
                Method = PaymentRecordMethods.Card
            });
            await db.SaveChangesAsync();
        }
        await using var verify = new RestaurantDbContext(options);
        var records = await verify.PaymentRecords.AsNoTracking().ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Kind == PaymentRecordKinds.Payment && r.Amount == 20m);
        Assert.Contains(records, r => r.Kind == PaymentRecordKinds.Refund && r.Amount == 5m);
        Assert.Equal(15m, records.Sum(r =>
            r.Kind == PaymentRecordKinds.Refund ? -r.Amount : r.Amount));
    }

    [Fact]
    public void SchemaDefinesOneBillPerOrderAndTenantSafePaymentHistory()
    {
        using var db = new RestaurantDbContext(NewOptions());
        // Check constraints live in the design-time model, not EF's optimized runtime model.
        var model = db.GetService<IDesignTimeModel>().Model;
        var bill = model.FindEntityType(typeof(Bill))!;
        var payment = model.FindEntityType(typeof(PaymentRecord))!;

        Assert.Contains(bill.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrder) &&
            fk.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "RestaurantId", "DiningTableId", "OrderId" }));
        Assert.Contains(bill.GetIndexes(), i => i.IsUnique &&
            i.GetDatabaseName() == "UX_bills_restaurant_order");
        Assert.Contains(bill.GetCheckConstraints(), c =>
            c.Name == "CK_bills_total_consistency");
        Assert.Contains(bill.GetCheckConstraints(), c =>
            c.Name == "CK_bills_currency_code");
        Assert.Contains(payment.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Bill) &&
            fk.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "RestaurantId", "BillId" }));
        Assert.Contains(payment.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(StaffMembership) &&
            fk.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "RestaurantId", "RecordedByStaffMembershipId" }));
        Assert.Contains(payment.GetCheckConstraints(), c =>
            c.Name == "CK_payment_records_amount_positive");
        Assert.Contains(payment.GetIndexes(), i =>
            i.GetDatabaseName() == "IX_payment_records_bill_history");
        Assert.Equal(12, bill.FindProperty("TotalAmount")!.GetPrecision());
    }
}
