using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class EfProcurementReceiptDestinationResolver(ErpDbContext dbContext)
    : IProcurementReceiptDestinationResolver
{
    public async ValueTask<ApplicationResult<ProcurementReceiptDestination>> ResolveAsync(
        DateOnly procurementDate,
        Guid procurementProductId,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<ProcurementProduct>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == procurementProductId)
            .Select(candidate => new
            {
                candidate.Active,
                candidate.DeletedAt,
                candidate.DefaultStorageLocationId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return Failure(ApplicationErrorKind.NotFound, ProcurementApplicationErrorCodes.ProductNotFound);
        }

        if (!product.Active || product.DeletedAt is not null)
        {
            return Failure(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.ProductInactive);
        }

        var batch = await dbContext.Set<ProcurementBatch>()
            .AsNoTracking()
            .Where(candidate => candidate.ProcurementDate == procurementDate
                && candidate.ProcurementProductId == procurementProductId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ReceiptStorageLocationId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        var storageLocationId = batch?.ReceiptStorageLocationId;
        var resolutionSource = storageLocationId.HasValue ? "BATCH" : "PRODUCT_DEFAULT";

        if (batch is not null && storageLocationId is null)
        {
            var legacyLocations = await dbContext.Set<InventoryMovement>()
                .AsNoTracking()
                .Where(movement => movement.ProcurementBatchId == batch.Id
                    && movement.MovementType == InventoryMovementType.PURCHASE_RECEIPT)
                .Select(movement => movement.StorageLocationId)
                .Distinct()
                .Take(2)
                .ToArrayAsync(cancellationToken);

            if (legacyLocations.Length > 1)
            {
                return Failure(ApplicationErrorKind.Conflict, ProcurementReceiptDestinationErrorCodes.Ambiguous);
            }

            if (legacyLocations.Length == 1)
            {
                storageLocationId = legacyLocations[0];
                resolutionSource = "LEGACY_BATCH_MOVEMENT";
            }
        }

        storageLocationId ??= product.DefaultStorageLocationId;
        if (storageLocationId is not Guid locationId)
        {
            return Failure(ApplicationErrorKind.Validation, ProcurementApplicationErrorCodes.ReceiptLocationRequired);
        }

        var destination = await (
            from location in dbContext.Set<StorageLocation>().AsNoTracking()
            join warehouse in dbContext.Set<Warehouse>().AsNoTracking()
                on location.WarehouseId equals warehouse.Id
            where location.Id == locationId
            select new { Location = location, Warehouse = warehouse })
            .SingleOrDefaultAsync(cancellationToken);

        if (destination is null)
        {
            return Failure(ApplicationErrorKind.NotFound, ProcurementApplicationErrorCodes.ReceiptLocationNotFound);
        }

        if (!destination.Location.Active || destination.Location.DeletedAt is not null)
        {
            return Failure(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.ReceiptLocationInactive);
        }

        return ApplicationResult<ProcurementReceiptDestination>.Success(
            new ProcurementReceiptDestination(
                destination.Location.Id,
                destination.Warehouse.Id,
                destination.Warehouse.NameZhTw,
                destination.Warehouse.NameThTh,
                resolutionSource));
    }

    private static ApplicationResult<ProcurementReceiptDestination> Failure(
        ApplicationErrorKind kind,
        string code) =>
        ProcurementReceiptDestinationFailures.Failure(kind, code);
}
