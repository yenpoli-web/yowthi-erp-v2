using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.SalesHandling;

public sealed record CreateSalesPackagingItemCommand(
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record CreateSalesPackagingItemExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateSalesPackagingItemCommand Command);

public sealed record UpdateSalesPackagingItemCommand(
    Guid SalesPackagingItemId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record UpdateSalesPackagingItemExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateSalesPackagingItemCommand Command);

public sealed record SalesPackagingItemMasterWriteResult(
    Guid SalesPackagingItemId,
    long RowVersion);

public interface ISalesPackagingItemMasterExecutor
{
    ValueTask<ApplicationResult<SalesPackagingItemMasterWriteResult>> CreateAsync(
        CreateSalesPackagingItemExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SalesPackagingItemMasterWriteResult>> UpdateAsync(
        UpdateSalesPackagingItemExecution execution,
        CancellationToken cancellationToken);
}

public enum SalesPackagingItemMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record SalesPackagingItemMasterQuery(
    string? Search,
    SalesPackagingItemMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record SalesPackagingItemMasterItem(
    Guid Id,
    string? NameZhTw,
    string? NameThTh,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesPackagingItemMasterPage(
    IReadOnlyList<SalesPackagingItemMasterItem> Items,
    int? NextOffset);

public interface ISalesPackagingItemMasterReader
{
    ValueTask<SalesPackagingItemMasterPage> GetAsync(
        SalesPackagingItemMasterQuery query,
        CancellationToken cancellationToken);
}

public static class SalesPackagingItemMasterErrorCodes
{
    public const string InvalidInput = "sales-handling.packaging-item.invalid-input";
    public const string ItemNotFound = "sales-handling.packaging-item.not-found";
    public const string ItemDeleted = "sales-handling.packaging-item.deleted";
    public const string StaleRowVersion = "sales-handling.packaging-item.stale-row-version";
    public const string IdempotencyKeyReused = "sales-handling.packaging-item.idempotency-key-reused";
}

public static class SalesPackagingItemMasterValidation
{
    public static ApplicationError? Validate(CreateSalesPackagingItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateSalesPackagingItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.SalesPackagingItemId != Guid.Empty
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
            SalesPackagingItemMasterErrorCodes.InvalidInput);
}
