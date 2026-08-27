using YowThi.Erp.Domain.Common;
using YowThi.Erp.Domain.ProcessingConfiguration;

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
    public ProcessingSourceKind? ProcessingSourceKind { get; private set; }
    public Guid? SupplierId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
