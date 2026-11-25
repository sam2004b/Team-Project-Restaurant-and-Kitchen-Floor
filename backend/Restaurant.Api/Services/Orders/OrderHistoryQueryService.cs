
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;

namespace Restaurant.Api.Services.Orders;

public sealed class OrderHistoryQueryService
{
    private readonly RestaurantDbContext _db;

    public OrderHistoryQueryService(RestaurantDbContext db)
    {
        _db = db;
    }

    public async Task<object> SearchAsync(
        int restaurantId,
        int membershipId,
        bool isAdministrator,
        int page = 1,
        int pageSize = 25,
        int? tableNumber = null,
        string? status = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? search = null,
        int? createdByStaffMembershipId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Orders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId);

        // Administrators can view all historical orders in their restaurant.
        // Waiters can view orders they created or orders on currently
        // assigned tables.
        if (!isAdministrator)
        {
            query = query.Where(o =>
                o.CreatedByStaffMembershipId == membershipId ||
                _db.TableStaffAssignments.Any(a =>
                    a.RestaurantId == restaurantId &&
                    a.DiningTableId == o.DiningTableId &&
                    a.StaffMembershipId == membershipId &&
                    a.StaffMembership.Status == MembershipStatus.Active &&
                    a.StaffMembership.Role == StaffRoleNames.Waiter &&
                    a.DiningTable.IsActive));
        }

        if (createdByStaffMembershipId.HasValue && isAdministrator)
        {
            query = query.Where(o =>
                o.CreatedByStaffMembershipId == createdByStaffMembershipId.Value &&
                o.CreatedByStaffMembership.RestaurantId == restaurantId);
        }

        if (tableNumber.HasValue)
        {
            query = query.Where(o =>
                o.DiningTable.TableNumber == tableNumber.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            query = query.Where(o => o.Status == normalizedStatus);
        }

        if (fromUtc.HasValue)
            query = query.Where(o => o.CreatedAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(o => o.CreatedAtUtc <= toUtc.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (!int.TryParse(search.Trim(), out var orderId))
                return new
                {
                    page,
                    pageSize,
                    totalCount = 0,
                    items = Array.Empty<object>()
                };

            query = query.Where(o => o.Id == orderId);
        }

        var totalCount = await query.CountAsync(ct);

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.DiningTableId,
                tableNumber = o.DiningTable.TableNumber,
                o.SeatNumber,
                o.Status,
                o.CreatedByStaffMembershipId,
                createdByStaffName = o.CreatedByStaffMembership.User.UserName,
                o.CreatedAtUtc,
                o.SentAtUtc,
                o.PaidAtUtc
            })
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.Id).ToList();

        var bills = await _db.Bills
            .AsNoTracking()
            .Where(b => b.RestaurantId == restaurantId &&
                        orderIds.Contains(b.OrderId))
            .ToDictionaryAsync(b => b.OrderId, ct);

        var items = await _db.OrderItems
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .GroupBy(i => i.OrderId)
            .Select(g => new
            {
                OrderId = g.Key,
                ItemCount = g.Count(),
                TotalQuantity = g.Sum(i => i.Quantity)
            })
            .ToDictionaryAsync(x => x.OrderId, ct);

        var results = orders.Select(o =>
        {
            bills.TryGetValue(o.Id, out var bill);
            items.TryGetValue(o.Id, out var itemSummary);

            return new
            {
                id = o.Id,
                o.DiningTableId,
                o.tableNumber,
                o.SeatNumber,
                o.Status,
                o.CreatedByStaffMembershipId,
                o.createdByStaffName,
                o.CreatedAtUtc,
                o.SentAtUtc,
                o.PaidAtUtc,
                itemCount = itemSummary?.ItemCount ?? 0,
                totalQuantity = itemSummary?.TotalQuantity ?? 0,
                totalAmount = bill?.TotalAmount,
                currencyCode = bill?.CurrencyCode,
                paymentStatus = bill is null
                    ? "NotBilled"
                    : o.PaidAtUtc.HasValue ? "Paid" : "Unpaid"
            };
        }).ToList();

        return new
        {
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            items = results
        };
    }

    public async Task<object?> GetDetailsAsync(
        int restaurantId,
        int membershipId,
        bool isAdministrator,
        int orderId,
        CancellationToken ct = default)
    {
        var query = _db.Orders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId &&
                        o.Id == orderId);

        if (!isAdministrator)
        {
            query = query.Where(o =>
                o.CreatedByStaffMembershipId == membershipId ||
                _db.TableStaffAssignments.Any(a =>
                    a.RestaurantId == restaurantId &&
                    a.DiningTableId == o.DiningTableId &&
                    a.StaffMembershipId == membershipId &&
                    a.StaffMembership.Status == MembershipStatus.Active &&
                    a.StaffMembership.Role == StaffRoleNames.Waiter &&
                    a.DiningTable.IsActive));
        }

        var order = await query
            .Select(o => new
            {
                o.Id,
                o.RestaurantId,
                o.DiningTableId,
                tableNumber = o.DiningTable.TableNumber,
                o.SeatNumber,
                o.Status,
                o.CreatedByStaffMembershipId,
                createdByStaffName = o.CreatedByStaffMembership.User.UserName,
                o.CreatedAtUtc,
                o.SentAtUtc,
                o.PaidAtUtc
            })
            .SingleOrDefaultAsync(ct);

        if (order is null)
            return null;

        var orderItems = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.OrderId == orderId &&
                        i.Order.RestaurantId == restaurantId)
            .OrderBy(i => i.SequenceNumber)
            .ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                i.DishId,
                dishName = i.DishNameSnapshot,
                i.Quantity,
                i.UnitPriceSnapshot,
                lineTotal = i.Quantity * i.UnitPriceSnapshot,
                i.SpecialRequest,
                i.KitchenSubmissionId,
                i.PreparationStatus,
                i.PreparationStartedAtUtc,
                i.ReadyAtUtc,
                i.ServiceStatus,
                i.ServedAtUtc
            })
            .ToListAsync(ct);

        var historyEvents = await _db.OrderHistoryEvents
            .AsNoTracking()
            .Where(e => e.RestaurantId == restaurantId &&
                        e.OrderId == orderId)
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.EventType,
                e.OccurredAtUtc,
                actorStaffMembershipId = (int?)e.ActorStaffMembershipId,
                e.OrderItemId,
                e.BeforeValueJson,
                e.AfterValueJson,
                source = "OrderHistoryEvent"
            })
            .ToListAsync(ct);

        var auditEvents = await _db.OrderChangeAudits
            .AsNoTracking()
            .Where(a => a.RestaurantId == restaurantId &&
                        a.OrderId == orderId)
            .OrderBy(a => a.ChangedAtUtc)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                id = a.Id,
                eventType = a.ChangeType,
                OccurredAtUtc = a.ChangedAtUtc,
                actorStaffMembershipId = (int?)a.ChangedByStaffMembershipId,
                a.OrderItemId,
                beforeValueJson = a.BeforeSnapshotJson,
                afterValueJson = a.AfterSnapshotJson,
                reason = a.Reason,
                source = "OrderChangeAudit"
            })
            .ToListAsync(ct);

        var bill = await _db.Bills
            .AsNoTracking()
            .SingleOrDefaultAsync(b =>
                b.RestaurantId == restaurantId &&
                b.OrderId == orderId, ct);

        var payments = bill is null
            ? new List<PaymentRecord>()
            : await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.RestaurantId == restaurantId &&
                            p.BillId == bill.Id)
                .OrderBy(p => p.RecordedAtUtc)
                .ThenBy(p => p.Id)
                .ToListAsync(ct);

        var timeline = new List<object>();

        foreach (var e in historyEvents)
        {
            timeline.Add(new
            {
                e.Id,
                e.EventType,
                e.OccurredAtUtc,
                e.actorStaffMembershipId,
                e.OrderItemId,
                e.BeforeValueJson,
                e.AfterValueJson,
                reason = (string?)null,
                e.source
            });
        }

        foreach (var e in auditEvents)
        {
            timeline.Add(new
            {
                e.id,
                e.eventType,
                e.OccurredAtUtc,
                e.actorStaffMembershipId,
                e.OrderItemId,
                e.beforeValueJson,
                e.afterValueJson,
                e.reason,
                e.source
            });
        }

        foreach (var payment in payments)
        {
            timeline.Add(new
            {
                id = payment.Id,
                eventType = payment.Kind == PaymentRecordKinds.Refund
                    ? "PaymentRefunded" : "PaymentRecorded",
                OccurredAtUtc = payment.RecordedAtUtc,
                actorStaffMembershipId =
                    (int?)payment.RecordedByStaffMembershipId,
                orderItemId = (int?)null,
                beforeValueJson = (string?)null,
                afterValueJson = (string?)null,
                reason = (string?)null,
                source = "PaymentRecord",
                amount = payment.Amount,
                payment.Method,
                payment.ExternalReference
            });
        }

        timeline = timeline
            .OrderBy(e => GetTimelineTimestamp(e))
            .ToList();

        decimal? paidAmount = bill is null
            ? null
            : payments
                .Where(p => p.Kind == PaymentRecordKinds.Payment)
                .Sum(p => p.Amount);

        return new
        {
            order.Id,
            order.RestaurantId,
            order.DiningTableId,
            order.tableNumber,
            order.SeatNumber,
            order.Status,
            order.CreatedByStaffMembershipId,
            order.CreatedAtUtc,
            order.SentAtUtc,
            order.PaidAtUtc,
            items = orderItems,
            bill = bill is null ? null : new
            {
                bill.Id,
                bill.CurrencyCode,
                bill.SubtotalAmount,
                bill.TaxAmount,
                bill.DiscountAmount,
                bill.TotalAmount,
                paidAmount,
                balance = bill.TotalAmount - paidAmount!.Value,
                paymentStatus = paidAmount >= bill.TotalAmount
                    ? "Paid"
                    : paidAmount > 0 ? "PartiallyPaid" : "Unpaid"
            },
            timeline
        };
    }

    private static DateTime GetTimelineTimestamp(object item)
    {
        var property = item.GetType().GetProperty("OccurredAtUtc");
        return property?.GetValue(item) is DateTime value
            ? value
            : DateTime.MinValue;
    }
}
