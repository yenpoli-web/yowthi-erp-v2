namespace YowThi.Erp.Application.Common.Transactions;

public interface ICommandTransactionRunner
{
    ValueTask<T> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<CommandTransactionDecision<T>>> operation,
        CancellationToken cancellationToken);
}
