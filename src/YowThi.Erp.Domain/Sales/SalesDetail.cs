using YowThi.Erp.Domain.Common;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Domain.Sales;

public sealed class SalesDetail : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid SalesId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid SalesProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public SalesPricingBasis PricingBasisSnapshot { get; private set; }
    public decimal? SalesWeightSnapshot { get; private set; }
    public decimal UnitPrice { get; private set; }
    public long AmountThb { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
