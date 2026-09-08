using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Security;

public sealed record CreateSecurityAccountCommand(
    string DisplayName,
    bool Active,
    string? IdentityIssuer,
    string? IdentitySubject,
    IReadOnlyList<string> Capabilities);

public sealed record CreateSecurityAccountExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateSecurityAccountCommand Command);

public sealed record UpdateSecurityAccountCommand(
    Guid AccountId,
    long ExpectedRowVersion,
    string DisplayName,
    bool Active,
    string? IdentityIssuer,
    string? IdentitySubject,
    IReadOnlyList<string> Capabilities);

public sealed record UpdateSecurityAccountExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateSecurityAccountCommand Command);

public sealed record SecurityAccountWriteResult(Guid AccountId, long RowVersion);

public interface ISecurityAccountManagementExecutor
{
    ValueTask<ApplicationResult<SecurityAccountWriteResult>> CreateAsync(
        CreateSecurityAccountExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SecurityAccountWriteResult>> UpdateAsync(
        UpdateSecurityAccountExecution execution,
        CancellationToken cancellationToken);
}

public sealed record SecurityAccountItem(
    Guid Id,
    string DisplayName,
    bool Active,
    string? IdentityIssuer,
    string? IdentitySubject,
    long RowVersion,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Capabilities,
    bool IsDevelopmentTestAdmin);

public sealed record SecurityAccountManagementPage(
    IReadOnlyList<SecurityAccountItem> Items,
    IReadOnlyList<string> AvailableCapabilities);

public interface ISecurityAccountManagementReader
{
    ValueTask<SecurityAccountManagementPage> GetAsync(
        string? search,
        CancellationToken cancellationToken);
}

public sealed record SecurityResolvedActor(
    Guid AccountId,
    string DisplayName,
    IReadOnlyList<string> Capabilities);

public interface ISecurityActorResolver
{
    ValueTask<SecurityResolvedActor?> ResolveAsync(
        Guid accountId,
        CancellationToken cancellationToken);
}

public interface IDevelopmentTestAdminProvisioner
{
    ValueTask<SecurityResolvedActor> EnsureAsync(CancellationToken cancellationToken);
}

public static class SecurityAccountErrorCodes
{
    public const string InvalidInput = "security.account.invalid-input";
    public const string AccountNotFound = "security.account.not-found";
    public const string StaleRowVersion = "security.account.stale-row-version";
    public const string IdempotencyKeyReused = "security.account.idempotency-key-reused";
    public const string ExternalIdentityConflict = "security.account.external-identity-conflict";
}

public static class SecurityAccountValidation
{
    public static ApplicationError? Validate(CreateSecurityAccountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return IsValid(command.DisplayName, command.IdentityIssuer, command.IdentitySubject, command.Capabilities)
            ? null
            : Invalid();
    }

    public static ApplicationError? Validate(UpdateSecurityAccountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.AccountId != Guid.Empty
            && command.ExpectedRowVersion >= 1
            && IsValid(command.DisplayName, command.IdentityIssuer, command.IdentitySubject, command.Capabilities)
                ? null
                : Invalid();
    }

    private static bool IsValid(
        string displayName,
        string? identityIssuer,
        string? identitySubject,
        IReadOnlyList<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(displayName) || capabilities is null)
        {
            return false;
        }

        var hasIssuer = !string.IsNullOrWhiteSpace(identityIssuer);
        var hasSubject = !string.IsNullOrWhiteSpace(identitySubject);
        if (hasIssuer != hasSubject)
        {
            return false;
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability)
                || !SecurityCapabilities.IsKnown(capability)
                || !distinct.Add(capability))
            {
                return false;
            }
        }

        return true;
    }

    private static ApplicationError Invalid() =>
        ApplicationError.Create(ApplicationErrorKind.Validation, SecurityAccountErrorCodes.InvalidInput);
}
