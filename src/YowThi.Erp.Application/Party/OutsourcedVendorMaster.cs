using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record CreateOutsourcedVendorCommand(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record CreateOutsourcedVendorExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateOutsourcedVendorCommand Command);

public sealed record UpdateOutsourcedVendorCommand(
    Guid OutsourcedVendorId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record UpdateOutsourcedVendorExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateOutsourcedVendorCommand Command);

public sealed record OutsourcedVendorMasterWriteResult(
    Guid OutsourcedVendorId,
    long RowVersion);

public interface IOutsourcedVendorMasterExecutor
{
    ValueTask<ApplicationResult<OutsourcedVendorMasterWriteResult>> CreateAsync(
        CreateOutsourcedVendorExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<OutsourcedVendorMasterWriteResult>> UpdateAsync(
        UpdateOutsourcedVendorExecution execution,
        CancellationToken cancellationToken);
}

public enum OutsourcedVendorMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record OutsourcedVendorMasterQuery(
    string? Search,
    OutsourcedVendorMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record OutsourcedVendorMasterItem(
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

public sealed record OutsourcedVendorMasterPage(
    IReadOnlyList<OutsourcedVendorMasterItem> Items,
    int? NextOffset);

public interface IOutsourcedVendorMasterReader
{
    ValueTask<OutsourcedVendorMasterPage> GetAsync(
        OutsourcedVendorMasterQuery query,
        CancellationToken cancellationToken);
}

public static class OutsourcedVendorMasterErrorCodes
{
    public const string InvalidInput = "party.outsourced-vendor.invalid-input";
    public const string OutsourcedVendorNotFound = "party.outsourced-vendor.not-found";
    public const string OutsourcedVendorDeleted = "party.outsourced-vendor.deleted";
    public const string StaleRowVersion = "party.outsourced-vendor.stale-row-version";
    public const string IdempotencyKeyReused = "party.outsourced-vendor.idempotency-key-reused";
}

public static class OutsourcedVendorMasterValidation
{
    public static ApplicationError? Validate(CreateOutsourcedVendorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateOutsourcedVendorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.OutsourcedVendorId != Guid.Empty
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
            OutsourcedVendorMasterErrorCodes.InvalidInput);
}
