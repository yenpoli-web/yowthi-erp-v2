using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Domain.Sales;

public static class SalesDetailAmount
{
    public static bool TryCalculate(
        decimal quantity,
        SalesPricingBasis pricingBasis,
        decimal? salesWeight,
        decimal unitPrice,
        out long amountThb)
    {
        amountThb = 0;

        if (quantity < 0 || unitPrice < 0)
        {
            return false;
        }

        decimal rawAmount;
        try
        {
            rawAmount = pricingBasis switch
            {
                SalesPricingBasis.WEIGHT_BASED_UNIT when salesWeight is >= 0m
                    => quantity * salesWeight.Value * unitPrice,
                SalesPricingBasis.UNIT_BASED when salesWeight is null
                    => quantity * unitPrice,
                _ => -1m,
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        if (rawAmount < 0)
        {
            return false;
        }

        var floored = decimal.Floor(rawAmount);
        if (floored > long.MaxValue)
        {
            return false;
        }

        amountThb = decimal.ToInt64(floored);
        return true;
    }
}