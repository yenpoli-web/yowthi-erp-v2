using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.SalesHandling;

namespace YowThi.Erp.Infrastructure.Persistence.Labor;

internal sealed class EfLaborDailyWageOptionsReader(ErpDbContext dbContext) : ILaborDailyWageOptionsReader
{
    public async ValueTask<LaborDailyWageOptionPage<LaborDailyWageEmployeeOption>> GetEmployeesAsync(
        LaborDailyWageOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<Employee>()
            .AsNoTracking()
            .Where(employee => employee.Active && employee.DeletedAt == null)
            .Select(employee => new
            {
                employee.Id,
                DisplayName = query.Locale == "th-TH"
                    ? employee.NameThTh ?? employee.NameZhTw
                    : employee.NameZhTw ?? employee.NameThTh,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.DisplayName != null && item.DisplayName.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new LaborDailyWageEmployeeOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id.ToString() : item.DisplayName))
            .ToArray();

        return new LaborDailyWageOptionPage<LaborDailyWageEmployeeOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<LaborDailyWageWorkspace?> GetWorkspaceAsync(
        Guid employeeId,
        DateOnly workDate,
        string locale,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Set<Employee>()
            .AsNoTracking()
            .Where(item => item.Id == employeeId && item.Active && item.DeletedAt == null)
            .Select(item => new
            {
                item.Id,
                DisplayName = locale == "th-TH"
                    ? item.NameThTh ?? item.NameZhTw
                    : item.NameZhTw ?? item.NameThTh,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
        {
            return null;
        }

        var alreadyConfirmed = await dbContext.Set<EmployeeDailyWage>()
            .AsNoTracking()
            .AnyAsync(item => item.EmployeeId == employeeId && item.WorkDate == workDate, cancellationToken);

        var sourceRows = await (
            from output in dbContext.Set<ProcessingExecutionOutput>().AsNoTracking()
            join execution in dbContext.Set<ProcessingExecution>().AsNoTracking()
                on output.ProcessingExecutionId equals execution.Id
            join outputDefinition in dbContext.Set<ProcessingModuleOutput>().AsNoTracking()
                on output.ProcessingModuleOutputId equals outputDefinition.Id
            join module in dbContext.Set<ProcessingModule>().AsNoTracking()
                on outputDefinition.ProcessingModuleId equals module.Id
            where execution.WorkDate == workDate
                && execution.EmployeeId == employeeId
                && execution.DeletedAt == null
                && !dbContext.Set<ProcessingWageComponentSource>()
                    .Any(source => source.ProcessingExecutionOutputId == output.Id)
            select new
            {
                output.ProcessingModuleOutputId,
                output.ConfiguredWageRateSnapshot,
                Quantity = output.DerivedNetQuantity ?? output.CompletedQuantity ?? 0m,
                ModuleName = locale == "th-TH"
                    ? module.NameThTh ?? module.NameZhTw
                    : module.NameZhTw ?? module.NameThTh,
                outputDefinition.OutputSequence,
            })
            .ToListAsync(cancellationToken);

        var processingTargets = sourceRows
            .GroupBy(item => new { item.ProcessingModuleOutputId, item.ConfiguredWageRateSnapshot, item.ModuleName, item.OutputSequence })
            .Select(group => new LaborProcessingWageTargetOption(
                group.Key.ProcessingModuleOutputId,
                $"{(string.IsNullOrWhiteSpace(group.Key.ModuleName) ? group.Key.ProcessingModuleOutputId.ToString() : group.Key.ModuleName)} · #{group.Key.OutputSequence}",
                group.Key.ConfiguredWageRateSnapshot,
                group.Sum(item => item.Quantity)))
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.ConfiguredWageRateSnapshot)
            .ToArray();

        var packagingWages = await dbContext.Set<SalesPackagingWorkRecord>()
            .AsNoTracking()
            .Where(item => item.WorkDate == workDate
                && item.EmployeeId == employeeId
                && item.DeletedAt == null)
            .Where(item => !dbContext.Set<SalesPackagingWageComponent>()
                .Any(component => component.SalesPackagingWorkRecordId == item.Id))
            .Select(item => item.ConfirmedWageThb)
            .ToListAsync(cancellationToken);

        long packagingTotal = 0;
        foreach (var wage in packagingWages)
        {
            packagingTotal = checked(packagingTotal + wage);
        }

        return new LaborDailyWageWorkspace(
            employee.Id,
            workDate,
            string.IsNullOrWhiteSpace(employee.DisplayName) ? employee.Id.ToString() : employee.DisplayName,
            alreadyConfirmed,
            processingTargets,
            packagingWages.Count,
            packagingTotal);
    }
}
