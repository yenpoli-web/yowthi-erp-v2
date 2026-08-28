namespace YowThi.Erp.Domain.Finance;

public sealed class Payment
{
    public Guid Id { get; private set; }
    public Guid PayableId { get; private set; }
    public long AmountThb { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public Guid ConfirmedByAccountId { get; private set; }
}
