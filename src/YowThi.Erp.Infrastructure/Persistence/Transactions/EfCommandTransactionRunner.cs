using System.Data;
using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Common.Transactions;

namespace YowThi.Erp.Infrastructure.Persistence.Transactions;

internal sealed class EfCommandTransactionRunner : ICommandTransactionRunner
{
    private readonly ErpDbContext _dbContext;
    private int _executionStarted;

    public EfCommandTransactionRunner(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<T> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<CommandTransactionDecision<T>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Interlocked.Exchange(ref _executionStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                "A command transaction runner is single-use. Retry the whole command with a fresh service scope and ErpDbContext.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var decision = await operation(cancellationToken);

            if (decision.ShouldCommit)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            return decision.Value;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original command/persistence exception. The scope is not reusable.
            }

            throw;
        }
    }
}
