using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record CreateEmployeeCommand(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record CreateEmployeeExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateEmployeeCommand Command);

public sealed record UpdateEmployeeCommand(
    Guid EmployeeId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record UpdateEmployeeExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateEmployeeCommand Command);

public sealed record EmployeeMasterWriteResult(
    Guid EmployeeId,
    long RowVersion);

public interface IEmployeeMasterExecutor
{
    ValueTask<ApplicationResult<EmployeeMasterWriteResult>> CreateAsync(
        CreateEmployeeExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<EmployeeMasterWriteResult>> UpdateAsync(
        UpdateEmployeeExecution execution,
        CancellationToken cancellationToken);
}

public enum EmployeeMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record EmployeeMasterQuery(
    string? Search,
    EmployeeMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record EmployeeMasterItem(
    Guid Id,
    string? Code,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record EmployeeMasterPage(
    IReadOnlyList<EmployeeMasterItem> Items,
    int? NextOffset);

public interface IEmployeeMasterReader
{
    ValueTask<EmployeeMasterPage> GetAsync(
        EmployeeMasterQuery query,
        CancellationToken cancellationToken);
}

public static class EmployeeMasterErrorCodes
{
    public const string InvalidInput = "party.employee.invalid-input";
    public const string EmployeeNotFound = "party.employee.not-found";
    public const string EmployeeDeleted = "party.employee.deleted";
    public const string StaleRowVersion = "party.employee.stale-row-version";
    public const string IdempotencyKeyReused = "party.employee.idempotency-key-reused";
}

public static class EmployeeMasterValidation
{
    public static ApplicationError? Validate(CreateEmployeeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateEmployeeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.EmployeeId != Guid.Empty
            && command.ExpectedRowVersion >= 1
            && HasVisibleName(command.NameZhTw, command.NameThTh)
                ? null
                : Invalid();
    }

    private static bool HasVisibleName(string? nameZhTw, string? nameThTh) =>
        !string.IsNullOrWhiteSpace(nameZhTw) || !string.IsNullOrWhiteSpace(nameThTh);

    private static ApplicationError Invalid() =>
        ApplicationError.Create(
            ApplicationErrorKind.Validation,
            EmployeeMasterErrorCodes.InvalidInput);
}
