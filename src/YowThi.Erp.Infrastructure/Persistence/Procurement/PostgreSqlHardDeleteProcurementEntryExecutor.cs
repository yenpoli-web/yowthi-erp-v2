using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class PostgreSqlHardDeleteProcurementEntryExecutor : IHardDeleteProcurementEntryExecutor
{
    private const string CommandType = "HardDeleteProcurementEntry";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteProcurementEntryExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<HardDeleteProcurementEntryResult>> ExecuteAsync(
        HardDeleteProcurementEntryExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = ProcurementTransactionLifecycleValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<HardDeleteProcurementEntryResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, ct);
                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteProcurementEntryResult>>.Rollback(
                        ApplicationResult<HardDeleteProcurementEntryResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.IdempotencyKeyReused);
                }

                var state = await LockEntryAsync(execution.Command.ProcurementEntryId, ct);
                if (state is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, ProcurementTransactionLifecycleErrorCodes.EntryNotFound);
                }

                if (state.RowVersion != execution.Command.ExpectedRowVersion)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                await ReverseInventoryProjectionAsync(execution.Command.ProcurementEntryId, ct);
                await ReversePayableProjectionAsync(execution.Command.ProcurementEntryId, now, ct);
                await DeletePendingOutboxAsync(execution.Command.ProcurementEntryId, ct);
                await DeleteEntryDerivedRowsAsync(execution.Command.ProcurementEntryId, ct);

                if (await DeleteEntryAsync(
                        execution.Command.ProcurementEntryId,
                        execution.Command.ExpectedRowVersion,
                        ct) != 1)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                var result = new HardDeleteProcurementEntryResult(execution.Command.ProcurementEntryId);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(execution, state.RowVersion, now, ct);
                await MarkCommandSucceededAsync(execution.CommandId.Value, storedResultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<HardDeleteProcurementEntryResult>>.Commit(
                    ApplicationResult<HardDeleteProcurementEntryResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<EntryState?> LockEntryAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version
            FROM procurement.procurement_entries
            WHERE id = @entry_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("entry_id", entryId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is long rowVersion ? new EntryState(rowVersion) : null;
    }

    private async ValueTask ReverseInventoryProjectionAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            WITH target_delta AS
            (
                SELECT
                    movement.origin,
                    movement.procurement_batch_id,
                    movement.outsourced_supply_batch_id,
                    movement.inventory_object_kind,
                    movement.procurement_product_id,
                    movement.process_material_id,
                    movement.sales_product_id,
                    movement.storage_location_id,
                    movement.raw_source_kind,
                    movement.supplier_id,
                    SUM(movement.quantity_delta) AS quantity_delta
                FROM inventory.inventory_operations operation
                JOIN inventory.inventory_movements movement
                  ON movement.inventory_operation_id = operation.id
                WHERE operation.procurement_entry_id = @entry_id
                GROUP BY
                    movement.origin,
                    movement.procurement_batch_id,
                    movement.outsourced_supply_batch_id,
                    movement.inventory_object_kind,
                    movement.procurement_product_id,
                    movement.process_material_id,
                    movement.sales_product_id,
                    movement.storage_location_id,
                    movement.raw_source_kind,
                    movement.supplier_id
            )
            UPDATE inventory.inventory_positions position
            SET balance_quantity = position.balance_quantity - target_delta.quantity_delta,
                row_version = position.row_version + 1
            FROM target_delta
            WHERE position.origin = target_delta.origin
              AND position.procurement_batch_id IS NOT DISTINCT FROM target_delta.procurement_batch_id
              AND position.outsourced_supply_batch_id IS NOT DISTINCT FROM target_delta.outsourced_supply_batch_id
              AND position.inventory_object_kind = target_delta.inventory_object_kind
              AND position.procurement_product_id IS NOT DISTINCT FROM target_delta.procurement_product_id
              AND position.process_material_id IS NOT DISTINCT FROM target_delta.process_material_id
              AND position.sales_product_id IS NOT DISTINCT FROM target_delta.sales_product_id
              AND position.storage_location_id = target_delta.storage_location_id
              AND position.raw_source_kind IS NOT DISTINCT FROM target_delta.raw_source_kind
              AND position.supplier_id IS NOT DISTINCT FROM target_delta.supplier_id;
            """);
        command.Parameters.AddWithValue("entry_id", entryId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask ReversePayableProjectionAsync(
        Guid entryId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            WITH target_obligation AS
            (
                SELECT payable_id, SUM(amount_thb)::bigint AS amount_thb
                FROM finance.payable_obligation_items
                WHERE procurement_entry_id = @entry_id
                GROUP BY payable_id
            )
            UPDATE finance.payable_outstanding_positions position
            SET original_obligation_thb = position.original_obligation_thb - target_obligation.amount_thb,
                outstanding_thb = position.outstanding_thb - target_obligation.amount_thb,
                row_version = position.row_version + 1,
                updated_at = @updated_at
            FROM target_obligation
            WHERE position.payable_id = target_obligation.payable_id;
            """);
        command.Parameters.AddWithValue("entry_id", entryId);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask DeletePendingOutboxAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'procurement.entry.confirmed'
              AND published_at IS NULL
              AND payload ->> 'procurementEntryId' = @entry_id_text;
            """);
        command.Parameters.AddWithValue("entry_id_text", entryId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask DeleteEntryDerivedRowsAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await using (var movements = CreateSqlCommand(
            """
            DELETE FROM inventory.inventory_movements movement
            USING inventory.inventory_operations operation
            WHERE movement.inventory_operation_id = operation.id
              AND operation.procurement_entry_id = @entry_id;
            """))
        {
            movements.Parameters.AddWithValue("entry_id", entryId);
            await movements.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var operations = CreateSqlCommand(
            """
            DELETE FROM inventory.inventory_operations
            WHERE procurement_entry_id = @entry_id;
            """))
        {
            operations.Parameters.AddWithValue("entry_id", entryId);
            await operations.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var transport = CreateSqlCommand(
            """
            DELETE FROM finance.company_pickup_transport_bases
            WHERE procurement_entry_id = @entry_id;
            """))
        {
            transport.Parameters.AddWithValue("entry_id", entryId);
            await transport.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var obligations = CreateSqlCommand(
            """
            DELETE FROM finance.payable_obligation_items
            WHERE procurement_entry_id = @entry_id;
            """);
        obligations.Parameters.AddWithValue("entry_id", entryId);
        await obligations.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<int> DeleteEntryAsync(
        Guid entryId,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM procurement.procurement_entries
            WHERE id = @entry_id
              AND row_version = @expected_row_version;
            """);
        command.Parameters.AddWithValue("entry_id", entryId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        HardDeleteProcurementEntryExecution execution,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using (var insert = CreateSqlCommand(
            """
            INSERT INTO system.command_executions
                (command_id, command_type, request_hash, status, result_payload,
                 actor_account_id, started_at, executed_at)
            VALUES
                (@command_id, @command_type, @request_hash, 'IN_PROGRESS', NULL,
                 @actor_account_id, @started_at, NULL)
            ON CONFLICT (command_id) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            insert.Parameters.AddWithValue("command_type", CommandType);
            insert.Parameters.AddWithValue("request_hash", execution.RequestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            insert.Parameters.AddWithValue("started_at", startedAt);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return CommandAcquisition.Acquired();
            }
        }

        await using var select = CreateSqlCommand(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", execution.CommandId.Value);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("HardDeleteProcurementEntry command conflict could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), CommandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException("A committed HardDeleteProcurementEntry command is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<HardDeleteProcurementEntryResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored HardDeleteProcurementEntry result could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        HardDeleteProcurementEntryExecution execution,
        long beforeRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = execution.Command.ProcurementEntryId }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'HARD_DELETE', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'procurement.entry', CAST(@subject_key AS jsonb), 'HARD_DELETE',
                 @before_row_version, NULL, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask MarkCommandSucceededAsync(
        Guid commandId,
        string resultJson,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE system.command_executions
            SET status = 'SUCCEEDED',
                result_payload = CAST(@result_payload AS jsonb),
                executed_at = @executed_at
            WHERE command_id = @command_id
              AND status = 'IN_PROGRESS';
            """);
        command.Parameters.AddWithValue("result_payload", resultJson);
        command.Parameters.AddWithValue("executed_at", executedAt);
        command.Parameters.AddWithValue("command_id", commandId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("HardDeleteProcurementEntry command could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("HardDeleteProcurementEntry SQL requires an active command transaction.");
        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteProcurementEntryResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteProcurementEntryResult>>.Rollback(
            ApplicationResult<HardDeleteProcurementEntryResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record EntryState(long RowVersion);
    private enum CommandAcquisitionKind { Acquired, Replay, Conflict }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        HardDeleteProcurementEntryResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(HardDeleteProcurementEntryResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
