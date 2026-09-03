using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Inventory;

namespace YowThi.Erp.Infrastructure.Persistence.Inventory;

internal static class InventoryBatchLifecycleCommandLock
{
    public static ValueTask<BatchLifecycleLockState> AcquireAsync(
        ErpDbContext dbContext,
        InventoryPositionIdentity identity,
        CancellationToken cancellationToken) =>
        identity.Origin switch
        {
            Domain.Inventory.InventoryOrigin.IN_HOUSE => AcquireProcurementAsync(
                dbContext,
                identity.ProcurementBatchId!.Value,
                cancellationToken),
            Domain.Inventory.InventoryOrigin.OUTSOURCED => AcquireOutsourcedAsync(
                dbContext,
                identity.OutsourcedSupplyBatchId!.Value,
                cancellationToken),
            _ => ValueTask.FromResult(BatchLifecycleLockState.Missing()),
        };

    public static async ValueTask<BatchLifecycleLockState> AcquireProcurementAsync(
        ErpDbContext dbContext,
        Guid procurementBatchId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            dbContext,
            """
            SELECT lifecycle_status, deleted_at
            FROM procurement.procurement_batches
            WHERE id = @id
            FOR SHARE;
            """);
        command.Parameters.AddWithValue("id", procurementBatchId);
        return await ReadStateAsync(command, cancellationToken);
    }

    public static async ValueTask<BatchLifecycleLockState> AcquireOutsourcedAsync(
        ErpDbContext dbContext,
        Guid outsourcedSupplyBatchId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            dbContext,
            """
            SELECT lifecycle_status, deleted_at
            FROM outsourced.outsourced_supply_batches
            WHERE id = @id
            FOR SHARE;
            """);
        command.Parameters.AddWithValue("id", outsourcedSupplyBatchId);
        return await ReadStateAsync(command, cancellationToken);
    }

    private static async ValueTask<BatchLifecycleLockState> ReadStateAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return BatchLifecycleLockState.Missing();
        }

        return new BatchLifecycleLockState(
            Exists: true,
            Active: string.Equals(reader.GetString(0), "ACTIVE", StringComparison.Ordinal),
            Deleted: !reader.IsDBNull(1));
    }

    private static NpgsqlCommand CreateSqlCommand(ErpDbContext dbContext, string sql)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Inventory batch lifecycle lock requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }
}

internal readonly record struct BatchLifecycleLockState(
    bool Exists,
    bool Active,
    bool Deleted)
{
    public static BatchLifecycleLockState Missing() => new(false, false, false);
}
