namespace YowThi.Erp.Domain.Sales;

public sealed class SalesAllocationRevision
{
    public Guid Id { get; private set; }
    public Guid SalesId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
}
