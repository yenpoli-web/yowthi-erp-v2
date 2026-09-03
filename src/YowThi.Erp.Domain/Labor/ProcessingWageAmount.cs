namespace YowThi.Erp.Domain.Labor;

public static class ProcessingWageAmount
{
    public static bool TryCalculate(decimal aggregatedQuantity, decimal appliedWageRate, out long amountThb)
    {
        amountThb = 0;

        if (aggregatedQuantity < 0 || appliedWageRate < 0)
        {
            return false;
        }

        try
        {
            var exact = decimal.Floor(checked(aggregatedQuantity * appliedWageRate));
            if (exact < 0 || exact > long.MaxValue)
            {
                return false;
            }

            amountThb = checked((long)exact);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
