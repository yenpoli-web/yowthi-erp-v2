namespace YowThi.Erp.Application.Common.Transactions;

public sealed record CommandTransactionDecision<T>
{
    private CommandTransactionDecision(T value, bool shouldCommit)
    {
        Value = value;
        ShouldCommit = shouldCommit;
    }

    public T Value { get; }

    public bool ShouldCommit { get; }

    public static CommandTransactionDecision<T> Commit(T value) => new(value, true);

    public static CommandTransactionDecision<T> Rollback(T value) => new(value, false);
}
