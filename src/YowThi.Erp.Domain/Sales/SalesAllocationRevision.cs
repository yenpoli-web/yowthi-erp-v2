namespace YowThi.Erp.Domain.Sales;

public sealed class SalesAllocationRevision
{
    public Guid Id { get; private set; }
    public Guid SalesId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }

    public static SalesAllocationRevision CreateInitial(
        Guid id,
        Guid salesId,
        DateTimeOffset createdAt,
        Guid createdByAccountId)
    {
        if (id == Guid.Empty || salesId == Guid.Empty || createdByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Sales Allocation Revision technical and reference IDs cannot be empty.");
        }

        return new SalesAllocationRevision
        {
            Id = id,
            SalesId = salesId,
            RevisionNumber = 0,
            Reason = null,
            CreatedAt = createdAt,
            CreatedByAccountId = createdByAccountId,
        };
    }
}