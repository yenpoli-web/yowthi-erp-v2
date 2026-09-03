using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Inventory;

public sealed record AdjustInventoryCommand(
    InventoryPositionIdentity InventoryIdentity,
    Guid StorageLocationId,
    decimal QuantityDelta,
    string ReasonText);

public sealed record AdjustInventoryExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    AdjustInventoryCommand Command);

public sealed record AdjustInventoryResult(
    Guid InventoryOperationId,
    Guid AdjustmentMovementId,
    decimal QuantityDelta);

public interface IAdjustInventoryExecutor
{
    ValueTask<ApplicationResult<AdjustInventoryResult>> ExecuteAsync(
        AdjustInventoryExecution execution,
        CancellationToken cancellationToken);
}

public static class AdjustInventoryValidation
{
    public static ApplicationError? Validate(AdjustInventoryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.InventoryIdentity is null
            || !InventoryPositionIdentityValidation.IsValid(command.InventoryIdentity)
            || command.StorageLocationId == Guid.Empty
            || command.QuantityDelta == 0
            || string.IsNullOrWhiteSpace(command.ReasonText))
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                InventoryApplicationErrorCodes.InvalidInput);
        }

        return null;
    }
}
