namespace YowThi.Erp.Domain.Procurement;

public static class ProcurementEntryAmount
{
    public static bool TryCalculate(decimal netQuantity, decimal unitPrice, out long amountThb)
    {
        try
        {
            var amount = decimal.Floor(checked(netQuantity * unitPrice));
            amountThb = checked((long)amount);
            return true;
        }
        catch (OverflowException)
        {
            amountThb = default;
            return false;
        }
    }
}
