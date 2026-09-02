using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Sales;

public sealed class Sale : IHasRowVersion
{
    public Guid Id { get; private set; }
    public DateOnly SalesDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public SalesStatus Status { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid? ConfirmedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }

    public void Confirm(DateTimeOffset confirmedAt, Guid confirmedByAccountId)
    {
        if (confirmedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Sales confirmation actor cannot be empty.", nameof(confirmedByAccountId));
        }

        if (Status != SalesStatus.DRAFT || ConfirmedAt is not null || ConfirmedByAccountId is not null)
        {
            throw new InvalidOperationException("Only an unconfirmed DRAFT Sale can be confirmed.");
        }

        Status = SalesStatus.CONFIRMED;
        ConfirmedAt = confirmedAt;
        ConfirmedByAccountId = confirmedByAccountId;
    }
}