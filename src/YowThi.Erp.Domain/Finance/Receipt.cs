namespace YowThi.Erp.Domain.Finance;

public sealed class Receipt
{
    public Guid Id { get; private set; }
    public Guid ReceivableId { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public Guid ConfirmedByAccountId { get; private set; }

    public static Receipt Create(
        Guid id,
        Guid receivableId,
        long amountThb,
        DateTimeOffset confirmedAt,
        Guid confirmedByAccountId)
    {
        if (id == Guid.Empty
            || receivableId == Guid.Empty
            || amountThb <= 0
            || confirmedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Receipt is invalid.");
        }

        return new Receipt
        {
            Id = id,
            ReceivableId = receivableId,
            AmountThb = amountThb,
            ConfirmedAt = confirmedAt,
            ConfirmedByAccountId = confirmedByAccountId,
        };
    }
}
