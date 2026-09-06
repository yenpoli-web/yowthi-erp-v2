using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Finance;

internal sealed class EfFinanceSettlementOptionsReader(ErpDbContext dbContext) : IFinanceSettlementOptionsReader
{
    public async ValueTask<FinanceSettlementOptionPage<FinancePayableSettlementOption>> GetPayablesAsync(
        FinanceSettlementOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source =
            from payable in dbContext.Set<Payable>().AsNoTracking()
            join outstanding in dbContext.Set<PayableOutstandingPosition>().AsNoTracking()
                on payable.Id equals outstanding.PayableId
            where outstanding.OutstandingThb > 0
                && payable.PayableKind != PayableKind.COMPANY_PICKUP_TRANSPORT
            select new
            {
                PayableId = payable.Id,
                payable.PayableKind,
                outstanding.OutstandingThb,
                OutstandingVersion = outstanding.RowVersion,
                outstanding.UpdatedAt,
                SourceDisplayName = payable.SupplierId != null
                    ? dbContext.Set<Supplier>()
                        .Where(item => item.Id == payable.SupplierId.Value)
                        .Select(item => query.Locale == "th-TH"
                            ? item.NameThTh ?? item.NameZhTw
                            : item.NameZhTw ?? item.NameThTh)
                        .FirstOrDefault()
                    : payable.FarmerId != null
                        ? dbContext.Set<Farmer>()
                            .Where(item => item.Id == payable.FarmerId.Value)
                            .Select(item => query.Locale == "th-TH"
                                ? item.NameThTh ?? item.NameZhTw
                                : item.NameZhTw ?? item.NameThTh)
                            .FirstOrDefault()
                        : payable.EmployeeDailyWageId != null
                            ? (from wage in dbContext.Set<EmployeeDailyWage>()
                               join employee in dbContext.Set<Employee>() on wage.EmployeeId equals employee.Id
                               where wage.Id == payable.EmployeeDailyWageId.Value
                               select query.Locale == "th-TH"
                                   ? employee.NameThTh ?? employee.NameZhTw
                                   : employee.NameZhTw ?? employee.NameThTh)
                                .FirstOrDefault()
                            : payable.OutsourcedSupplyDetailId != null
                                ? (from detail in dbContext.Set<OutsourcedSupplyDetail>()
                                   join batch in dbContext.Set<OutsourcedSupplyBatch>()
                                       on detail.OutsourcedSupplyBatchId equals batch.Id
                                   join vendor in dbContext.Set<OutsourcedVendor>()
                                       on batch.OutsourcedVendorId equals vendor.Id
                                   where detail.Id == payable.OutsourcedSupplyDetailId.Value
                                   select query.Locale == "th-TH"
                                       ? vendor.NameThTh ?? vendor.NameZhTw
                                       : vendor.NameZhTw ?? vendor.NameThTh)
                                    .FirstOrDefault()
                                : null,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.SourceDisplayName != null && item.SourceDisplayName.Contains(search));
        }

        var rows = await source
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.PayableId)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new FinancePayableSettlementOption(
                item.PayableId,
                item.PayableKind.ToString(),
                string.IsNullOrWhiteSpace(item.SourceDisplayName) ? item.PayableId.ToString() : item.SourceDisplayName,
                item.OutstandingThb,
                item.OutstandingVersion,
                item.UpdatedAt))
            .ToArray();

        return new FinanceSettlementOptionPage<FinancePayableSettlementOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<FinanceSettlementOptionPage<FinanceReceivableSettlementOption>> GetReceivablesAsync(
        FinanceSettlementOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source =
            from receivable in dbContext.Set<Receivable>().AsNoTracking()
            join outstanding in dbContext.Set<ReceivableOutstandingPosition>().AsNoTracking()
                on receivable.Id equals outstanding.ReceivableId
            join sale in dbContext.Set<Sale>().AsNoTracking() on receivable.SalesId equals sale.Id
            join customer in dbContext.Set<Customer>().AsNoTracking() on sale.CustomerId equals customer.Id
            where outstanding.OutstandingThb > 0
            select new
            {
                ReceivableId = receivable.Id,
                receivable.SalesId,
                sale.SalesDate,
                CustomerId = customer.Id,
                CustomerDisplayName = query.Locale == "th-TH"
                    ? customer.NameThTh ?? customer.NameZhTw
                    : customer.NameZhTw ?? customer.NameThTh,
                outstanding.OutstandingThb,
                OutstandingVersion = outstanding.RowVersion,
                outstanding.UpdatedAt,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.CustomerDisplayName != null && item.CustomerDisplayName.Contains(search));
        }

        var rows = await source
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.ReceivableId)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new FinanceReceivableSettlementOption(
                item.ReceivableId,
                item.SalesId,
                item.SalesDate,
                string.IsNullOrWhiteSpace(item.CustomerDisplayName) ? item.CustomerId.ToString() : item.CustomerDisplayName,
                item.OutstandingThb,
                item.OutstandingVersion,
                item.UpdatedAt))
            .ToArray();

        return new FinanceSettlementOptionPage<FinanceReceivableSettlementOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }
}
