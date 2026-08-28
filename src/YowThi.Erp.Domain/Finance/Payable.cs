using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Finance;

public sealed class Payable : IHasRowVersion
{
    public Guid Id { get; private set; }
    public PayableKind PayableKind { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Guid? FarmerId { get; private set; }
    public Guid? OutsourcedSupplyDetailId { get; private set; }
    public Guid? EmployeeDailyWageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long RowVersion { get; private set; }
}
