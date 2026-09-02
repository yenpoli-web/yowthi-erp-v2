using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Domain.Tests;

public sealed class OutsourcedSupplyDetailAmountTests
{
    [Fact]
    public void Calculate_weighted_amount_uses_quantity_times_sales_weight_times_price()
    {
        var succeeded = OutsourcedSupplyDetailAmount.TryCalculate(
            12.5m,
            SalesPricingBasis.WEIGHT_BASED_UNIT,
            5.2m,
            100.25m,
            out var amountThb);

        Assert.True(succeeded);
        Assert.Equal(6516L, amountThb);
    }

    [Fact]
    public void Calculate_unit_based_amount_uses_quantity_times_price()
    {
        var succeeded = OutsourcedSupplyDetailAmount.TryCalculate(
            12.5m,
            SalesPricingBasis.UNIT_BASED,
            null,
            30.25m,
            out var amountThb);

        Assert.True(succeeded);
        Assert.Equal(378L, amountThb);
    }

    [Fact]
    public void Calculate_preserves_valid_zero_amount()
    {
        var succeeded = OutsourcedSupplyDetailAmount.TryCalculate(
            1m,
            SalesPricingBasis.UNIT_BASED,
            null,
            0m,
            out var amountThb);

        Assert.True(succeeded);
        Assert.Equal(0L, amountThb);
    }

    [Theory]
    [InlineData(SalesPricingBasis.WEIGHT_BASED_UNIT, null)]
    [InlineData(SalesPricingBasis.UNIT_BASED, 5.0)]
    public void Calculate_rejects_invalid_pricing_snapshot_shape(
        SalesPricingBasis pricingBasis,
        double? salesWeight)
    {
        var succeeded = OutsourcedSupplyDetailAmount.TryCalculate(
            1m,
            pricingBasis,
            salesWeight is null ? null : (decimal)salesWeight.Value,
            10m,
            out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void Calculate_rejects_values_that_do_not_fit_bigint()
    {
        var succeeded = OutsourcedSupplyDetailAmount.TryCalculate(
            decimal.MaxValue,
            SalesPricingBasis.UNIT_BASED,
            null,
            2m,
            out _);

        Assert.False(succeeded);
    }
}
