namespace YowThi.Erp.Application.Common.Idempotency;

public sealed class CommandExecutionAcquireResult
{
    private readonly CommandResultSnapshot? _replaySnapshot;

    private CommandExecutionAcquireResult(
        CommandExecutionAcquireStatus status,
        CommandResultSnapshot? replaySnapshot)
    {
        Status = status;
        _replaySnapshot = replaySnapshot;
    }

    public CommandExecutionAcquireStatus Status { get; }

    public CommandResultSnapshot ReplaySnapshot => Status == CommandExecutionAcquireStatus.Replay
        ? _replaySnapshot!
        : throw new InvalidOperationException("Only replay results contain a stored command result snapshot.");

    public static CommandExecutionAcquireResult Acquired() =>
        new(CommandExecutionAcquireStatus.Acquired, null);

    public static CommandExecutionAcquireResult Replay(CommandResultSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new CommandExecutionAcquireResult(CommandExecutionAcquireStatus.Replay, snapshot);
    }

    public static CommandExecutionAcquireResult Conflict() =>
        new(CommandExecutionAcquireStatus.Conflict, null);
}
