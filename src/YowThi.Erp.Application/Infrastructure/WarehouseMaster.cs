using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Infrastructure;

public enum InfrastructureMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record WarehouseMasterQuery(string? Search, InfrastructureMasterStatusFilter Status, int Offset, int Limit);

public sealed record WarehouseMasterItem(
    Guid Id,
    string? Code,
    string? NameZhTw,
    string? NameThTh,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record WarehouseMasterPage(IReadOnlyList<WarehouseMasterItem> Items, int? NextOffset);

public sealed record CreateWarehouseCommand(string? Code, string? NameZhTw, string? NameThTh, bool Active);
public sealed record UpdateWarehouseCommand(Guid WarehouseId, long ExpectedRowVersion, string? Code, string? NameZhTw, string? NameThTh, bool Active);

public sealed record CreateWarehouseExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, CreateWarehouseCommand Command);
public sealed record UpdateWarehouseExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, UpdateWarehouseCommand Command);
public sealed record WarehouseMasterWriteResult(Guid WarehouseId, long RowVersion);

public interface IWarehouseMasterReader
{
    ValueTask<WarehouseMasterPage> GetAsync(WarehouseMasterQuery query, CancellationToken cancellationToken);
}

public interface IWarehouseMasterExecutor
{
    ValueTask<ApplicationResult<WarehouseMasterWriteResult>> CreateAsync(CreateWarehouseExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<WarehouseMasterWriteResult>> UpdateAsync(UpdateWarehouseExecution execution, CancellationToken cancellationToken);
}

public static class WarehouseMasterErrorCodes
{
    public const string InvalidInput = "infrastructure.warehouse.invalid-input";
    public const string NotFound = "infrastructure.warehouse.not-found";
    public const string Deleted = "infrastructure.warehouse.deleted";
    public const string StaleRowVersion = "infrastructure.warehouse.stale-row-version";
    public const string IdempotencyKeyReused = "infrastructure.warehouse.idempotency-key-reused";
}

public static class WarehouseMasterValidation
{
    public static ApplicationError? Validate(CreateWarehouseCommand command) =>
        HasName(command.NameZhTw, command.NameThTh) ? null : Invalid();

    public static ApplicationError? Validate(UpdateWarehouseCommand command) =>
        command.WarehouseId != Guid.Empty && command.ExpectedRowVersion >= 1 && HasName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();

    private static bool HasName(string? zh, string? th) => !string.IsNullOrWhiteSpace(zh) || !string.IsNullOrWhiteSpace(th);
    private static ApplicationError Invalid() => ApplicationError.Create(ApplicationErrorKind.Validation, WarehouseMasterErrorCodes.InvalidInput);
}
