using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Product;

public sealed record CreateSalesProductGroupCommand(
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record CreateSalesProductGroupExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateSalesProductGroupCommand Command);

public sealed record UpdateSalesProductGroupCommand(
    Guid SalesProductGroupId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record UpdateSalesProductGroupExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateSalesProductGroupCommand Command);

public sealed record SalesProductGroupMasterWriteResult(
    Guid SalesProductGroupId,
    long RowVersion);

public interface ISalesProductGroupMasterExecutor
{
    ValueTask<ApplicationResult<SalesProductGroupMasterWriteResult>> CreateAsync(
        CreateSalesProductGroupExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SalesProductGroupMasterWriteResult>> UpdateAsync(
        UpdateSalesProductGroupExecution execution,
        CancellationToken cancellationToken);
}

public enum SalesProductGroupMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record SalesProductGroupMasterQuery(
    string? Search,
    SalesProductGroupMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record SalesProductGroupMasterItem(
    Guid Id,
    string? NameZhTw,
    string? NameThTh,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesProductGroupMasterPage(
    IReadOnlyList<SalesProductGroupMasterItem> Items,
    int? NextOffset);

public interface ISalesProductGroupMasterReader
{
    ValueTask<SalesProductGroupMasterPage> GetAsync(
        SalesProductGroupMasterQuery query,
        CancellationToken cancellationToken);
}

public static class SalesProductGroupMasterErrorCodes
{
    public const string InvalidInput = "product.sales-product-group.invalid-input";
    public const string ItemNotFound = "product.sales-product-group.not-found";
    public const string ItemDeleted = "product.sales-product-group.deleted";
    public const string StaleRowVersion = "product.sales-product-group.stale-row-version";
    public const string IdempotencyKeyReused = "product.sales-product-group.idempotency-key-reused";
}

public static class SalesProductGroupMasterValidation
{
    public static ApplicationError? Validate(CreateSalesProductGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateSalesProductGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.SalesProductGroupId != Guid.Empty
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
            SalesProductGroupMasterErrorCodes.InvalidInput);
}
