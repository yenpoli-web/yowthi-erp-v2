using YowThi.Erp.Domain.Common;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Domain.Outsourced;

public sealed class OutsourcedSupplyDetail : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid OutsourcedSupplyBatchId { get; private set; }
    public Guid SalesProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public SalesPricingBasis PricingBasisSnapshot { get; private set; }
    public decimal? SalesWeightSnapshot { get; private set; }
    public decimal UnitPrice { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
