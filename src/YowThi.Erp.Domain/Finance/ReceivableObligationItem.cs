namespace YowThi.Erp.Domain.Finance;

public sealed class ReceivableObligationItem
{
    public Guid Id { get; private set; }
    public Guid ReceivableId { get; private set; }
    public Guid SalesId { get; private set; }
    public Guid SalesDetailId { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}
