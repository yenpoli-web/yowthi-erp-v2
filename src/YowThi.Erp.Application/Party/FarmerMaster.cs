using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record CreateFarmerCommand(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active);

public sealed record CreateFarmerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateFarmerCommand Command);

public sealed record UpdateFarmerCommand(
    Guid FarmerId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active);

public sealed record UpdateFarmerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateFarmerCommand Command);

public sealed record FarmerMasterWriteResult(
    Guid FarmerId,
    long RowVersion);

public interface IFarmerMasterExecutor
{
    ValueTask<ApplicationResult<FarmerMasterWriteResult>> CreateAsync(
        CreateFarmerExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<FarmerMasterWriteResult>> UpdateAsync(
        UpdateFarmerExecution execution,
        CancellationToken cancellationToken);
}

public enum FarmerMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record FarmerMasterQuery(
    string? Search,
    FarmerMasterStatusFilter Status,
    int Offset,
    int Limit);

public sealed record FarmerMasterItem(
    Guid Id,
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

public sealed record FarmerMasterPage(
    IReadOnlyList<FarmerMasterItem> Items,
    int? NextOffset);

public interface IFarmerMasterReader
{
    ValueTask<FarmerMasterPage> GetAsync(
        FarmerMasterQuery query,
        CancellationToken cancellationToken);
}

public static class FarmerMasterErrorCodes
{
    public const string InvalidInput = "party.farmer.invalid-input";
    public const string FarmerNotFound = "party.farmer.not-found";
    public const string FarmerDeleted = "party.farmer.deleted";
    public const string StaleRowVersion = "party.farmer.stale-row-version";
    public const string IdempotencyKeyReused = "party.farmer.idempotency-key-reused";
}

public static class FarmerMasterValidation
{
    public static ApplicationError? Validate(CreateFarmerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HasVisibleName(command.NameZhTw, command.NameThTh)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateFarmerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.FarmerId != Guid.Empty
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
            FarmerMasterErrorCodes.InvalidInput);
}
