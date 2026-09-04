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
        Guid createdByAccountId) =>
        Create(id, salesId, 0, null, createdAt, createdByAccountId);

    public static SalesAllocationRevision CreateCorrection(
        Guid id,
        Guid salesId,
        int revisionNumber,
        DateTimeOffset createdAt,
        Guid createdByAccountId) =>
        Create(id, salesId, revisionNumber, null, createdAt, createdByAccountId);

    private static SalesAllocationRevision Create(
        Guid id,
        Guid salesId,
        int revisionNumber,
        string? reason,
        DateTimeOffset createdAt,
        Guid createdByAccountId)
    {
        if (id == Guid.Empty || salesId == Guid.Empty || createdByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Sales Allocation Revision technical and reference IDs cannot be empty.");
        }

        if (revisionNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        }

        return new SalesAllocationRevision
        {
            Id = id,
            SalesId = salesId,
            RevisionNumber = revisionNumber,
            Reason = reason,
            CreatedAt = createdAt,
            CreatedByAccountId = createdByAccountId,
        };
    }
}