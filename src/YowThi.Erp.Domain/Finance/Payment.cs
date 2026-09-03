namespace YowThi.Erp.Domain.Finance;

public sealed class Payment
{
    public Guid Id { get; private set; }
    public Guid PayableId { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public Guid ConfirmedByAccountId { get; private set; }

    public static Payment Create(
        Guid id,
        Guid payableId,
        long amountThb,
        DateTimeOffset confirmedAt,
        Guid confirmedByAccountId)
    {
        if (id == Guid.Empty
            || payableId == Guid.Empty
            || amountThb <= 0
            || confirmedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Payment is invalid.");
        }

        return new Payment
        {
            Id = id,
            PayableId = payableId,
            AmountThb = amountThb,
            ConfirmedAt = confirmedAt,
            ConfirmedByAccountId = confirmedByAccountId,
        };
    }
}
