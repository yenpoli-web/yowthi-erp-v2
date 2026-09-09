using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Infrastructure;

public sealed record StorageLocationMasterQuery(string? Search, InfrastructureMasterStatusFilter Status, Guid? WarehouseId, int Offset, int Limit, string Locale = "zh-TW");

public sealed record StorageLocationMasterItem(
    Guid Id,
    Guid WarehouseId,
    string WarehouseDisplayName,
    string? Code,
    string? NameZhTw,
    string? NameThTh,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record StorageLocationMasterPage(IReadOnlyList<StorageLocationMasterItem> Items, int? NextOffset);

public sealed record CreateStorageLocationCommand(Guid WarehouseId, string? Code, string? NameZhTw, string? NameThTh, bool Active);
public sealed record UpdateStorageLocationCommand(Guid StorageLocationId, long ExpectedRowVersion, Guid WarehouseId, string? Code, string? NameZhTw, string? NameThTh, bool Active);

public sealed record CreateStorageLocationExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, CreateStorageLocationCommand Command);
public sealed record UpdateStorageLocationExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, UpdateStorageLocationCommand Command);
public sealed record StorageLocationMasterWriteResult(Guid StorageLocationId, long RowVersion);

public sealed record WarehouseMasterOption(Guid Id, string DisplayName, string? Code);
public sealed record WarehouseMasterOptions(IReadOnlyList<WarehouseMasterOption> Items);

public interface IStorageLocationMasterReader
{
    ValueTask<StorageLocationMasterPage> GetAsync(StorageLocationMasterQuery query, CancellationToken cancellationToken);
    ValueTask<WarehouseMasterOptions> GetWarehouseOptionsAsync(string locale, string? search, int limit, CancellationToken cancellationToken);
}

public interface IStorageLocationMasterExecutor
{
    ValueTask<ApplicationResult<StorageLocationMasterWriteResult>> CreateAsync(CreateStorageLocationExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<StorageLocationMasterWriteResult>> UpdateAsync(UpdateStorageLocationExecution execution, CancellationToken cancellationToken);
}

public static class StorageLocationMasterErrorCodes
{
    public const string InvalidInput = "infrastructure.storage-location.invalid-input";
    public const string NotFound = "infrastructure.storage-location.not-found";
    public const string Deleted = "infrastructure.storage-location.deleted";
    public const string WarehouseNotFound = "infrastructure.storage-location.warehouse-not-found";
    public const string StaleRowVersion = "infrastructure.storage-location.stale-row-version";
    public const string IdempotencyKeyReused = "infrastructure.storage-location.idempotency-key-reused";
}

public static class StorageLocationMasterValidation
{
    public static ApplicationError? Validate(CreateStorageLocationCommand command) =>
        command.WarehouseId != Guid.Empty && HasName(command.NameZhTw, command.NameThTh) ? null : Invalid();

    public static ApplicationError? Validate(UpdateStorageLocationCommand command) =>
        command.StorageLocationId != Guid.Empty && command.ExpectedRowVersion >= 1 && command.WarehouseId != Guid.Empty && HasName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();

    private static bool HasName(string? zh, string? th) => !string.IsNullOrWhiteSpace(zh) || !string.IsNullOrWhiteSpace(th);
    private static ApplicationError Invalid() => ApplicationError.Create(ApplicationErrorKind.Validation, StorageLocationMasterErrorCodes.InvalidInput);
}
