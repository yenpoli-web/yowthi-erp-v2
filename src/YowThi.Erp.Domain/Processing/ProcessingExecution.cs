using YowThi.Erp.Domain.Common;
using YowThi.Erp.Domain.ProcessingConfiguration;
using SourceKind = YowThi.Erp.Domain.Processing.ProcessingSourceKind;

namespace YowThi.Erp.Domain.Processing;

public sealed class ProcessingExecution : IHasRowVersion
{
    public Guid Id { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid ProcurementBatchId { get; private set; }
    public Guid ProcessingRouteVersionId { get; private set; }
    public Guid ProcessingModuleId { get; private set; }
    public ProcessingExecutionMode ExecutionModeSnapshot { get; private set; }
    public string NegativeInventoryPolicySnapshot { get; private set; } = null!;
    public SourceKind? ProcessingSourceKind { get; private set; }
    public Guid? SupplierId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }

    public static ProcessingExecution Create(
        Guid id,
        DateOnly workDate,
        Guid employeeId,
        Guid procurementBatchId,
        Guid processingRouteVersionId,
        Guid processingModuleId,
        ProcessingExecutionMode executionMode,
        string negativeInventoryPolicy,
        SourceKind? sourceKind,
        Guid? supplierId,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty || employeeId == Guid.Empty || procurementBatchId == Guid.Empty
            || processingRouteVersionId == Guid.Empty || processingModuleId == Guid.Empty
            || recordedByAccountId == Guid.Empty || string.IsNullOrWhiteSpace(negativeInventoryPolicy))
        {
            throw new ArgumentException("Processing execution references and policy must be present.");
        }

        var validSource = executionMode == ProcessingExecutionMode.SOURCE_TRACKED
            ? sourceKind is SourceKind.SUPPLIER or SourceKind.FARMERS_COMBINED
              && (sourceKind != SourceKind.SUPPLIER || supplierId is not null)
              && (sourceKind != SourceKind.FARMERS_COMBINED || supplierId is null)
            : sourceKind is null && supplierId is null;

        if (!validSource)
        {
            throw new ArgumentException("Processing source shape does not match the execution mode.");
        }

        return new ProcessingExecution
        {
            Id = id,
            WorkDate = workDate,
            EmployeeId = employeeId,
            ProcurementBatchId = procurementBatchId,
            ProcessingRouteVersionId = processingRouteVersionId,
            ProcessingModuleId = processingModuleId,
            ExecutionModeSnapshot = executionMode,
            NegativeInventoryPolicySnapshot = negativeInventoryPolicy,
            ProcessingSourceKind = sourceKind,
            SupplierId = supplierId,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
            RowVersion = 1,
        };
    }
}