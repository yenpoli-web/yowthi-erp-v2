using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Inventory;

public sealed record TransferInventoryCommand(
    InventoryPositionIdentity InventoryIdentity,
    Guid SourceStorageLocationId,
    Guid DestinationStorageLocationId,
    decimal Quantity);

public sealed record TransferInventoryExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    TransferInventoryCommand Command);

public sealed record TransferInventoryResult(
    Guid InventoryOperationId,
    Guid TransferOutMovementId,
    Guid TransferInMovementId,
    decimal Quantity);

public interface ITransferInventoryExecutor
{
    ValueTask<ApplicationResult<TransferInventoryResult>> ExecuteAsync(
        TransferInventoryExecution execution,
        CancellationToken cancellationToken);
}

public static class TransferInventoryValidation
{
    public static ApplicationError? Validate(TransferInventoryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.InventoryIdentity is null
            || !InventoryPositionIdentityValidation.IsValid(command.InventoryIdentity)
            || command.SourceStorageLocationId == Guid.Empty
            || command.DestinationStorageLocationId == Guid.Empty
            || command.SourceStorageLocationId == command.DestinationStorageLocationId
            || command.Quantity <= 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                InventoryApplicationErrorCodes.InvalidInput);
        }

        return null;
    }
}
