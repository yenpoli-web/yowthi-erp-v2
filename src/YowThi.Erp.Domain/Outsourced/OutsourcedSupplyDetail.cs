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

    public static OutsourcedSupplyDetail Create(
        Guid id,
        Guid outsourcedSupplyBatchId,
        Guid salesProductId,
        decimal quantity,
        SalesPricingBasis pricingBasisSnapshot,
        decimal? salesWeightSnapshot,
        decimal unitPrice,
        long amountThb,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty
            || outsourcedSupplyBatchId == Guid.Empty
            || salesProductId == Guid.Empty
            || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Outsourced Supply Detail technical and reference IDs cannot be empty.");
        }

        var pricingShapeIsValid = pricingBasisSnapshot switch
        {
            SalesPricingBasis.WEIGHT_BASED_UNIT => salesWeightSnapshot is not null,
            SalesPricingBasis.UNIT_BASED => salesWeightSnapshot is null,
            _ => false,
        };

        if (!pricingShapeIsValid)
        {
            throw new ArgumentException("Outsourced Supply Detail pricing snapshot shape is invalid.");
        }

        return new OutsourcedSupplyDetail
        {
            Id = id,
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId,
            SalesProductId = salesProductId,
            Quantity = quantity,
            PricingBasisSnapshot = pricingBasisSnapshot,
            SalesWeightSnapshot = salesWeightSnapshot,
            UnitPrice = unitPrice,
            AmountThb = amountThb,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
            RowVersion = 1,
        };
    }
}
