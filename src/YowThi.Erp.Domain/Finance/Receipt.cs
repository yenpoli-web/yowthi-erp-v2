namespace YowThi.Erp.Domain.Finance;

public sealed class Receipt
{
    public Guid Id { get; private set; }
    public Guid ReceivableId { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public Guid ConfirmedByAccountId { get; private set; }
}
