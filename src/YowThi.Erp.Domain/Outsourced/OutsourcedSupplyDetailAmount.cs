using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Domain.Outsourced;

public static class OutsourcedSupplyDetailAmount
{
    public static bool TryCalculate(
        decimal quantity,
        SalesPricingBasis pricingBasis,
        decimal? salesWeightSnapshot,
        decimal unitPrice,
        out long amountThb)
    {
        try
        {
            decimal rawAmount;

            switch (pricingBasis)
            {
                case SalesPricingBasis.WEIGHT_BASED_UNIT when salesWeightSnapshot is decimal salesWeight:
                    rawAmount = checked(checked(quantity * salesWeight) * unitPrice);
                    break;
                case SalesPricingBasis.UNIT_BASED when salesWeightSnapshot is null:
                    rawAmount = checked(quantity * unitPrice);
                    break;
                default:
                    amountThb = default;
                    return false;
            }

            amountThb = checked((long)decimal.Floor(rawAmount));
            return true;
        }
        catch (OverflowException)
        {
            amountThb = default;
            return false;
        }
    }
}
