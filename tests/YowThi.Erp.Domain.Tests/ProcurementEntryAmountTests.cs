using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Domain.Tests;

public sealed class ProcurementEntryAmountTests
{
    [Fact]
    public void Calculate_floors_quantity_times_price_to_integer_thb()
    {
        var succeeded = ProcurementEntryAmount.TryCalculate(125.5m, 18.25m, out var amountThb);

        Assert.True(succeeded);
        Assert.Equal(2290L, amountThb);
    }

    [Fact]
    public void Calculate_preserves_valid_zero_amount()
    {
        var succeeded = ProcurementEntryAmount.TryCalculate(1m, 0m, out var amountThb);

        Assert.True(succeeded);
        Assert.Equal(0L, amountThb);
    }

    [Fact]
    public void Calculate_rejects_values_that_do_not_fit_bigint()
    {
        var succeeded = ProcurementEntryAmount.TryCalculate(decimal.MaxValue, 2m, out _);

        Assert.False(succeeded);
    }
}
