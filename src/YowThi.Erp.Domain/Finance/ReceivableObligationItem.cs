namespace YowThi.Erp.Domain.Finance;

public sealed class ReceivableObligationItem
{
    public Guid Id { get; private set; }
    public Guid ReceivableId { get; private set; }
    public Guid SalesId { get; private set; }
    public Guid SalesDetailId { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static ReceivableObligationItem Create(
        Guid id,
        Guid receivableId,
        Guid salesId,
        Guid salesDetailId,
        long amountThb,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty
            || receivableId == Guid.Empty
            || salesId == Guid.Empty
            || salesDetailId == Guid.Empty)
        {
            throw new ArgumentException("Receivable Obligation Item technical and reference IDs cannot be empty.");
        }

        if (amountThb < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountThb), "Receivable obligation amount cannot be negative.");
        }

        return new ReceivableObligationItem
        {
            Id = id,
            ReceivableId = receivableId,
            SalesId = salesId,
            SalesDetailId = salesDetailId,
            AmountThb = amountThb,
            RecordedAt = recordedAt,
        };
    }
}