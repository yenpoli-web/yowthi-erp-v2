using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Finance;

public sealed class Receivable : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid SalesId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long RowVersion { get; private set; }

    public static Receivable Create(Guid id, Guid salesId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || salesId == Guid.Empty)
        {
            throw new ArgumentException("Receivable technical and Sales IDs cannot be empty.");
        }

        return new Receivable
        {
            Id = id,
            SalesId = salesId,
            CreatedAt = createdAt,
            RowVersion = 1,
        };
    }
}