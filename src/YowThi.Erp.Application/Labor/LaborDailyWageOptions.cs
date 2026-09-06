namespace YowThi.Erp.Application.Labor;

public sealed record LaborDailyWageOptionsQuery(string Locale, string? Search, int Offset, int Limit);
public sealed record LaborDailyWageOptionPage<T>(IReadOnlyList<T> Items, int? NextOffset);
public sealed record LaborDailyWageEmployeeOption(Guid Id, string DisplayName);
public sealed record LaborProcessingWageTargetOption(Guid ProcessingModuleOutputId, string DisplayName, decimal ConfiguredWageRateSnapshot, decimal AggregatedQuantity);
public sealed record LaborDailyWageWorkspace(Guid EmployeeId, DateOnly WorkDate, string EmployeeDisplayName, bool AlreadyConfirmed, IReadOnlyList<LaborProcessingWageTargetOption> ProcessingTargets, int SalesPackagingWorkRecordCount, long SalesPackagingWageTotalThb);

public interface ILaborDailyWageOptionsReader
{
    ValueTask<LaborDailyWageOptionPage<LaborDailyWageEmployeeOption>> GetEmployeesAsync(LaborDailyWageOptionsQuery query, CancellationToken cancellationToken);
    ValueTask<LaborDailyWageWorkspace?> GetWorkspaceAsync(Guid employeeId, DateOnly workDate, string locale, CancellationToken cancellationToken);
}
