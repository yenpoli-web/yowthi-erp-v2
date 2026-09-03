using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Labor;

public sealed record ProcessingWageRateOverride(
    Guid ProcessingModuleOutputId,
    decimal ConfiguredWageRateSnapshot,
    decimal AppliedWageRate);

public sealed record ConfirmEmployeeDailyWageCommand(
    DateOnly WorkDate,
    Guid EmployeeId,
    IReadOnlyList<ProcessingWageRateOverride> ProcessingWageRateOverrides);

public sealed record ConfirmEmployeeDailyWageExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ConfirmEmployeeDailyWageCommand Command);

public sealed record ConfirmEmployeeDailyWageResult(
    Guid EmployeeDailyWageId,
    long RowVersion,
    Guid PayableId,
    long ProcessingWageTotalThb,
    long SalesPackagingWageTotalThb,
    long TotalWageThb);

public interface IConfirmEmployeeDailyWageExecutor
{
    ValueTask<ApplicationResult<ConfirmEmployeeDailyWageResult>> ExecuteAsync(
        ConfirmEmployeeDailyWageExecution execution,
        CancellationToken cancellationToken);
}

public static class LaborApplicationErrorCodes
{
    public const string InvalidInput = "labor.invalid-input";
    public const string EmployeeNotFound = "labor.employee-not-found";
    public const string DailyWageAlreadyConfirmed = "labor.daily-wage-already-confirmed";
    public const string ProcessingRateOverrideInvalid = "labor.processing-rate-override-invalid";
    public const string ProcessingRateOverrideTargetNotFound = "labor.processing-rate-override-target-not-found";
    public const string WageAmountInvalid = "labor.wage-amount-invalid";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ConfirmEmployeeDailyWageValidation
{
    public static ApplicationError? Validate(ConfirmEmployeeDailyWageCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.EmployeeId == Guid.Empty || command.ProcessingWageRateOverrides is null)
        {
            return Validation(LaborApplicationErrorCodes.InvalidInput);
        }

        var targets = new HashSet<(Guid ProcessingModuleOutputId, decimal ConfiguredWageRateSnapshot)>();
        foreach (var rateOverride in command.ProcessingWageRateOverrides)
        {
            if (rateOverride is null
                || rateOverride.ProcessingModuleOutputId == Guid.Empty
                || rateOverride.ConfiguredWageRateSnapshot < 0
                || rateOverride.AppliedWageRate < 0
                || !targets.Add((rateOverride.ProcessingModuleOutputId, rateOverride.ConfiguredWageRateSnapshot)))
            {
                return Validation(LaborApplicationErrorCodes.ProcessingRateOverrideInvalid);
            }
        }

        return null;
    }

    private static ApplicationError Validation(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Validation, code);
}
