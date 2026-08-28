using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using YowThi.Erp.Application.Common.Audit;

namespace YowThi.Erp.Infrastructure.Persistence.Audit;

internal static class AuditPersistenceConversions
{
    public static ValueConverter<AuditEventKind, string> EventKind { get; } = new(
        value => ToDatabase(value),
        value => FromDatabaseEventKind(value));

    public static ValueConverter<AuditChangeKind, string> ChangeKind { get; } = new(
        value => ToDatabase(value),
        value => FromDatabaseChangeKind(value));

    public static ValueConverter<AuditCorrectionMode, string> CorrectionMode { get; } = new(
        value => ToDatabase(value),
        value => FromDatabaseCorrectionMode(value));

    private static string ToDatabase(AuditEventKind value) => value switch
    {
        AuditEventKind.BusinessCommand => "BUSINESS_COMMAND",
        AuditEventKind.Correction => "CORRECTION",
        AuditEventKind.DataLifecycle => "DATA_LIFECYCLE",
        AuditEventKind.HardDelete => "HARD_DELETE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported audit event kind."),
    };

    private static AuditEventKind FromDatabaseEventKind(string value) => value switch
    {
        "BUSINESS_COMMAND" => AuditEventKind.BusinessCommand,
        "CORRECTION" => AuditEventKind.Correction,
        "DATA_LIFECYCLE" => AuditEventKind.DataLifecycle,
        "HARD_DELETE" => AuditEventKind.HardDelete,
        _ => throw new InvalidOperationException($"Unsupported audit event kind '{value}'."),
    };

    private static string ToDatabase(AuditChangeKind value) => value switch
    {
        AuditChangeKind.Create => "CREATE",
        AuditChangeKind.Update => "UPDATE",
        AuditChangeKind.SoftDelete => "SOFT_DELETE",
        AuditChangeKind.Restore => "RESTORE",
        AuditChangeKind.HardDelete => "HARD_DELETE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported audit change kind."),
    };

    private static AuditChangeKind FromDatabaseChangeKind(string value) => value switch
    {
        "CREATE" => AuditChangeKind.Create,
        "UPDATE" => AuditChangeKind.Update,
        "SOFT_DELETE" => AuditChangeKind.SoftDelete,
        "RESTORE" => AuditChangeKind.Restore,
        "HARD_DELETE" => AuditChangeKind.HardDelete,
        _ => throw new InvalidOperationException($"Unsupported audit change kind '{value}'."),
    };

    private static string ToDatabase(AuditCorrectionMode value) => value switch
    {
        AuditCorrectionMode.DirectAmendment => "DIRECT_AMENDMENT",
        AuditCorrectionMode.Compensation => "COMPENSATION",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported audit correction mode."),
    };

    private static AuditCorrectionMode FromDatabaseCorrectionMode(string value) => value switch
    {
        "DIRECT_AMENDMENT" => AuditCorrectionMode.DirectAmendment,
        "COMPENSATION" => AuditCorrectionMode.Compensation,
        _ => throw new InvalidOperationException($"Unsupported audit correction mode '{value}'."),
    };
}
