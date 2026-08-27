namespace YowThi.Erp.Domain.Common;

public interface IHasRowVersion
{
    long RowVersion { get; }
}
