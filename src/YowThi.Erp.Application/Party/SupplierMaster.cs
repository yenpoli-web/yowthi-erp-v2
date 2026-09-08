using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record CreateSupplierCommand(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record CreateSupplierExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateSupplierCommand Command);

public sealed record UpdateSupplierCommand(
    Guid SupplierId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record UpdateSupplierExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateSupplierCommand Command);

public sealed record SupplierMasterWriteResult(
    Guid SupplierId,
    long RowVersion);

public interface ISupplierMasterExecutor
{
    ValueTask<ApplicationResult<SupplierMasterWriteResult>> CreateAsync(
        CreateSupplierExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SupplierMasterWriteResult>> UpdateAsync(
        UpdateSupplierExecution execution,
        CancellationToken cancellationToken);
}

public enum SupplierMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record SupplierMasterQuery(
    string? Search,
    SupplierMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record SupplierMasterItem(
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

public sealed record SupplierMasterPage(
    IReadOnlyList<SupplierMasterItem> Items,
    int? NextOffset);

public interface ISupplierMasterReader
{
    ValueTask<SupplierMasterPage> GetAsync(
        SupplierMasterQuery query,
        CancellationToken cancellationToken);
}

public static class SupplierMasterErrorCodes
{
    public const string InvalidInput = "party.supplier.invalid-input";
    public const string SupplierNotFound = "party.supplier.not-found";
    public const string SupplierDeleted = "party.supplier.deleted";
    public const string StaleRowVersion = "party.supplier.stale-row-version";
    public const string IdempotencyKeyReused = "party.supplier.idempotency-key-reused";
}

public static class SupplierMasterValidation
{
    public static ApplicationError? Validate(CreateSupplierCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateSupplierCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.SupplierId != Guid.Empty
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
            SupplierMasterErrorCodes.InvalidInput);
}
