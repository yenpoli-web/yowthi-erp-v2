namespace YowThi.Erp.Domain.Finance;

public sealed class PayableObligationItem
{
    public Guid Id { get; private set; }
    public Guid PayableId { get; private set; }
    public PayableKind PayableKind { get; private set; }
    public PayableObligationKind ObligationKind { get; private set; }
    public long AmountThb { get; private set; }
    public Guid? ProcurementEntryId { get; private set; }
    public Guid? OutsourcedSupplyDetailId { get; private set; }
    public Guid? EmployeeDailyWageId { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public decimal? AggregatedApplicableQuantity { get; private set; }
    public decimal? AppliedRatePerKg { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static PayableObligationItem CreateProcurementEntry(
        Guid id,
        Guid payableId,
        PayableKind payableKind,
        Guid procurementEntryId,
        long amountThb,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty || payableId == Guid.Empty || procurementEntryId == Guid.Empty)
        {
            throw new ArgumentException("Payable obligation technical and reference IDs cannot be empty.");
        }

        if (payableKind is not (PayableKind.PROCUREMENT_SUPPLIER or PayableKind.PROCUREMENT_FARMER))
        {
            throw new ArgumentOutOfRangeException(nameof(payableKind), payableKind, "Procurement Entry obligations require a Procurement payable kind.");
        }

        return new PayableObligationItem
        {
            Id = id,
            PayableId = payableId,
            PayableKind = payableKind,
            ObligationKind = PayableObligationKind.PROCUREMENT_ENTRY,
            AmountThb = amountThb,
            ProcurementEntryId = procurementEntryId,
            RecordedAt = recordedAt,
        };
    }

    public static PayableObligationItem CreateOutsourcedSupplyDetail(
        Guid id,
        Guid payableId,
        Guid outsourcedSupplyDetailId,
        long amountThb,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty || payableId == Guid.Empty || outsourcedSupplyDetailId == Guid.Empty)
        {
            throw new ArgumentException("Payable obligation technical and reference IDs cannot be empty.");
        }

        return new PayableObligationItem
        {
            Id = id,
            PayableId = payableId,
            PayableKind = PayableKind.OUTSOURCED_VENDOR,
            ObligationKind = PayableObligationKind.OUTSOURCED_SUPPLY_DETAIL,
            AmountThb = amountThb,
            OutsourcedSupplyDetailId = outsourcedSupplyDetailId,
            RecordedAt = recordedAt,
        };
    }

    public static PayableObligationItem CreateEmployeeDailyWage(
        Guid id,
        Guid payableId,
        Guid employeeDailyWageId,
        long amountThb,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty
            || payableId == Guid.Empty
            || employeeDailyWageId == Guid.Empty
            || amountThb < 0)
        {
            throw new ArgumentException("Employee Daily Wage payable obligation is invalid.");
        }

        return new PayableObligationItem
        {
            Id = id,
            PayableId = payableId,
            PayableKind = PayableKind.EMPLOYEE_DAILY_WAGE,
            ObligationKind = PayableObligationKind.EMPLOYEE_DAILY_WAGE,
            AmountThb = amountThb,
            EmployeeDailyWageId = employeeDailyWageId,
            RecordedAt = recordedAt,
        };
    }
}
