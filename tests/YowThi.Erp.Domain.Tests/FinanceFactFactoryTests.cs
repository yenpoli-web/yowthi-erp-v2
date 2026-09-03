using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Domain.Tests;

public sealed class FinanceFactFactoryTests
{
    [Fact]
    public void Payable_adjustment_factory_accepts_only_supplier_deduction_shape()
    {
        var id = Guid.CreateVersion7();
        var payableId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var adjustment = PayableAdjustment.Create(
            id,
            payableId,
            PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
            -125,
            "quality deduction",
            now,
            actorId);

        Assert.Equal(id, adjustment.Id);
        Assert.Equal(payableId, adjustment.PayableId);
        Assert.Equal(-125, adjustment.AmountDeltaThb);
        Assert.Equal("quality deduction", adjustment.ReasonText);
        Assert.Equal(now, adjustment.RecordedAt);
        Assert.Equal(actorId, adjustment.RecordedByAccountId);

        Assert.Throws<ArgumentException>(() => PayableAdjustment.Create(
            Guid.CreateVersion7(),
            payableId,
            PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
            0,
            null,
            now,
            actorId));
    }

    [Fact]
    public void Payment_factory_requires_positive_settlement_amount()
    {
        var payment = Payment.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            500,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7());

        Assert.Equal(500, payment.AmountThb);

        Assert.Throws<ArgumentException>(() => Payment.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            0,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7()));
    }

    [Fact]
    public void Receipt_factory_requires_positive_settlement_amount()
    {
        var receipt = Receipt.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            750,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7());

        Assert.Equal(750, receipt.AmountThb);

        Assert.Throws<ArgumentException>(() => Receipt.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            -1,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7()));
    }
}
