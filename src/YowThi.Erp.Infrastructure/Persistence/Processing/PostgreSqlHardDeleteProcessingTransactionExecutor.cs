using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Processing;

namespace YowThi.Erp.Infrastructure.Persistence.Processing;

internal sealed class PostgreSqlHardDeleteProcessingTransactionExecutor : IHardDeleteProcessingTransactionExecutor
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteProcessingTransactionExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> HardDeleteExecutionAsync(
        HardDeleteProcessingExecutionExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcessingExecutionId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Execution,
            ProcessingTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    public ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> HardDeleteInputAsync(
        HardDeleteProcessingExecutionInputExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcessingExecutionId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Input,
            ProcessingTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    public ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> HardDeleteOutputAsync(
        HardDeleteProcessingExecutionOutputExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcessingExecutionOutputId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Output,
            ProcessingTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    private async ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> ExecuteAsync(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        Guid id,
        long expectedRowVersion,
        TargetKind target,
        ApplicationError? validationError,
        CancellationToken cancellationToken)
    {
        if (validationError is not null)
        {
            return ApplicationResult<HardDeleteProcessingTransactionResult>.Failure(validationError);
        }

        var commandType = target switch
        {
            TargetKind.Execution => "HardDeleteProcessingExecution",
            TargetKind.Input => "HardDeleteProcessingExecutionInput",
            TargetKind.Output => "HardDeleteProcessingExecutionOutput",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(
                    commandId.Value,
                    requestHash,
                    actorAccountId.Value,
                    commandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteProcessingTransactionResult>>.Rollback(
                        ApplicationResult<HardDeleteProcessingTransactionResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingTransactionLifecycleErrorCodes.IdempotencyKeyReused);
                }

                var lockResult = await LockTargetAsync(id, target, ct);
                if (lockResult is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, NotFoundCode(target));
                }

                if (lockResult.RowVersion != expectedRowVersion)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                ApplicationError? closureError = target switch
                {
                    TargetKind.Execution => await HardDeleteExecutionClosureAsync(id, expectedRowVersion, now, ct),
                    TargetKind.Input => await HardDeleteInputClosureAsync((InputState)lockResult, expectedRowVersion, ct),
                    TargetKind.Output => await HardDeleteOutputClosureAsync((OutputState)lockResult, expectedRowVersion, now, ct),
                    _ => throw new ArgumentOutOfRangeException(nameof(target)),
                };
                if (closureError is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteProcessingTransactionResult>>.Rollback(
                        ApplicationResult<HardDeleteProcessingTransactionResult>.Failure(closureError));
                }

                var result = new HardDeleteProcessingTransactionResult(id);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);
                await AppendAuditAsync(
                    commandId.Value,
                    actorAccountId.Value,
                    id,
                    target,
                    commandType,
                    expectedRowVersion,
                    now,
                    ct);
                await MarkCommandSucceededAsync(commandId.Value, storedResultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<HardDeleteProcessingTransactionResult>>.Commit(
                    ApplicationResult<HardDeleteProcessingTransactionResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<TargetState?> LockTargetAsync(Guid id, TargetKind target, CancellationToken ct)
    {
        if (target == TargetKind.Execution)
        {
            await using var command = CreateSqlCommand(
                "SELECT row_version FROM processing.processing_executions WHERE id = @id FOR UPDATE;");
            command.Parameters.AddWithValue("id", id);
            var value = await command.ExecuteScalarAsync(ct);
            return value is long rowVersion ? new ExecutionState(id, rowVersion) : null;
        }

        if (target == TargetKind.Input)
        {
            await using var command = CreateSqlCommand(
                """
                SELECT row_version, consumed_quantity
                FROM processing.processing_execution_inputs
                WHERE processing_execution_id = @id
                FOR UPDATE;
                """);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new InputState(id, reader.GetInt64(0), reader.GetDecimal(1));
        }

        await using (var command = CreateSqlCommand(
            """
            SELECT
                output.row_version,
                output.processing_execution_id,
                execution.execution_mode_snapshot,
                CASE
                    WHEN execution.execution_mode_snapshot = 'FINAL_PACKAGING'
                        THEN COALESCE(output.source_consumption_quantity, 0)
                    WHEN execution.execution_mode_snapshot = 'POOLED_OUTPUT'
                        THEN COALESCE(output.derived_net_quantity, output.completed_quantity, 0)
                    ELSE 0
                END AS source_contribution
            FROM processing.processing_execution_outputs output
            JOIN processing.processing_executions execution
              ON execution.id = output.processing_execution_id
            WHERE output.id = @id
            FOR UPDATE OF output, execution;
            """))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new OutputState(
                id,
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetDecimal(3));
        }
    }

    private async ValueTask<ApplicationError?> HardDeleteInputClosureAsync(
        InputState input,
        long expectedRowVersion,
        CancellationToken ct)
    {
        var movements = await ReadConsumeMovementsAsync(input.ProcessingExecutionId, ct);
        if (movements.Count == 0 && input.ConsumedQuantity == 0m)
        {
            return await DeleteInputAsync(input.ProcessingExecutionId, expectedRowVersion, ct) == 1
                ? null
                : Conflict(ProcessingTransactionLifecycleErrorCodes.StaleRowVersion);
        }

        if (movements.Count != 1)
        {
            return Conflict(ProcessingTransactionLifecycleErrorCodes.InputHardDeleteClosureInvalid);
        }

        var movement = movements[0];
        if (!await AdjustPositionAsync(movement, -movement.QuantityDelta, ct))
        {
            return Conflict(ProcessingTransactionLifecycleErrorCodes.InputHardDeleteClosureInvalid);
        }

        if (await DeleteMovementAsync(movement.Id, ct) != 1)
        {
            return Conflict(ProcessingTransactionLifecycleErrorCodes.InputHardDeleteClosureInvalid);
        }

        return await DeleteInputAsync(input.ProcessingExecutionId, expectedRowVersion, ct) == 1
            ? null
            : Conflict(ProcessingTransactionLifecycleErrorCodes.StaleRowVersion);
    }

    private async ValueTask<ApplicationError?> HardDeleteOutputClosureAsync(
        OutputState output,
        long expectedRowVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var produceMovements = await ReadMatchingProduceMovementsAsync(output.OutputId, ct);
        if (produceMovements.Count != 1)
        {
            return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
        }

        var produce = produceMovements[0];
        if (!await AdjustPositionAsync(produce, -produce.QuantityDelta, ct))
        {
            return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
        }

        if (await DeleteMovementAsync(produce.Id, ct) != 1)
        {
            return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
        }

        if (output.SourceContribution > 0m
            && output.ExecutionMode is "POOLED_OUTPUT" or "FINAL_PACKAGING")
        {
            var input = await LockInputAsync(output.ProcessingExecutionId, ct);
            if (input is not null)
            {
                if (input.ConsumedQuantity < output.SourceContribution)
                {
                    return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
                }

                var consumeMovements = await ReadConsumeMovementsAsync(output.ProcessingExecutionId, ct);
                if (consumeMovements.Count != 1)
                {
                    return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
                }

                var consume = consumeMovements[0];
                var nextConsumedQuantity = input.ConsumedQuantity - output.SourceContribution;
                if (!await AdjustPositionAsync(consume, output.SourceContribution, ct))
                {
                    return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
                }

                if (nextConsumedQuantity == 0m)
                {
                    if (await DeleteMovementAsync(consume.Id, ct) != 1)
                    {
                        return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
                    }
                }
                else
                {
                    if (await UpdateConsumeMovementQuantityAsync(consume.Id, -nextConsumedQuantity, ct) != 1)
                    {
                        return Conflict(ProcessingTransactionLifecycleErrorCodes.OutputHardDeleteClosureAmbiguous);
                    }
                }

                if (await UpdateDerivedInputQuantityAsync(
                        output.ProcessingExecutionId,
                        input.RowVersion,
                        nextConsumedQuantity,
                        ct) != 1)
                {
                    return Conflict(ProcessingTransactionLifecycleErrorCodes.StaleRowVersion);
                }
            }
        }

        await CloseLaborSourcesAsync([output.OutputId], now, ct);

        return await DeleteOutputAsync(output.OutputId, expectedRowVersion, ct) == 1
            ? null
            : Conflict(ProcessingTransactionLifecycleErrorCodes.StaleRowVersion);
    }

    private async ValueTask<ApplicationError?> HardDeleteExecutionClosureAsync(
        Guid processingExecutionId,
        long expectedRowVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outputIds = await ReadExecutionOutputIdsAsync(processingExecutionId, ct);
        if (outputIds.Length > 0)
        {
            await CloseLaborSourcesAsync(outputIds, now, ct);
        }

        await ReverseExecutionInventoryProjectionAsync(processingExecutionId, ct);
        await DeletePendingProcessingOutboxAsync(processingExecutionId, ct);

        await using (var movements = CreateSqlCommand(
            """
            DELETE FROM inventory.inventory_movements movement
            USING inventory.inventory_operations operation
            WHERE movement.inventory_operation_id = operation.id
              AND operation.processing_execution_id = @execution_id;
            """))
        {
            movements.Parameters.AddWithValue("execution_id", processingExecutionId);
            await movements.ExecuteNonQueryAsync(ct);
        }

        await using (var operation = CreateSqlCommand(
            "DELETE FROM inventory.inventory_operations WHERE processing_execution_id = @execution_id;"))
        {
            operation.Parameters.AddWithValue("execution_id", processingExecutionId);
            await operation.ExecuteNonQueryAsync(ct);
        }

        await using (var input = CreateSqlCommand(
            "DELETE FROM processing.processing_execution_inputs WHERE processing_execution_id = @execution_id;"))
        {
            input.Parameters.AddWithValue("execution_id", processingExecutionId);
            await input.ExecuteNonQueryAsync(ct);
        }

        await using (var outputs = CreateSqlCommand(
            "DELETE FROM processing.processing_execution_outputs WHERE processing_execution_id = @execution_id;"))
        {
            outputs.Parameters.AddWithValue("execution_id", processingExecutionId);
            await outputs.ExecuteNonQueryAsync(ct);
        }

        await using var execution = CreateSqlCommand(
            """
            DELETE FROM processing.processing_executions
            WHERE id = @execution_id
              AND row_version = @expected_row_version;
            """);
        execution.Parameters.AddWithValue("execution_id", processingExecutionId);
        execution.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await execution.ExecuteNonQueryAsync(ct) == 1
            ? null
            : Conflict(ProcessingTransactionLifecycleErrorCodes.StaleRowVersion);
    }

    private async ValueTask<InputState?> LockInputAsync(Guid processingExecutionId, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version, consumed_quantity
            FROM processing.processing_execution_inputs
            WHERE processing_execution_id = @execution_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("execution_id", processingExecutionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new InputState(processingExecutionId, reader.GetInt64(0), reader.GetDecimal(1));
    }

    private async ValueTask<List<MovementState>> ReadConsumeMovementsAsync(Guid processingExecutionId, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT
                movement.id,
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
                movement.quantity_delta
            FROM inventory.inventory_operations operation
            JOIN inventory.inventory_movements movement
              ON movement.inventory_operation_id = operation.id
            WHERE operation.processing_execution_id = @execution_id
              AND movement.movement_type IN ('PROCESS_CONSUME', 'FINAL_PACKAGE_CONSUME')
            ORDER BY movement.sequence
            FOR UPDATE OF movement;
            """);
        command.Parameters.AddWithValue("execution_id", processingExecutionId);
        return await ReadMovementsAsync(command, ct);
    }

    private async ValueTask<List<MovementState>> ReadMatchingProduceMovementsAsync(Guid outputId, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT
                movement.id,
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
                movement.quantity_delta
            FROM processing.processing_execution_outputs output
            JOIN processing_config.processing_module_outputs definition
              ON definition.id = output.processing_module_output_id
            JOIN inventory.inventory_operations operation
              ON operation.processing_execution_id = output.processing_execution_id
            JOIN inventory.inventory_movements movement
              ON movement.inventory_operation_id = operation.id
            WHERE output.id = @output_id
              AND movement.movement_type IN ('PROCESS_PRODUCE', 'FINAL_PACKAGE_PRODUCE')
              AND movement.inventory_object_kind = output.output_kind_snapshot
              AND movement.process_material_id IS NOT DISTINCT FROM definition.process_material_id
              AND movement.sales_product_id IS NOT DISTINCT FROM definition.sales_product_id
              AND movement.quantity_delta = COALESCE(output.derived_net_quantity, output.completed_quantity)
            ORDER BY movement.sequence
            FOR UPDATE OF movement;
            """);
        command.Parameters.AddWithValue("output_id", outputId);
        return await ReadMovementsAsync(command, ct);
    }

    private static async ValueTask<List<MovementState>> ReadMovementsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var result = new List<MovementState>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new MovementState(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.GetDecimal(11)));
        }
        return result;
    }

    private async ValueTask<bool> AdjustPositionAsync(
        MovementState movement,
        decimal balanceDelta,
        CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE inventory.inventory_positions
            SET balance_quantity = balance_quantity + @balance_delta,
                row_version = row_version + 1
            WHERE origin = @origin
              AND procurement_batch_id IS NOT DISTINCT FROM @procurement_batch_id
              AND outsourced_supply_batch_id IS NOT DISTINCT FROM @outsourced_supply_batch_id
              AND inventory_object_kind = @object_kind
              AND procurement_product_id IS NOT DISTINCT FROM @procurement_product_id
              AND process_material_id IS NOT DISTINCT FROM @process_material_id
              AND sales_product_id IS NOT DISTINCT FROM @sales_product_id
              AND storage_location_id = @storage_location_id
              AND raw_source_kind IS NOT DISTINCT FROM @raw_source_kind
              AND supplier_id IS NOT DISTINCT FROM @supplier_id;
            """);
        AddMovementIdentityParameters(command, movement);
        command.Parameters.AddWithValue("balance_delta", balanceDelta);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    private static void AddMovementIdentityParameters(NpgsqlCommand command, MovementState movement)
    {
        command.Parameters.AddWithValue("origin", movement.Origin);
        command.Parameters.AddWithValue("procurement_batch_id", (object?)movement.ProcurementBatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("outsourced_supply_batch_id", (object?)movement.OutsourcedSupplyBatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("object_kind", movement.InventoryObjectKind);
        command.Parameters.AddWithValue("procurement_product_id", (object?)movement.ProcurementProductId ?? DBNull.Value);
        command.Parameters.AddWithValue("process_material_id", (object?)movement.ProcessMaterialId ?? DBNull.Value);
        command.Parameters.AddWithValue("sales_product_id", (object?)movement.SalesProductId ?? DBNull.Value);
        command.Parameters.AddWithValue("storage_location_id", movement.StorageLocationId);
        command.Parameters.AddWithValue("raw_source_kind", (object?)movement.RawSourceKind ?? DBNull.Value);
        command.Parameters.AddWithValue("supplier_id", (object?)movement.SupplierId ?? DBNull.Value);
    }

    private async ValueTask<int> DeleteMovementAsync(Guid movementId, CancellationToken ct)
    {
        await using var command = CreateSqlCommand("DELETE FROM inventory.inventory_movements WHERE id = @id;");
        command.Parameters.AddWithValue("id", movementId);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<int> UpdateConsumeMovementQuantityAsync(
        Guid movementId,
        decimal quantityDelta,
        CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            "UPDATE inventory.inventory_movements SET quantity_delta = @quantity_delta WHERE id = @id;");
        command.Parameters.AddWithValue("id", movementId);
        command.Parameters.AddWithValue("quantity_delta", quantityDelta);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<int> UpdateDerivedInputQuantityAsync(
        Guid processingExecutionId,
        long expectedRowVersion,
        decimal consumedQuantity,
        CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE processing.processing_execution_inputs
            SET consumed_quantity = @consumed_quantity,
                row_version = row_version + 1
            WHERE processing_execution_id = @execution_id
              AND row_version = @expected_row_version
              AND consumption_basis IN ('OUTPUT_QUANTITY', 'PACKAGING_WEIGHT');
            """);
        command.Parameters.AddWithValue("execution_id", processingExecutionId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        command.Parameters.AddWithValue("consumed_quantity", consumedQuantity);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<int> DeleteInputAsync(Guid processingExecutionId, long expectedRowVersion, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM processing.processing_execution_inputs
            WHERE processing_execution_id = @execution_id
              AND row_version = @expected_row_version;
            """);
        command.Parameters.AddWithValue("execution_id", processingExecutionId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<int> DeleteOutputAsync(Guid outputId, long expectedRowVersion, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM processing.processing_execution_outputs
            WHERE id = @output_id
              AND row_version = @expected_row_version;
            """);
        command.Parameters.AddWithValue("output_id", outputId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<Guid[]> ReadExecutionOutputIdsAsync(Guid processingExecutionId, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            "SELECT id FROM processing.processing_execution_outputs WHERE processing_execution_id = @execution_id ORDER BY id FOR UPDATE;");
        command.Parameters.AddWithValue("execution_id", processingExecutionId);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids.ToArray();
    }

    private async ValueTask ReverseExecutionInventoryProjectionAsync(Guid processingExecutionId, CancellationToken ct)
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
                WHERE operation.processing_execution_id = @execution_id
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
        command.Parameters.AddWithValue("execution_id", processingExecutionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask DeletePendingProcessingOutboxAsync(Guid processingExecutionId, CancellationToken ct)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'processing.execution.confirmed'
              AND published_at IS NULL
              AND payload ->> 'processingExecutionId' = @execution_id_text;
            """);
        command.Parameters.AddWithValue("execution_id_text", processingExecutionId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask CloseLaborSourcesAsync(
        Guid[] outputIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (outputIds.Length == 0) return;

        Guid[] componentIds;
        Guid[] dailyWageIds;
        await using (var command = CreateSqlCommand(
            """
            SELECT source.processing_wage_component_id, component.employee_daily_wage_id
            FROM labor.processing_wage_component_sources source
            JOIN labor.processing_wage_components component
              ON component.id = source.processing_wage_component_id
            WHERE source.processing_execution_output_id = ANY(@output_ids)
            ORDER BY source.processing_wage_component_id
            FOR UPDATE OF source, component;
            """))
        {
            command.Parameters.AddWithValue("output_ids", outputIds);
            var components = new HashSet<Guid>();
            var dailyWages = new HashSet<Guid>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                components.Add(reader.GetGuid(0));
                dailyWages.Add(reader.GetGuid(1));
            }
            componentIds = components.OrderBy(id => id).ToArray();
            dailyWageIds = dailyWages.OrderBy(id => id).ToArray();
        }

        if (componentIds.Length == 0) return;

        await using (var lockWages = CreateSqlCommand(
            "SELECT id FROM labor.employee_daily_wages WHERE id = ANY(@ids) ORDER BY id FOR UPDATE;"))
        {
            lockWages.Parameters.AddWithValue("ids", dailyWageIds);
            await using var reader = await lockWages.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }

        await using (var deleteSources = CreateSqlCommand(
            "DELETE FROM labor.processing_wage_component_sources WHERE processing_execution_output_id = ANY(@output_ids);"))
        {
            deleteSources.Parameters.AddWithValue("output_ids", outputIds);
            await deleteSources.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteEmptyComponents = CreateSqlCommand(
            """
            DELETE FROM labor.processing_wage_components component
            WHERE component.id = ANY(@component_ids)
              AND NOT EXISTS
                  (SELECT 1 FROM labor.processing_wage_component_sources source
                   WHERE source.processing_wage_component_id = component.id);
            """))
        {
            deleteEmptyComponents.Parameters.AddWithValue("component_ids", componentIds);
            await deleteEmptyComponents.ExecuteNonQueryAsync(ct);
        }

        await using (var recalcComponents = CreateSqlCommand(
            """
            UPDATE labor.processing_wage_components component
            SET aggregated_quantity = source_total.quantity,
                amount_thb = floor(source_total.quantity * component.applied_wage_rate)::bigint
            FROM
            (
                SELECT processing_wage_component_id, SUM(quantity_snapshot) AS quantity
                FROM labor.processing_wage_component_sources
                WHERE processing_wage_component_id = ANY(@component_ids)
                GROUP BY processing_wage_component_id
            ) source_total
            WHERE component.id = source_total.processing_wage_component_id;
            """))
        {
            recalcComponents.Parameters.AddWithValue("component_ids", componentIds);
            await recalcComponents.ExecuteNonQueryAsync(ct);
        }

        await using (var recalcWages = CreateSqlCommand(
            """
            UPDATE labor.employee_daily_wages wage
            SET processing_wage_total_thb = COALESCE(
                    (SELECT SUM(component.amount_thb)::bigint
                     FROM labor.processing_wage_components component
                     WHERE component.employee_daily_wage_id = wage.id), 0),
                total_wage_thb = COALESCE(
                    (SELECT SUM(component.amount_thb)::bigint
                     FROM labor.processing_wage_components component
                     WHERE component.employee_daily_wage_id = wage.id), 0)
                    + wage.sales_packaging_wage_total_thb,
                row_version = row_version + 1
            WHERE wage.id = ANY(@daily_wage_ids);
            """))
        {
            recalcWages.Parameters.AddWithValue("daily_wage_ids", dailyWageIds);
            await recalcWages.ExecuteNonQueryAsync(ct);
        }

        await using (var obligation = CreateSqlCommand(
            """
            UPDATE finance.payable_obligation_items obligation
            SET amount_thb = wage.total_wage_thb
            FROM labor.employee_daily_wages wage
            WHERE obligation.employee_daily_wage_id = wage.id
              AND wage.id = ANY(@daily_wage_ids);
            """))
        {
            obligation.Parameters.AddWithValue("daily_wage_ids", dailyWageIds);
            await obligation.ExecuteNonQueryAsync(ct);
        }

        await using (var outstanding = CreateSqlCommand(
            """
            UPDATE finance.payable_outstanding_positions position
            SET original_obligation_thb = wage.total_wage_thb,
                outstanding_thb = wage.total_wage_thb + position.adjustment_total_thb - position.settlement_total_thb,
                row_version = position.row_version + 1,
                updated_at = @updated_at
            FROM finance.payables payable
            JOIN labor.employee_daily_wages wage
              ON wage.id = payable.employee_daily_wage_id
            WHERE position.payable_id = payable.id
              AND wage.id = ANY(@daily_wage_ids);
            """))
        {
            outstanding.Parameters.AddWithValue("daily_wage_ids", dailyWageIds);
            outstanding.Parameters.AddWithValue("updated_at", now);
            await outstanding.ExecuteNonQueryAsync(ct);
        }

        await using var pendingOutbox = CreateSqlCommand(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'labor.employee-daily-wage.confirmed'
              AND published_at IS NULL
              AND payload ->> 'employeeDailyWageId' = ANY(@daily_wage_id_texts);
            """);
        pendingOutbox.Parameters.AddWithValue("daily_wage_id_texts", dailyWageIds.Select(id => id.ToString()).ToArray());
        await pendingOutbox.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        Guid commandId,
        CommandRequestHash requestHash,
        Guid actorAccountId,
        string commandType,
        DateTimeOffset startedAt,
        CancellationToken ct)
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
            insert.Parameters.AddWithValue("command_id", commandId);
            insert.Parameters.AddWithValue("command_type", commandType);
            insert.Parameters.AddWithValue("request_hash", requestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", actorAccountId);
            insert.Parameters.AddWithValue("started_at", startedAt);
            if (await insert.ExecuteNonQueryAsync(ct) == 1) return CommandAcquisition.Acquired();
        }

        await using var select = CreateSqlCommand(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", commandId);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Processing hard-delete command conflict could not be loaded.");

        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
            return CommandAcquisition.Conflict();

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
            throw new InvalidOperationException($"A committed {commandType} command is not replayable.");

        var replay = JsonSerializer.Deserialize<HardDeleteProcessingTransactionResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        Guid commandId,
        Guid actorAccountId,
        Guid id,
        TargetKind target,
        string commandType,
        long beforeRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKind = target switch
        {
            TargetKind.Execution => "processing.execution",
            TargetKind.Input => "processing.execution-input",
            TargetKind.Output => "processing.execution-output",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        var subjectKeyJson = JsonSerializer.Serialize(new { id }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'HARD_DELETE', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", commandId);
            auditEvent.Parameters.AddWithValue("command_type", commandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", actorAccountId);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(ct);
        }

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, @subject_kind, CAST(@subject_key AS jsonb), 'HARD_DELETE',
                 @before_row_version, NULL, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_kind", subjectKind);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        await subject.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask MarkCommandSucceededAsync(
        Guid commandId,
        string resultJson,
        DateTimeOffset executedAt,
        CancellationToken ct)
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
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Processing hard-delete command could not transition to SUCCEEDED.");
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Processing hard-delete SQL requires an active command transaction.");
        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static string NotFoundCode(TargetKind target) => target switch
    {
        TargetKind.Execution => ProcessingTransactionLifecycleErrorCodes.ExecutionNotFound,
        TargetKind.Input => ProcessingTransactionLifecycleErrorCodes.InputNotFound,
        TargetKind.Output => ProcessingTransactionLifecycleErrorCodes.OutputNotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static ApplicationError Conflict(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Conflict, code);

    private static CommandTransactionDecision<ApplicationResult<HardDeleteProcessingTransactionResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteProcessingTransactionResult>>.Rollback(
            ApplicationResult<HardDeleteProcessingTransactionResult>.Failure(ApplicationError.Create(kind, code)));

    private abstract record TargetState(Guid Id, long RowVersion);
    private sealed record ExecutionState(Guid ProcessingExecutionId, long Version)
        : TargetState(ProcessingExecutionId, Version);
    private sealed record InputState(Guid ProcessingExecutionId, long Version, decimal ConsumedQuantity)
        : TargetState(ProcessingExecutionId, Version);
    private sealed record OutputState(
        Guid OutputId,
        long Version,
        Guid ProcessingExecutionId,
        string ExecutionMode,
        decimal SourceContribution)
        : TargetState(OutputId, Version);

    private sealed record MovementState(
        Guid Id,
        string Origin,
        Guid? ProcurementBatchId,
        Guid? OutsourcedSupplyBatchId,
        string InventoryObjectKind,
        Guid? ProcurementProductId,
        Guid? ProcessMaterialId,
        Guid? SalesProductId,
        Guid StorageLocationId,
        string? RawSourceKind,
        Guid? SupplierId,
        decimal QuantityDelta);

    private enum TargetKind { Execution, Input, Output }
    private enum CommandAcquisitionKind { Acquired, Replay, Conflict }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        HardDeleteProcessingTransactionResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(HardDeleteProcessingTransactionResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
