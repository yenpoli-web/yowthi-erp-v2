using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Party;

public sealed class Employee : IHasRowVersion
{
    public Guid Id { get; private set; }
    public string? Code { get; private set; }
    public string? NameZhTw { get; private set; }
    public string? NameThTh { get; private set; }
    public string? BankName { get; private set; }
    public string? BankAccount { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public bool Active { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
