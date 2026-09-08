using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record CreateCustomerCommand(
    string? NameZhTw,
    string? NameThTh,
    string? Phone,
    bool Active,
    string? Code = null);

public sealed record CreateCustomerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateCustomerCommand Command);

public sealed record UpdateCustomerCommand(
    Guid CustomerId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? Phone,
    bool Active,
    string? Code = null);

public sealed record UpdateCustomerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateCustomerCommand Command);

public sealed record CustomerMasterWriteResult(
    Guid CustomerId,
    long RowVersion);

public interface ICustomerMasterExecutor
{
    ValueTask<ApplicationResult<CustomerMasterWriteResult>> CreateAsync(
        CreateCustomerExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<CustomerMasterWriteResult>> UpdateAsync(
        UpdateCustomerExecution execution,
        CancellationToken cancellationToken);
}

public enum CustomerMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record CustomerMasterQuery(
    string? Search,
    CustomerMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record CustomerMasterItem(
    Guid Id,
    string? Code,
    string? NameZhTw,
    string? NameThTh,
    string? Phone,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record CustomerMasterPage(
    IReadOnlyList<CustomerMasterItem> Items,
    int? NextOffset);

public interface ICustomerMasterReader
{
    ValueTask<CustomerMasterPage> GetAsync(
        CustomerMasterQuery query,
        CancellationToken cancellationToken);
}

public static class CustomerMasterErrorCodes
{
    public const string InvalidInput = "party.customer.invalid-input";
    public const string CustomerNotFound = "party.customer.not-found";
    public const string CustomerDeleted = "party.customer.deleted";
    public const string StaleRowVersion = "party.customer.stale-row-version";
    public const string IdempotencyKeyReused = "party.customer.idempotency-key-reused";
}

public static class CustomerMasterValidation
{
    public static ApplicationError? Validate(CreateCustomerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateCustomerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.CustomerId != Guid.Empty
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
            CustomerMasterErrorCodes.InvalidInput);
}
