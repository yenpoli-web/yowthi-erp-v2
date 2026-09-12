namespace YowThi.Erp.Application.Processing;

public sealed record ProcessingWorkspaceListQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record ProcessingWorkspaceListItem(
    Guid Id,
    DateOnly WorkDate,
    Guid EmployeeId,
    string EmployeeDisplayName,
    Guid ProcurementBatchId,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    Guid ProcessingModuleId,
    string ProcessingModuleDisplayName,
    string ExecutionMode,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcessingWorkspaceListPage(
    IReadOnlyList<ProcessingWorkspaceListItem> Items,
    int? NextOffset);

public sealed record ProcessingWorkspaceInput(
    Guid ProcessingExecutionId,
    string ConsumptionBasis,
    decimal ConsumedQuantity,
    decimal? ObservedScaleReading,
    int? ActualContainerCount,
    decimal? TareWeightSnapshot,
    decimal? DerivedNetQuantity,
    string? InventoryObjectKind,
    Guid? InventoryObjectId,
    string? InventoryObjectDisplayName,
    Guid? StorageLocationId,
    string? StorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset? DeletedAt);

public sealed record ProcessingWorkspaceOutput(
    Guid Id,
    Guid ProcessingModuleOutputId,
    int OutputSequence,
    string OutputKind,
    Guid? TargetId,
    string? TargetDisplayName,
    decimal ConfiguredWageRate,
    decimal? ObservedScaleReading,
    int? ActualContainerCount,
    decimal? TareWeightSnapshot,
    decimal? DerivedNetQuantity,
    decimal? CompletedQuantity,
    decimal? PackagingWeightSnapshot,
    decimal? SourceConsumptionQuantity,
    Guid? StorageLocationId,
    string? StorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset? DeletedAt);

public sealed record ProcessingWorkspace(
    Guid Id,
    DateOnly WorkDate,
    Guid EmployeeId,
    string EmployeeDisplayName,
    Guid ProcurementBatchId,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    Guid ProcessingRouteVersionId,
    Guid ProcessingModuleId,
    string ProcessingModuleDisplayName,
    string ExecutionMode,
    string? SourceKind,
    Guid? SupplierId,
    string? SupplierDisplayName,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt,
    ProcessingWorkspaceInput? Input,
    IReadOnlyList<ProcessingWorkspaceOutput> Outputs);

public interface IProcessingWorkspaceReader
{
    ValueTask<ProcessingWorkspaceListPage> GetExecutionsAsync(
        ProcessingWorkspaceListQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcessingWorkspace?> GetExecutionAsync(
        Guid processingExecutionId,
        string locale,
        CancellationToken cancellationToken);
}
