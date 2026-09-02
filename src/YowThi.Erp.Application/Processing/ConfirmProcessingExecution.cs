using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Domain.Processing;

namespace YowThi.Erp.Application.Processing;

public sealed record ConfirmProcessingExecutionCommand(
    DateOnly WorkDate,
    Guid EmployeeId,
    Guid ProcurementBatchId,
    Guid ProcessingModuleId,
    ProcessingSourceSelection? Source,
    ProcessingScaleMeasurement? InputScale,
    Guid? InputStorageLocationId,
    IReadOnlyList<ProcessingOutputMeasurement> Outputs);

public sealed record ProcessingSourceSelection(
    ProcessingSourceKind SourceKind,
    Guid? SupplierId);

public sealed record ProcessingScaleMeasurement(
    decimal ObservedScaleReading,
    int? ActualContainerCount);

public sealed record ProcessingOutputMeasurement(
    Guid ProcessingModuleOutputId,
    decimal? ObservedScaleReading,
    int? ActualContainerCount,
    decimal? CompletedQuantity,
    Guid? OutputStorageLocationId);

public sealed record ConfirmProcessingExecutionExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ConfirmProcessingExecutionCommand Command);

public sealed record ConfirmProcessingExecutionResult(
    Guid ProcessingExecutionId,
    Guid InventoryOperationId,
    long ProcessingExecutionRowVersion);

public interface IConfirmProcessingExecutionExecutor
{
    ValueTask<ApplicationResult<ConfirmProcessingExecutionResult>> ExecuteAsync(
        ConfirmProcessingExecutionExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcessingApplicationErrorCodes
{
    public const string EmployeeNotFound = "processing.employee-not-found";
    public const string EmployeeInactive = "processing.employee-inactive";
    public const string BatchNotFound = "processing.batch-not-found";
    public const string BatchUnavailable = "processing.batch-unavailable";
    public const string BatchRouteRequired = "processing.batch-route-required";
    public const string ModuleNotFound = "processing.module-not-found";
    public const string ModuleRouteMismatch = "processing.module-route-mismatch";
    public const string InvalidSourceShape = "processing.source-shape-invalid";
    public const string SupplierNotFound = "processing.supplier-not-found";
    public const string SupplierInactive = "processing.supplier-inactive";
    public const string InvalidInputScale = "processing.input-scale-invalid";
    public const string InputLocationNotFound = "processing.input-location-not-found";
    public const string InputLocationInactive = "processing.input-location-inactive";
    public const string InputLocationRequired = "processing.input-location-required";
    public const string OutputRequired = "processing.output-required";
    public const string OutputDefinitionInvalid = "processing.output-definition-invalid";
    public const string OutputMeasurementInvalid = "processing.output-measurement-invalid";
    public const string OutputLocationNotFound = "processing.output-location-not-found";
    public const string OutputLocationInactive = "processing.output-location-inactive";
    public const string OutputLocationRequired = "processing.output-location-required";
    public const string InsufficientInventory = "processing.insufficient-inventory";
    public const string NegativeInventoryPolicyUnsupported = "processing.negative-inventory-policy-unsupported";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ConfirmProcessingExecutionValidation
{
    public static ApplicationError? Validate(ConfirmProcessingExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.EmployeeId == Guid.Empty)
        {
            return Validation(ProcessingApplicationErrorCodes.EmployeeNotFound);
        }

        if (command.ProcurementBatchId == Guid.Empty)
        {
            return Validation(ProcessingApplicationErrorCodes.BatchNotFound);
        }

        if (command.ProcessingModuleId == Guid.Empty)
        {
            return Validation(ProcessingApplicationErrorCodes.ModuleNotFound);
        }

        if (command.InputStorageLocationId == Guid.Empty)
        {
            return Validation(ProcessingApplicationErrorCodes.InputLocationNotFound);
        }

        if (command.InputScale is { } input
            && (input.ObservedScaleReading < 0 || input.ActualContainerCount < 0))
        {
            return Validation(ProcessingApplicationErrorCodes.InvalidInputScale);
        }

        if (command.Outputs is null || command.Outputs.Count == 0)
        {
            return Validation(ProcessingApplicationErrorCodes.OutputRequired);
        }

        if (command.Outputs.Any(x =>
                x.ProcessingModuleOutputId == Guid.Empty
                || x.ObservedScaleReading < 0
                || x.ActualContainerCount < 0
                || x.CompletedQuantity < 0
                || x.OutputStorageLocationId == Guid.Empty))
        {
            return Validation(ProcessingApplicationErrorCodes.OutputMeasurementInvalid);
        }

        if (command.Outputs.Select(x => x.ProcessingModuleOutputId).Distinct().Count() != command.Outputs.Count)
        {
            return Validation(ProcessingApplicationErrorCodes.OutputDefinitionInvalid);
        }

        return null;
    }

    private static ApplicationError Validation(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Validation, code);
}
