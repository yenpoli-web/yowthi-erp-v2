namespace YowThi.Erp.Domain.Inventory;

public sealed class InventoryOperation
{
    public Guid Id { get; private set; }
    public InventoryOperationType OperationType { get; private set; }
    public Guid? ProcurementEntryId { get; private set; }
    public Guid? ProcessingExecutionId { get; private set; }
    public Guid? OutsourcedSupplyDetailId { get; private set; }
    public Guid? SalesId { get; private set; }
    public Guid? SalesAllocationRevisionId { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }

    public static InventoryOperation CreateProcurementReceipt(
        Guid id,
        Guid procurementEntryId,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty || procurementEntryId == Guid.Empty || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Inventory operation technical and reference IDs cannot be empty.");
        }

        return new InventoryOperation
        {
            Id = id,
            OperationType = InventoryOperationType.PROCUREMENT_RECEIPT,
            ProcurementEntryId = procurementEntryId,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
        };
    }

    public static InventoryOperation CreateProcessing(
        Guid id,
        Guid processingExecutionId,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty || processingExecutionId == Guid.Empty || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Inventory operation technical and reference IDs cannot be empty.");
        }

        return new InventoryOperation
        {
            Id = id,
            OperationType = InventoryOperationType.PROCESSING,
            ProcessingExecutionId = processingExecutionId,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
        };
    }

    public static InventoryOperation CreateOutsourcedReceipt(
        Guid id,
        Guid outsourcedSupplyDetailId,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty || outsourcedSupplyDetailId == Guid.Empty || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Inventory operation technical and reference IDs cannot be empty.");
        }

        return new InventoryOperation
        {
            Id = id,
            OperationType = InventoryOperationType.OUTSOURCED_RECEIPT,
            OutsourcedSupplyDetailId = outsourcedSupplyDetailId,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
        };
    }

    public static InventoryOperation CreateSalesIssue(
        Guid id,
        Guid salesId,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty || salesId == Guid.Empty || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Inventory operation technical and reference IDs cannot be empty.");
        }

        return new InventoryOperation
        {
            Id = id,
            OperationType = InventoryOperationType.SALES_ISSUE,
            SalesId = salesId,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
        };
    }

    public static InventoryOperation CreateTransfer(
        Guid id,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId) =>
        CreateStandalone(id, InventoryOperationType.TRANSFER, recordedAt, recordedByAccountId);

    public static InventoryOperation CreateAdjustment(
        Guid id,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId) =>
        CreateStandalone(id, InventoryOperationType.ADJUSTMENT, recordedAt, recordedByAccountId);

    private static InventoryOperation CreateStandalone(
        Guid id,
        InventoryOperationType operationType,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Inventory operation technical and actor IDs cannot be empty.");
        }

        if (operationType is not InventoryOperationType.TRANSFER and not InventoryOperationType.ADJUSTMENT)
        {
            throw new ArgumentOutOfRangeException(nameof(operationType));
        }

        return new InventoryOperation
        {
            Id = id,
            OperationType = operationType,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
        };
    }
}
