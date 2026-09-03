namespace YowThi.Erp.Domain.Inventory;

public sealed class InventoryMovement
{
    public Guid Id { get; private set; }
    public Guid InventoryOperationId { get; private set; }
    public int Sequence { get; private set; }
    public InventoryMovementType MovementType { get; private set; }
    public InventoryOrigin Origin { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public Guid? OutsourcedSupplyBatchId { get; private set; }
    public InventoryObjectKind InventoryObjectKind { get; private set; }
    public Guid? ProcurementProductId { get; private set; }
    public Guid? ProcessMaterialId { get; private set; }
    public Guid? SalesProductId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public InventoryRawSourceKind? RawSourceKind { get; private set; }
    public Guid? SupplierId { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public Guid? SalesAllocationRevisionItemId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static InventoryMovement CreateProcurementReceipt(
        Guid id,
        Guid inventoryOperationId,
        Guid procurementBatchId,
        Guid procurementProductId,
        Guid storageLocationId,
        InventoryRawSourceKind rawSourceKind,
        Guid? supplierId,
        decimal quantity,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty
            || inventoryOperationId == Guid.Empty
            || procurementBatchId == Guid.Empty
            || procurementProductId == Guid.Empty
            || storageLocationId == Guid.Empty)
        {
            throw new ArgumentException("Inventory movement technical and reference IDs cannot be empty.");
        }

        return new InventoryMovement
        {
            Id = id,
            InventoryOperationId = inventoryOperationId,
            Sequence = 1,
            MovementType = InventoryMovementType.PURCHASE_RECEIPT,
            Origin = InventoryOrigin.IN_HOUSE,
            ProcurementBatchId = procurementBatchId,
            InventoryObjectKind = InventoryObjectKind.PROCUREMENT_PRODUCT,
            ProcurementProductId = procurementProductId,
            StorageLocationId = storageLocationId,
            RawSourceKind = rawSourceKind,
            SupplierId = supplierId,
            QuantityDelta = quantity,
            RecordedAt = recordedAt,
        };
    }

    public static InventoryMovement CreateOutsourcedReceipt(
        Guid id,
        Guid inventoryOperationId,
        Guid outsourcedSupplyBatchId,
        Guid salesProductId,
        Guid storageLocationId,
        decimal quantity,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty
            || inventoryOperationId == Guid.Empty
            || outsourcedSupplyBatchId == Guid.Empty
            || salesProductId == Guid.Empty
            || storageLocationId == Guid.Empty)
        {
            throw new ArgumentException("Inventory movement technical and reference IDs cannot be empty.");
        }

        return new InventoryMovement
        {
            Id = id,
            InventoryOperationId = inventoryOperationId,
            Sequence = 1,
            MovementType = InventoryMovementType.OUTSOURCED_RECEIPT,
            Origin = InventoryOrigin.OUTSOURCED,
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId,
            InventoryObjectKind = InventoryObjectKind.SALES_PRODUCT,
            SalesProductId = salesProductId,
            StorageLocationId = storageLocationId,
            QuantityDelta = quantity,
            RecordedAt = recordedAt,
        };
    }

    public static InventoryMovement CreateSalesIssue(
        Guid id,
        Guid inventoryOperationId,
        int sequence,
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        Guid salesProductId,
        Guid storageLocationId,
        decimal quantity,
        Guid salesAllocationRevisionItemId,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty
            || inventoryOperationId == Guid.Empty
            || salesProductId == Guid.Empty
            || storageLocationId == Guid.Empty
            || salesAllocationRevisionItemId == Guid.Empty)
        {
            throw new ArgumentException("Inventory movement technical and reference IDs cannot be empty.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Inventory movement sequence must be positive.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Sales issue quantity must be positive before sign inversion.");
        }

        var sourceShapeIsValid = origin switch
        {
            InventoryOrigin.IN_HOUSE => procurementBatchId is { } procurementId
                && procurementId != Guid.Empty
                && outsourcedSupplyBatchId is null,
            InventoryOrigin.OUTSOURCED => outsourcedSupplyBatchId is { } outsourcedId
                && outsourcedId != Guid.Empty
                && procurementBatchId is null,
            _ => false,
        };

        if (!sourceShapeIsValid)
        {
            throw new ArgumentException("Sales issue source batch shape is invalid.");
        }

        return new InventoryMovement
        {
            Id = id,
            InventoryOperationId = inventoryOperationId,
            Sequence = sequence,
            MovementType = InventoryMovementType.SALES_ISSUE,
            Origin = origin,
            ProcurementBatchId = procurementBatchId,
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId,
            InventoryObjectKind = InventoryObjectKind.SALES_PRODUCT,
            SalesProductId = salesProductId,
            StorageLocationId = storageLocationId,
            QuantityDelta = -quantity,
            SalesAllocationRevisionItemId = salesAllocationRevisionItemId,
            RecordedAt = recordedAt,
        };
    }

    public static InventoryMovement CreateTransferOut(
        Guid id,
        Guid inventoryOperationId,
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        InventoryObjectKind inventoryObjectKind,
        Guid? procurementProductId,
        Guid? processMaterialId,
        Guid? salesProductId,
        Guid storageLocationId,
        InventoryRawSourceKind? rawSourceKind,
        Guid? supplierId,
        decimal quantity,
        DateTimeOffset recordedAt)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Transfer quantity must be positive before sign inversion.");
        }

        return CreateManualMovement(
            id,
            inventoryOperationId,
            1,
            InventoryMovementType.TRANSFER_OUT,
            origin,
            procurementBatchId,
            outsourcedSupplyBatchId,
            inventoryObjectKind,
            procurementProductId,
            processMaterialId,
            salesProductId,
            storageLocationId,
            rawSourceKind,
            supplierId,
            -quantity,
            recordedAt);
    }

    public static InventoryMovement CreateTransferIn(
        Guid id,
        Guid inventoryOperationId,
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        InventoryObjectKind inventoryObjectKind,
        Guid? procurementProductId,
        Guid? processMaterialId,
        Guid? salesProductId,
        Guid storageLocationId,
        InventoryRawSourceKind? rawSourceKind,
        Guid? supplierId,
        decimal quantity,
        DateTimeOffset recordedAt)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Transfer quantity must be positive.");
        }

        return CreateManualMovement(
            id,
            inventoryOperationId,
            2,
            InventoryMovementType.TRANSFER_IN,
            origin,
            procurementBatchId,
            outsourcedSupplyBatchId,
            inventoryObjectKind,
            procurementProductId,
            processMaterialId,
            salesProductId,
            storageLocationId,
            rawSourceKind,
            supplierId,
            quantity,
            recordedAt);
    }

    public static InventoryMovement CreateAdjustment(
        Guid id,
        Guid inventoryOperationId,
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        InventoryObjectKind inventoryObjectKind,
        Guid? procurementProductId,
        Guid? processMaterialId,
        Guid? salesProductId,
        Guid storageLocationId,
        InventoryRawSourceKind? rawSourceKind,
        Guid? supplierId,
        decimal quantityDelta,
        DateTimeOffset recordedAt)
    {
        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Inventory adjustment delta cannot be zero.");
        }

        return CreateManualMovement(
            id,
            inventoryOperationId,
            1,
            InventoryMovementType.ADJUSTMENT,
            origin,
            procurementBatchId,
            outsourcedSupplyBatchId,
            inventoryObjectKind,
            procurementProductId,
            processMaterialId,
            salesProductId,
            storageLocationId,
            rawSourceKind,
            supplierId,
            quantityDelta,
            recordedAt);
    }

    private static InventoryMovement CreateManualMovement(
        Guid id,
        Guid inventoryOperationId,
        int sequence,
        InventoryMovementType movementType,
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        InventoryObjectKind inventoryObjectKind,
        Guid? procurementProductId,
        Guid? processMaterialId,
        Guid? salesProductId,
        Guid storageLocationId,
        InventoryRawSourceKind? rawSourceKind,
        Guid? supplierId,
        decimal quantityDelta,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty || inventoryOperationId == Guid.Empty || storageLocationId == Guid.Empty)
        {
            throw new ArgumentException("Inventory movement technical and location IDs cannot be empty.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        var signIsValid = movementType switch
        {
            InventoryMovementType.TRANSFER_OUT => quantityDelta < 0,
            InventoryMovementType.TRANSFER_IN => quantityDelta > 0,
            InventoryMovementType.ADJUSTMENT => quantityDelta != 0,
            _ => false,
        };

        if (!signIsValid)
        {
            throw new ArgumentException("Inventory manual movement sign is invalid.");
        }

        if (!HasValidIdentity(
                origin,
                procurementBatchId,
                outsourcedSupplyBatchId,
                inventoryObjectKind,
                procurementProductId,
                processMaterialId,
                salesProductId,
                rawSourceKind,
                supplierId))
        {
            throw new ArgumentException("Inventory movement identity shape is invalid.");
        }

        return new InventoryMovement
        {
            Id = id,
            InventoryOperationId = inventoryOperationId,
            Sequence = sequence,
            MovementType = movementType,
            Origin = origin,
            ProcurementBatchId = procurementBatchId,
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId,
            InventoryObjectKind = inventoryObjectKind,
            ProcurementProductId = procurementProductId,
            ProcessMaterialId = processMaterialId,
            SalesProductId = salesProductId,
            StorageLocationId = storageLocationId,
            RawSourceKind = rawSourceKind,
            SupplierId = supplierId,
            QuantityDelta = quantityDelta,
            RecordedAt = recordedAt,
        };
    }

    private static bool HasValidIdentity(
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        InventoryObjectKind inventoryObjectKind,
        Guid? procurementProductId,
        Guid? processMaterialId,
        Guid? salesProductId,
        InventoryRawSourceKind? rawSourceKind,
        Guid? supplierId)
    {
        var sourceBatchIsValid = origin switch
        {
            InventoryOrigin.IN_HOUSE => IsPresent(procurementBatchId) && outsourcedSupplyBatchId is null,
            InventoryOrigin.OUTSOURCED => IsPresent(outsourcedSupplyBatchId) && procurementBatchId is null,
            _ => false,
        };

        var inventoryObjectIsValid = inventoryObjectKind switch
        {
            InventoryObjectKind.PROCUREMENT_PRODUCT => IsPresent(procurementProductId)
                && processMaterialId is null
                && salesProductId is null,
            InventoryObjectKind.PROCESS_MATERIAL => procurementProductId is null
                && IsPresent(processMaterialId)
                && salesProductId is null,
            InventoryObjectKind.SALES_PRODUCT => procurementProductId is null
                && processMaterialId is null
                && IsPresent(salesProductId),
            _ => false,
        };

        var rawSourceIsValid = rawSourceKind switch
        {
            null => supplierId is null,
            InventoryRawSourceKind.SUPPLIER => origin == InventoryOrigin.IN_HOUSE
                && inventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                && IsPresent(supplierId),
            InventoryRawSourceKind.FARMERS_COMBINED => origin == InventoryOrigin.IN_HOUSE
                && inventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                && supplierId is null,
            _ => false,
        };

        return sourceBatchIsValid && inventoryObjectIsValid && rawSourceIsValid;
    }

    private static bool IsPresent(Guid? value) => value is { } id && id != Guid.Empty;
}
