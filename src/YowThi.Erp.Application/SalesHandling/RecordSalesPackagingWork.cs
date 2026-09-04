using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.SalesHandling;

public sealed record RecordSalesPackagingWorkCommand(
    Guid SalesId,
    DateOnly WorkDate,
    Guid EmployeeId,
    Guid SalesPackagingItemId,
    long ConfirmedWageThb);

public sealed record RecordSalesPackagingWorkExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RecordSalesPackagingWorkCommand Command);

public sealed record RecordSalesPackagingWorkResult(
    Guid SalesPackagingWorkRecordId,
    long RowVersion);

public interface IRecordSalesPackagingWorkExecutor
{
    ValueTask<ApplicationResult<RecordSalesPackagingWorkResult>> ExecuteAsync(
        RecordSalesPackagingWorkExecution execution,
        CancellationToken cancellationToken);
}

public static class SalesHandlingApplicationErrorCodes
{
    public const string InvalidInput = "sales-handling.invalid-input";
    public const string SalesNotFound = "sales-handling.sales-not-found";
    public const string EmployeeNotFound = "sales-handling.employee-not-found";
    public const string EmployeeInactive = "sales-handling.employee-inactive";
    public const string ItemNotFound = "sales-handling.item-not-found";
    public const string ItemInactive = "sales-handling.item-inactive";
    public const string SalesStateNotAllowed = "sales-handling.sales-state-not-allowed";
    public const string DailyWageAlreadyConfirmed = "labor.daily-wage-already-confirmed";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class RecordSalesPackagingWorkValidation
{
    public static ApplicationError? Validate(RecordSalesPackagingWorkCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SalesId == Guid.Empty
            || command.EmployeeId == Guid.Empty
            || command.SalesPackagingItemId == Guid.Empty
            || command.ConfirmedWageThb < 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SalesHandlingApplicationErrorCodes.InvalidInput);
        }

        // HANDLING-001 confirmed 2026-09-03: DRAFT and CONFIRMED Sales are both allowed.
        // The execution layer validates the current persisted Sales lifecycle value.
        return null;
    }
}
