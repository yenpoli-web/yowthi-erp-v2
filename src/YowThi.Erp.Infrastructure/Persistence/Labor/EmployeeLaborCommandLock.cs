using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace YowThi.Erp.Infrastructure.Persistence.Labor;

internal static class EmployeeLaborCommandLock
{
    public static async ValueTask<EmployeeLaborCommandLockState> AcquireAsync(
        ErpDbContext dbContext,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (employeeId == Guid.Empty)
        {
            return EmployeeLaborCommandLockState.Missing;
        }

        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Employee labor command locking requires the active command transaction.");

        await using var command = new NpgsqlCommand(
            """
            SELECT active, deleted_at IS NOT NULL
            FROM party.employees
            WHERE id = @employee_id
            FOR UPDATE;
            """,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("employee_id", employeeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return EmployeeLaborCommandLockState.Missing;
        }

        return new EmployeeLaborCommandLockState(
            Exists: true,
            Active: reader.GetBoolean(0),
            Deleted: reader.GetBoolean(1));
    }
}

internal readonly record struct EmployeeLaborCommandLockState(
    bool Exists,
    bool Active,
    bool Deleted)
{
    public static EmployeeLaborCommandLockState Missing => new(false, false, false);
}
