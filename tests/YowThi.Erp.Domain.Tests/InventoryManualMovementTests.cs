using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Domain.Tests;

public sealed class InventoryManualMovementTests
{
    [Fact]
    public void Transfer_factories_preserve_full_inventory_identity_and_opposite_signs()
    {
        var operationId = Guid.CreateVersion7();
        var procurementBatchId = Guid.CreateVersion7();
        var procurementProductId = Guid.CreateVersion7();
        var supplierId = Guid.CreateVersion7();
        var sourceLocationId = Guid.CreateVersion7();
        var destinationLocationId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var operation = InventoryOperation.CreateTransfer(operationId, now, actorId);
        var transferOut = InventoryMovement.CreateTransferOut(
            Guid.CreateVersion7(),
            operationId,
            InventoryOrigin.IN_HOUSE,
            procurementBatchId,
            null,
            InventoryObjectKind.PROCUREMENT_PRODUCT,
            procurementProductId,
            null,
            null,
            sourceLocationId,
            InventoryRawSourceKind.SUPPLIER,
            supplierId,
            12.5m,
            now);
        var transferIn = InventoryMovement.CreateTransferIn(
            Guid.CreateVersion7(),
            operationId,
            InventoryOrigin.IN_HOUSE,
            procurementBatchId,
            null,
            InventoryObjectKind.PROCUREMENT_PRODUCT,
            procurementProductId,
            null,
            null,
            destinationLocationId,
            InventoryRawSourceKind.SUPPLIER,
            supplierId,
            12.5m,
            now);

        Assert.Equal(InventoryOperationType.TRANSFER, operation.OperationType);
        Assert.Equal(InventoryMovementType.TRANSFER_OUT, transferOut.MovementType);
        Assert.Equal(1, transferOut.Sequence);
        Assert.Equal(-12.5m, transferOut.QuantityDelta);
        Assert.Equal(sourceLocationId, transferOut.StorageLocationId);
        Assert.Equal(InventoryMovementType.TRANSFER_IN, transferIn.MovementType);
        Assert.Equal(2, transferIn.Sequence);
        Assert.Equal(12.5m, transferIn.QuantityDelta);
        Assert.Equal(destinationLocationId, transferIn.StorageLocationId);

        Assert.Equal(transferOut.Origin, transferIn.Origin);
        Assert.Equal(transferOut.ProcurementBatchId, transferIn.ProcurementBatchId);
        Assert.Equal(transferOut.OutsourcedSupplyBatchId, transferIn.OutsourcedSupplyBatchId);
        Assert.Equal(transferOut.InventoryObjectKind, transferIn.InventoryObjectKind);
        Assert.Equal(transferOut.ProcurementProductId, transferIn.ProcurementProductId);
        Assert.Equal(transferOut.ProcessMaterialId, transferIn.ProcessMaterialId);
        Assert.Equal(transferOut.SalesProductId, transferIn.SalesProductId);
        Assert.Equal(transferOut.RawSourceKind, transferIn.RawSourceKind);
        Assert.Equal(transferOut.SupplierId, transferIn.SupplierId);
    }

    [Fact]
    public void Adjustment_factory_preserves_signed_delta_and_identity()
    {
        var operationId = Guid.CreateVersion7();
        var outsourcedBatchId = Guid.CreateVersion7();
        var salesProductId = Guid.CreateVersion7();
        var locationId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var operation = InventoryOperation.CreateAdjustment(operationId, now, actorId);
        var movement = InventoryMovement.CreateAdjustment(
            Guid.CreateVersion7(),
            operationId,
            InventoryOrigin.OUTSOURCED,
            null,
            outsourcedBatchId,
            InventoryObjectKind.SALES_PRODUCT,
            null,
            null,
            salesProductId,
            locationId,
            null,
            null,
            -3m,
            now);

        Assert.Equal(InventoryOperationType.ADJUSTMENT, operation.OperationType);
        Assert.Equal(InventoryMovementType.ADJUSTMENT, movement.MovementType);
        Assert.Equal(1, movement.Sequence);
        Assert.Equal(-3m, movement.QuantityDelta);
        Assert.Equal(InventoryOrigin.OUTSOURCED, movement.Origin);
        Assert.Equal(outsourcedBatchId, movement.OutsourcedSupplyBatchId);
        Assert.Equal(InventoryObjectKind.SALES_PRODUCT, movement.InventoryObjectKind);
        Assert.Equal(salesProductId, movement.SalesProductId);
        Assert.Equal(locationId, movement.StorageLocationId);
    }

    [Fact]
    public void Manual_movement_rejects_invalid_source_batch_shape()
    {
        Assert.Throws<ArgumentException>(() => InventoryMovement.CreateTransferIn(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            InventoryOrigin.OUTSOURCED,
            Guid.CreateVersion7(),
            null,
            InventoryObjectKind.SALES_PRODUCT,
            null,
            null,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            null,
            1m,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Manual_movement_rejects_raw_source_on_non_raw_inventory_object()
    {
        Assert.Throws<ArgumentException>(() => InventoryMovement.CreateAdjustment(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            InventoryOrigin.IN_HOUSE,
            Guid.CreateVersion7(),
            null,
            InventoryObjectKind.PROCESS_MATERIAL,
            null,
            Guid.CreateVersion7(),
            null,
            Guid.CreateVersion7(),
            InventoryRawSourceKind.SUPPLIER,
            Guid.CreateVersion7(),
            1m,
            DateTimeOffset.UtcNow));
    }
}
