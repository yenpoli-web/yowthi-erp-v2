using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Application.Processing;

public sealed record ProcessingExecutionOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record ProcessingExecutionOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record ProcessingEmployeeOption(
    Guid Id,
    string DisplayName);

public sealed record ProcessingBatchOption(
    Guid Id,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    Guid ProcessingRouteVersionId);

public sealed record ProcessingModuleOption(
    Guid Id,
    string DisplayName,
    ProcessingExecutionMode ExecutionMode,
    Guid? InputProcessMaterialId,
    bool? InputUsesContainer,
    Guid? InputContainerId,
    int? DefaultInputContainerCount);

public sealed record ProcessingBatchModuleOptions(
    Guid ProcurementBatchId,
    Guid ProcessingRouteVersionId,
    ProcessingExecutionOptionPage<ProcessingModuleOption> Modules);

public sealed record ProcessingSupplierOption(
    Guid Id,
    string DisplayName);

public sealed record ProcessingStorageLocationOption(
    Guid Id,
    string DisplayName,
    string? Code,
    Guid WarehouseId);

public sealed record ProcessingInputStorageLocationOptions(
    Guid ProcurementBatchId,
    Guid ProcessingModuleId,
    Guid? AutoSelectionLocationId,
    ProcessingExecutionOptionPage<ProcessingStorageLocationOption> Locations);

public sealed record ProcessingModuleOutputOption(
    Guid Id,
    int OutputSequence,
    ProcessingOutputKind OutputKind,
    Guid? TargetId,
    string? TargetDisplayName,
    bool TargetAvailable,
    bool UsesContainer,
    Guid? ContainerId,
    int? DefaultContainerCount,
    decimal? PackagingWeight,
    Guid? DefaultStorageLocationId,
    bool DefaultStorageLocationAvailable,
    decimal DefaultWageRate);

public sealed record ProcessingModuleOutputOptions(
    Guid ProcurementBatchId,
    Guid ProcessingModuleId,
    IReadOnlyList<ProcessingModuleOutputOption> Outputs);

public interface IProcessingExecutionOptionsReader
{
    ValueTask<ProcessingExecutionOptionPage<ProcessingEmployeeOption>> GetEmployeesAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcessingExecutionOptionPage<ProcessingBatchOption>> GetBatchesAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcessingBatchModuleOptions?> GetModulesAsync(
        Guid procurementBatchId,
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcessingExecutionOptionPage<ProcessingSupplierOption>> GetSuppliersAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcessingInputStorageLocationOptions?> GetInputStorageLocationsAsync(
        Guid procurementBatchId,
        Guid processingModuleId,
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcessingModuleOutputOptions?> GetModuleOutputsAsync(
        Guid procurementBatchId,
        Guid processingModuleId,
        string locale,
        CancellationToken cancellationToken);

    ValueTask<ProcessingExecutionOptionPage<ProcessingStorageLocationOption>> GetStorageLocationsAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken);
}
