using YowThi.Erp.Domain.Labor;

namespace YowThi.Erp.Domain.Tests;

public sealed class ProcessingWageAmountTests
{
    [Theory]
    [InlineData("3.3", "2.5", 8L)]
    [InlineData("0", "12.75", 0L)]
    [InlineData("10.5", "0", 0L)]
    public void Calculates_floor_after_aggregate(string quantityText, string rateText, long expected)
    {
        var quantity = decimal.Parse(quantityText, System.Globalization.CultureInfo.InvariantCulture);
        var rate = decimal.Parse(rateText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(ProcessingWageAmount.TryCalculate(quantity, rate, out var amount));
        Assert.Equal(expected, amount);
    }

    [Fact]
    public void Rejects_negative_values()
    {
        Assert.False(ProcessingWageAmount.TryCalculate(-1m, 5m, out _));
        Assert.False(ProcessingWageAmount.TryCalculate(1m, -5m, out _));
    }

    [Fact]
    public void Rejects_decimal_overflow()
    {
        Assert.False(ProcessingWageAmount.TryCalculate(decimal.MaxValue, 2m, out _));
    }
}
